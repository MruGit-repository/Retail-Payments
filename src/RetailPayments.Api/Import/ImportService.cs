using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RetailPayments.Api.Data;
using RetailPayments.Api.Domain;
using RetailPayments.Api.Sources;

namespace RetailPayments.Api.Import;

// lets only one import run at a time; register as a singleton
public class ImportLock
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
}

public interface IImportService
{
    // runs the import for all sources; the endpoint calls it now, a scheduler can later (triggeredBy = "Scheduler")
    Task<ImportBatchResult> RunAsync(string triggeredBy, CancellationToken ct = default);
}

public class ImportService(
    PaymentsDbContext db,
    IEnumerable<ITransactionSource> sources,
    TransactionReconciler reconciler,
    IOptions<ImportOptions> options,
    TimeProvider clock,
    ImportLock gate,
    ILogger<ImportService> logger) : IImportService
{
    public async Task<ImportBatchResult> RunAsync(string triggeredBy, CancellationToken ct = default)
    {
        if (!await gate.Semaphore.WaitAsync(0, ct)) throw new ImportInProgressException();
        try
        {
            return await RunCoreAsync(triggeredBy, ct);
        }
        finally
        {
            gate.Semaphore.Release();
        }
    }

    private async Task<ImportBatchResult> RunCoreAsync(string triggeredBy, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var window = TimeSpan.FromHours(options.Value.FinalizeAfterHours);

        var batch = new ImportBatch { StartedAt = now, Status = ImportBatchStatus.Running, TriggeredBy = triggeredBy };
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Import batch {BatchId} started ({Trigger})", batch.Id, triggeredBy);

        var results = new List<SourceImportResult>();
        foreach (var source in sources)
        {
            results.Add(await ImportSourceAsync(source, batch, now, window, ct));
        }

        var totals = new ReconcileCounts();
        foreach (var result in results)
            totals.Add(result.Counts);

        CompleteBatch(batch, results, totals);
        await db.SaveChangesAsync(CancellationToken.None);

        logger.LogInformation(
            "Import batch {BatchId} {Status}: +{Created} ~{Updated} revoked {Revoked} restored {Restored} finalized {Finalized} unchanged {Unchanged} rejected {Rejected}",
            batch.Id, batch.Status, totals.Created, totals.Updated, totals.Revoked, totals.Restored,
            totals.Finalized, totals.Unchanged, totals.Rejected);

        return new ImportBatchResult(batch.Id, batch.Status.ToString(), batch.TriggeredBy,
            batch.StartedAt, batch.CompletedAt, totals, results);
    }

    private void CompleteBatch(ImportBatch batch, List<SourceImportResult> results, ReconcileCounts totals)
    {
        var failures = results.Where(r => !r.Succeeded).ToList();

        batch.Status = failures.Count == 0 ? ImportBatchStatus.Succeeded
            : failures.Count == results.Count ? ImportBatchStatus.Failed
            : ImportBatchStatus.PartiallySucceeded;
        batch.CompletedAt = clock.GetUtcNow().UtcDateTime;

        batch.Created = totals.Created;
        batch.Updated = totals.Updated;
        batch.Revoked = totals.Revoked;
        batch.Restored = totals.Restored;
        batch.Finalized = totals.Finalized;
        batch.Unchanged = totals.Unchanged;
        batch.Rejected = totals.Rejected;

        batch.Details = JsonSerializer.Serialize(results);
        batch.Error = failures.Count == 0
            ? null
            : string.Join("; ", failures.Select(f => $"{f.SourceSystem}: {f.Error}"));
    }

    // Fetch + reconcile one source. If it fails we report it and leave that source's data alone
    // (a failed fetch must never revoke anything); the other sources still run.
    // One SaveChanges per source, so its changes commit together.
    private async Task<SourceImportResult> ImportSourceAsync(
        ITransactionSource source, ImportBatch batch, DateTime now, TimeSpan window, CancellationToken ct)
    {
        var timeoutSeconds = options.Value.SourceTimeoutSeconds;
        using var fetchTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        fetchTimeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        IReadOnlyList<SourceTransaction> snapshot;
        try
        {
            snapshot = await source.GetSnapshotAsync(fetchTimeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            var message = $"timed out after {timeoutSeconds}s";
            logger.LogError("Fetching snapshot from {Source} {Message}", source.SourceSystem, message);
            return new SourceImportResult(source.SourceSystem, false, message, 0, new ReconcileCounts());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Fetching snapshot from {Source} failed", source.SourceSystem);
            return new SourceImportResult(source.SourceSystem, false, ex.Message, 0, new ReconcileCounts());
        }

        try
        {
            var existing = await db.Transactions
                .Where(t => t.SourceSystem == source.SourceSystem)
                .ToListAsync(ct);

            var counts = reconciler.Reconcile(db, source.SourceSystem, snapshot, existing, batch, now, window);
            await db.SaveChangesAsync(ct);
            return new SourceImportResult(source.SourceSystem, true, null, snapshot.Count, counts);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Reconciling {Source} failed; changes for this source were rolled back", source.SourceSystem);
            db.ChangeTracker.Clear();
            db.ImportBatches.Attach(batch);
            return new SourceImportResult(source.SourceSystem, false, ex.Message, snapshot.Count, new ReconcileCounts());
        }
    }
}

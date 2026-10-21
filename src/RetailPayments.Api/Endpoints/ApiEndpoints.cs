using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailPayments.Api.Data;
using RetailPayments.Api.Domain;
using RetailPayments.Api.Import;
using RetailPayments.Api.Sources;

namespace RetailPayments.Api.Endpoints;

public static class ApiEndpoints
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 200;

    private static readonly string[] SortFields =
        ["sourceSystem", "transactionId", "locationCode", "productName", "amount", "transactionTime", "status"];

    public static void MapApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/import", RunImport);
        api.MapGet("/imports", GetImports);
        api.MapGet("/imports/{batchId:int}/rejections", GetRejections);
        api.MapGet("/transactions", GetTransactions);
        api.MapGet("/transactions/{sourceSystem}/{transactionId:int}", GetTransaction);
    }

    // dev only: moves the mock sources between scenario steps
    public static void MapDevApi(this IEndpointRouteBuilder app)
    {
        var dev = app.MapGroup("/api/dev/scenario");
        dev.MapGet("", GetScenario);
        dev.MapPost("/{step:int}", SetScenario);
    }

    private static async Task<IResult> RunImport(IImportService importService, CancellationToken ct)
    {
        try
        {
            var result = await importService.RunAsync("Manual", ct);
            return Results.Ok(result);
        }
        catch (ImportInProgressException)
        {
            return Results.Problem(
                title: "Import already running",
                detail: "Another import is in progress. Try again when it completes.",
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> GetImports(PaymentsDbContext db, [FromQuery] int? take, CancellationToken ct)
    {
        var batches = await db.ImportBatches.AsNoTracking()
            .OrderByDescending(b => b.Id)
            .Take(Math.Clamp(take ?? 20, 1, 100))
            .ToListAsync(ct);

        return Results.Ok(batches.Select(ToDto).ToList());
    }

    private static async Task<IResult> GetRejections(int batchId, PaymentsDbContext db, CancellationToken ct)
    {
        if (!await db.ImportBatches.AnyAsync(b => b.Id == batchId, ct))
            return Results.NotFound();

        var rejected = await db.RejectedRecords.AsNoTracking()
            .Where(r => r.ImportBatchId == batchId)
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

        return Results.Ok(rejected.Select(ToDto).ToList());
    }

    private static async Task<IResult> GetTransactions(
        PaymentsDbContext db,
        [FromQuery] string? sourceSystem,
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        TransactionStatus? statusFilter = null;
        if (status is not null)
        {
            if (!Enum.TryParse<TransactionStatus>(status, true, out var parsed))
                return Invalid("status", "Unknown status.");
            statusFilter = parsed;
        }

        if (sortBy is not null && !SortFields.Contains(sortBy, StringComparer.OrdinalIgnoreCase))
            return Invalid("sortBy", $"Use one of: {string.Join(", ", SortFields)}.");

        var query = db.Transactions.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(sourceSystem))
            query = query.Where(t => t.SourceSystem == sourceSystem);

        if (statusFilter is not null)
            query = query.Where(t => t.Status == statusFilter);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(t => EF.Functions.Like(t.ProductName, pattern)
                                  || EF.Functions.Like(t.LocationCode, pattern)
                                  || EF.Functions.Like(t.CardNumber, pattern)
                                  || EF.Functions.Like(t.SourceSystem, pattern));
        }

        var descending = !string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
        var ordered = ApplySort(query, sortBy, descending).ThenBy(t => t.Id); // Id keeps page order stable

        var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
        var pageNumber = Math.Max(page ?? 1, 1);

        var total = await ordered.CountAsync(ct);
        var rows = await ordered.Skip((pageNumber - 1) * size).Take(size).ToListAsync(ct);

        return Results.Ok(new PagedResult<TransactionDto>(rows.Select(ToDto).ToList(), total, pageNumber, size));
    }

    private static async Task<IResult> GetTransaction(
        string sourceSystem, int transactionId, PaymentsDbContext db, CancellationToken ct)
    {
        var transaction = await db.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.SourceSystem == sourceSystem && t.TransactionId == transactionId, ct);
        if (transaction is null)
            return Results.NotFound();

        var audit = await db.AuditEntries.AsNoTracking()
            .Where(a => a.TransactionKey == transaction.Id)
            .OrderBy(a => a.Id)
            .ToListAsync(ct);

        return Results.Ok(new TransactionDetailDto(ToDto(transaction), audit.Select(ToDto).ToList()));
    }

    private static IResult GetScenario(IScenario scenario) =>
        Results.Ok(new { step = scenario.Step, maxStep = scenario.MaxStep });

    private static IResult SetScenario(int step, IScenario scenario)
    {
        if (step < 0 || step > scenario.MaxStep)
            return Results.Problem($"step must be between 0 and {scenario.MaxStep}", statusCode: 400);

        scenario.Step = step;
        return Results.Ok(new { step = scenario.Step, maxStep = scenario.MaxStep });
    }

    private static IOrderedQueryable<Transaction> ApplySort(IQueryable<Transaction> query, string? sortBy, bool descending)
    {
        return (sortBy ?? "transactionTime").ToLowerInvariant() switch
        {
            "sourcesystem" => descending ? query.OrderByDescending(t => t.SourceSystem) : query.OrderBy(t => t.SourceSystem),
            "transactionid" => descending ? query.OrderByDescending(t => t.TransactionId) : query.OrderBy(t => t.TransactionId),
            "locationcode" => descending ? query.OrderByDescending(t => t.LocationCode) : query.OrderBy(t => t.LocationCode),
            "productname" => descending ? query.OrderByDescending(t => t.ProductName) : query.OrderBy(t => t.ProductName),
            "amount" => descending ? query.OrderByDescending(t => t.Amount) : query.OrderBy(t => t.Amount),
            "status" => descending ? query.OrderByDescending(t => t.Status) : query.OrderBy(t => t.Status),
            _ => descending ? query.OrderByDescending(t => t.TransactionTime) : query.OrderBy(t => t.TransactionTime),
        };
    }

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static TransactionDto ToDto(Transaction t) => new(
        t.Id, t.SourceSystem, t.TransactionId, t.CardNumber, t.LocationCode, t.ProductName, t.Amount,
        t.TransactionTime, t.Status.ToString(), t.Revision, t.FirstImportedAt, t.LastUpdatedAt);

    private static AuditDto ToDto(AuditEntry a) => new(
        a.Id, a.ImportBatchId, a.ChangeType.ToString(), a.FieldName, a.OldValue, a.NewValue,
        a.Revision, a.OccurredAt);

    private static ImportBatchDto ToDto(ImportBatch b) => new(
        b.Id, b.StartedAt, b.CompletedAt, b.Status.ToString(), b.TriggeredBy, b.Created, b.Updated,
        b.Revoked, b.Restored, b.Finalized, b.Unchanged, b.Rejected, b.Error);

    private static RejectionDto ToDto(RejectedRecord r) => new(
        r.Id, r.ImportBatchId, r.SourceSystem, r.TransactionId, r.Reason, r.Payload, r.RejectedAt);
}

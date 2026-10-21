using RetailPayments.Api.Data;
using RetailPayments.Api.Domain;
using RetailPayments.Api.Sources;
using System.Globalization;
using System.Text.Json;

namespace RetailPayments.Api.Import;

// Compares one source's snapshot with what we have stored for that source.
//   new id                          -> insert, audit Created
//   values differ                   -> update, audit FieldChanged per field
//   revoked id shows up again       -> Restored
//   active id missing, inside window -> Revoked
//   active id past the window       -> Finalized, ignored from then on
//   revoked/missing past the window -> left alone
// Invalid records still count as "present" so a bad correction can't revoke the real one.
public class TransactionReconciler(ILogger<TransactionReconciler> logger)
{
    private const decimal MaxAmount = 10_000_000_000_000_000m;
    private const int MaxPayloadLength = 2000;

    public ReconcileCounts Reconcile(
        PaymentsDbContext db,
        string sourceSystem,
        IReadOnlyList<SourceTransaction> snapshot,
        IReadOnlyList<Transaction> existing,
        ImportBatch batch,
        DateTime now,
        TimeSpan window)
    {
        var counts = new ReconcileCounts();
        var cutoff = now - window;

        var (valid, present) = ReadSnapshot(db, sourceSystem, snapshot, batch, now, counts);

        foreach (var tx in existing)
            ReconcileExisting(db, batch, tx, valid, present, cutoff, now, counts);

        var knownIds = existing.Select(t => t.TransactionId).ToHashSet();
        foreach (var src in valid.Values.Where(s => !knownIds.Contains(s.TransactionId)))
            InsertNew(db, batch, sourceSystem, src, cutoff, now, counts);

        return counts;
    }

    // splits the snapshot into valid records and the ids that were present at all
    private (Dictionary<int, SourceTransaction> Valid, HashSet<int> Present) ReadSnapshot(
        PaymentsDbContext db, string sourceSystem, IReadOnlyList<SourceTransaction> snapshot,
        ImportBatch batch, DateTime now, ReconcileCounts counts)
    {
        var valid = new Dictionary<int, SourceTransaction>();
        var present = new HashSet<int>();

        foreach (var raw in snapshot)
        {
            present.Add(raw.TransactionId);

            var error = Validate(sourceSystem, raw);
            if (error is null && valid.ContainsKey(raw.TransactionId))
                error = "duplicate transaction id in snapshot";

            if (error is not null)
            {
                Reject(db, batch, sourceSystem, raw, error, now);
                counts.Rejected++;
                continue;
            }

            valid[raw.TransactionId] = Normalize(raw);
        }

        return (valid, present);
    }

    private void Reject(
        PaymentsDbContext db, ImportBatch batch, string sourceSystem, SourceTransaction raw, string error, DateTime now)
    {
        logger.LogWarning("Rejected {Source}/{Id}: {Error}", sourceSystem, raw.TransactionId, error);

        var payload = JsonSerializer.Serialize(raw);
        db.RejectedRecords.Add(new RejectedRecord
        {
            ImportBatch = batch,
            SourceSystem = sourceSystem,
            TransactionId = raw.TransactionId,
            Reason = error,
            Payload = payload.Length <= MaxPayloadLength ? payload : payload[..MaxPayloadLength],
            RejectedAt = now,
        });
    }

    private static void ReconcileExisting(
        PaymentsDbContext db, ImportBatch batch, Transaction tx,
        Dictionary<int, SourceTransaction> valid, HashSet<int> present,
        DateTime cutoff, DateTime now, ReconcileCounts counts)
    {
        if (tx.Status == TransactionStatus.Finalized)
        {
            counts.Unchanged++;
            return;
        }

        // past the window: active ones get finalized, revoked ones stay as they are
        if (tx.TransactionTime < cutoff)
        {
            if (tx.Status == TransactionStatus.Active)
            {
                SetStatus(db, batch, tx, TransactionStatus.Finalized, AuditChangeType.Finalized, now);
                counts.Finalized++;
            }
            else
            {
                counts.Unchanged++;
            }
            return;
        }

        // missing from the snapshot
        if (!present.Contains(tx.TransactionId))
        {
            if (tx.Status == TransactionStatus.Active)
            {
                SetStatus(db, batch, tx, TransactionStatus.Revoked, AuditChangeType.Revoked, now);
                counts.Revoked++;
            }
            else
            {
                counts.Unchanged++;
            }
            return;
        }

        // in the snapshot but the record was rejected, keep what we have
        if (!valid.TryGetValue(tx.TransactionId, out var src))
        {
            counts.Unchanged++;
            return;
        }

        var changed = false;

        if (tx.Status == TransactionStatus.Revoked)
        {
            SetStatus(db, batch, tx, TransactionStatus.Active, AuditChangeType.Restored, now);
            counts.Restored++;
            changed = true;
        }

        if (ApplyFieldChanges(db, batch, tx, src, now))
        {
            counts.Updated++;
            changed = true;
        }

        if (!changed)
            counts.Unchanged++;
    }

    private static void InsertNew(
        PaymentsDbContext db, ImportBatch batch, string sourceSystem, SourceTransaction src,
        DateTime cutoff, DateTime now, ReconcileCounts counts)
    {
        var tx = new Transaction
        {
            SourceSystem = sourceSystem,
            TransactionId = src.TransactionId,
            CardNumber = src.CardNumber!,
            LocationCode = src.LocationCode!,
            ProductName = src.ProductName!,
            Amount = src.Amount,
            TransactionTime = src.TransactionTime,
            Status = TransactionStatus.Active,
            Revision = 1,
            FirstImportedAt = now,
            LastUpdatedAt = now,
        };
        db.Transactions.Add(tx);
        AddAudit(db, batch, tx, AuditChangeType.Created, null, null, nameof(TransactionStatus.Active), now);
        counts.Created++;

        // already too old when it arrived
        if (tx.TransactionTime < cutoff)
        {
            SetStatus(db, batch, tx, TransactionStatus.Finalized, AuditChangeType.Finalized, now);
            counts.Finalized++;
        }
    }

    private static string? Validate(string sourceSystem, SourceTransaction t)
    {
        if (!string.Equals(t.SourceSystem, sourceSystem, StringComparison.Ordinal))
            return $"sourceSystem '{t.SourceSystem}' does not match feed '{sourceSystem}'";
        if (t.TransactionId <= 0)
            return "transactionId must be positive";
        if ((t.CardNumber?.Trim().Length ?? 0) > FieldLimits.Text)
            return "cardNumber too long";
        if (string.IsNullOrWhiteSpace(t.LocationCode) || t.LocationCode.Trim().Length > FieldLimits.Text)
            return "locationCode missing or too long";
        if (string.IsNullOrWhiteSpace(t.ProductName) || t.ProductName.Trim().Length > FieldLimits.ProductName)
            return "productName missing or too long";
        if (Math.Abs(t.Amount) >= MaxAmount)
            return "amount out of range";
        if (t.TransactionTime == default)
            return "transactionTime missing";
        return null;
    }

    private static SourceTransaction Normalize(SourceTransaction t) => t with
    {
        CardNumber = t.CardNumber?.Trim() ?? "",
        LocationCode = t.LocationCode!.Trim(),
        ProductName = t.ProductName!.Trim(),
        Amount = decimal.Round(t.Amount, 2, MidpointRounding.AwayFromZero),
        TransactionTime = t.TransactionTime.Kind == DateTimeKind.Utc
            ? t.TransactionTime
            : t.TransactionTime.ToUniversalTime(),
    };

    // copies changed fields onto tx, returns false if nothing differed
    private static bool ApplyFieldChanges(
        PaymentsDbContext db, ImportBatch batch, Transaction tx, SourceTransaction src, DateTime now)
    {
        var changes = new List<(string Field, string OldValue, string NewValue)>();

        if (tx.CardNumber != src.CardNumber)
        {
            changes.Add((nameof(tx.CardNumber), tx.CardNumber, src.CardNumber!));
            tx.CardNumber = src.CardNumber!;
        }
        if (tx.LocationCode != src.LocationCode)
        {
            changes.Add((nameof(tx.LocationCode), tx.LocationCode, src.LocationCode!));
            tx.LocationCode = src.LocationCode!;
        }
        if (tx.ProductName != src.ProductName)
        {
            changes.Add((nameof(tx.ProductName), tx.ProductName, src.ProductName!));
            tx.ProductName = src.ProductName!;
        }
        if (tx.Amount != src.Amount)
        {
            changes.Add((nameof(tx.Amount), FormatMoney(tx.Amount), FormatMoney(src.Amount)));
            tx.Amount = src.Amount;
        }
        if (tx.TransactionTime != src.TransactionTime)
        {
            changes.Add((nameof(tx.TransactionTime), FormatTime(tx.TransactionTime), FormatTime(src.TransactionTime)));
            tx.TransactionTime = src.TransactionTime;
        }

        if (changes.Count == 0)
            return false;

        tx.Revision++;
        tx.LastUpdatedAt = now;
        foreach (var change in changes)
            AddAudit(db, batch, tx, AuditChangeType.FieldChanged, change.Field, change.OldValue, change.NewValue, now);

        return true;
    }

    private static string FormatMoney(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatTime(DateTime value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static void SetStatus(
        PaymentsDbContext db, ImportBatch batch, Transaction tx,
        TransactionStatus to, AuditChangeType type, DateTime now)
    {
        var from = tx.Status;
        tx.Status = to;
        tx.Revision++;
        tx.LastUpdatedAt = now;
        AddAudit(db, batch, tx, type, nameof(Transaction.Status), from.ToString(), to.ToString(), now);
    }

    private static void AddAudit(
        PaymentsDbContext db, ImportBatch batch, Transaction tx,
        AuditChangeType type, string? field, string? oldValue, string? newValue, DateTime now)
    {
        db.AuditEntries.Add(new AuditEntry
        {
            Transaction = tx,
            ImportBatch = batch,
            ChangeType = type,
            FieldName = field,
            OldValue = oldValue,
            NewValue = newValue,
            Revision = tx.Revision,
            OccurredAt = now,
        });
    }
}

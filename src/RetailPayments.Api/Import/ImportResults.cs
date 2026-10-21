namespace RetailPayments.Api.Import;

// counts from reconciling one source
public class ReconcileCounts
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Revoked { get; set; }
    public int Restored { get; set; }
    public int Finalized { get; set; }
    public int Unchanged { get; set; }
    public int Rejected { get; set; }

    public void Add(ReconcileCounts o)
    {
        Created += o.Created; Updated += o.Updated; Revoked += o.Revoked; Restored += o.Restored;
        Finalized += o.Finalized; Unchanged += o.Unchanged; Rejected += o.Rejected;
    }
}

public record SourceImportResult(
    string SourceSystem,
    bool Succeeded,
    string? Error,
    int Received,
    ReconcileCounts Counts);

public record ImportBatchResult(
    int BatchId,
    string Status,
    string TriggeredBy,
    DateTime StartedAt,
    DateTime? CompletedAt,
    ReconcileCounts Totals,
    IReadOnlyList<SourceImportResult> Sources);

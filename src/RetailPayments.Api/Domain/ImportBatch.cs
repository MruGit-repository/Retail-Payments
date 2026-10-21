namespace RetailPayments.Api.Domain;

public enum ImportBatchStatus
{
    Running = 0,
    Succeeded = 1,
    // at least one source failed, the others still ran
    PartiallySucceeded = 2,
    Failed = 3,
}

// one run of the import, manual or scheduled
public class ImportBatch
{
    public int Id { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public ImportBatchStatus Status { get; set; }
    public string TriggeredBy { get; set; } = "";

    public int Created { get; set; }
    public int Updated { get; set; }
    public int Revoked { get; set; }
    public int Restored { get; set; }
    public int Finalized { get; set; }
    public int Unchanged { get; set; }
    public int Rejected { get; set; }

    // per-source results as JSON
    public string? Details { get; set; }
    public string? Error { get; set; }
}

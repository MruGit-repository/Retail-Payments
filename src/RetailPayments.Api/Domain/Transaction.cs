namespace RetailPayments.Api.Domain;

public enum TransactionStatus
{
    // in the latest snapshot, can still be corrected
    Active = 0,
    // dropped from the snapshot while still inside the window
    Revoked = 1,
    // past the window, no further changes
    Finalized = 2,
}

// card transaction, identified by SourceSystem + TransactionId
public class Transaction
{
    public int Id { get; set; }

    public string SourceSystem { get; set; } = "";
    public int TransactionId { get; set; }
    public string CardNumber { get; set; } = "";
    public string LocationCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime TransactionTime { get; set; }

    public TransactionStatus Status { get; set; }
    // starts at 1, +1 on every change
    public int Revision { get; set; } = 1;
    public DateTime FirstImportedAt { get; set; }
    public DateTime LastUpdatedAt { get; set; }

    public List<AuditEntry> AuditEntries { get; set; } = new();
}

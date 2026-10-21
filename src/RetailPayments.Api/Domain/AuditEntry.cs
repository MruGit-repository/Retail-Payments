namespace RetailPayments.Api.Domain;

public enum AuditChangeType
{
    Created = 0,
    FieldChanged = 1,
    Revoked = 2,
    Restored = 3,
    Finalized = 4,
}

// one row per change found during an import, never updated or deleted
public class AuditEntry
{
    public int Id { get; set; }

    public int TransactionKey { get; set; }
    public Transaction? Transaction { get; set; }

    public int ImportBatchId { get; set; }
    public ImportBatch? ImportBatch { get; set; }

    public AuditChangeType ChangeType { get; set; }
    // field name, or "Status" for revoke/restore/finalize
    public string? FieldName { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }

    // revision of the transaction after this change
    public int Revision { get; set; }
    public DateTime OccurredAt { get; set; }
}

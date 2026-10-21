namespace RetailPayments.Api.Endpoints;

// JSON shapes returned by the API
public record TransactionDto(
    int Id, string SourceSystem, int TransactionId, string CardNumber, string LocationCode,
    string ProductName, decimal Amount, DateTime TransactionTime, string Status, int Revision,
    DateTime FirstImportedAt, DateTime LastUpdatedAt);

public record AuditDto(
    int Id, int ImportBatchId, string ChangeType, string? FieldName, string? OldValue,
    string? NewValue, int Revision, DateTime OccurredAt);

public record RejectionDto(
    int Id, int ImportBatchId, string SourceSystem, int TransactionId, string Reason, string Payload,
    DateTime RejectedAt);

public record TransactionDetailDto(TransactionDto Transaction, IReadOnlyList<AuditDto> Audit);

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public record ImportBatchDto(
    int Id, DateTime StartedAt, DateTime? CompletedAt, string Status, string TriggeredBy,
    int Created, int Updated, int Revoked, int Restored, int Finalized, int Unchanged, int Rejected,
    string? Error);

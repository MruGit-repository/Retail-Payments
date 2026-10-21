namespace RetailPayments.Api.Sources;

// One per source system. GetSnapshotAsync has to return everything the source currently
// publishes for the window, otherwise missing ids get revoked.
public interface ITransactionSource
{
    string SourceSystem { get; }
    Task<IReadOnlyList<SourceTransaction>> GetSnapshotAsync(CancellationToken ct);
}

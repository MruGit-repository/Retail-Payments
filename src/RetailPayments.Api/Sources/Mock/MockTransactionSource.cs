namespace RetailPayments.Api.Sources.Mock;

// fake source for local runs, reads from MockScenario
public class MockTransactionSource(string sourceSystem, MockScenario scenario) : ITransactionSource
{
    public string SourceSystem { get; } = sourceSystem;

    public Task<IReadOnlyList<SourceTransaction>> GetSnapshotAsync(CancellationToken ct) =>
        Task.FromResult(scenario.Snapshot(SourceSystem));
}

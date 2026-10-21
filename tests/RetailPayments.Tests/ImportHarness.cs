using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RetailPayments.Api.Data;
using RetailPayments.Api.Import;
using RetailPayments.Api.Sources;

namespace RetailPayments.Tests;

public class StubSource(string name) : ITransactionSource
{
    public string SourceSystem { get; } = name;
    public List<SourceTransaction> Data { get; set; } = new();
    public Exception? Failure { get; set; }
    public bool Hang { get; set; }

    public async Task<IReadOnlyList<SourceTransaction>> GetSnapshotAsync(CancellationToken ct)
    {
        if (Hang)
            await Task.Delay(Timeout.Infinite, ct);
        if (Failure is not null)
            throw Failure;
        return Data.ToList();
    }
}

// in-memory SQLite with the real migrations, a fake clock and stub sources
public sealed class ImportHarness : IDisposable
{
    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private readonly ImportLock _lock = new();

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
    public StubSource Uptown { get; } = new("Store Uptown");
    public StubSource Downtown { get; } = new("Store Downtown");
    public StubSource Online { get; } = new("Online");

    public ImportHarness()
    {
        _conn.Open();
        using var db = NewDb();
        db.Database.Migrate();
    }

    public DateTime Now => Clock.GetUtcNow().UtcDateTime;

    public PaymentsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<PaymentsDbContext>().UseSqlite(_conn).Options);

    // builds a source record that happened hoursAgo before the fake "now"
    public SourceTransaction Tx(StubSource s, int id, double hoursAgo = 1, decimal amount = 10m,
        string product = "Widget", string card = "***1111", string location = "LOC") =>
        new(s.SourceSystem, id, card, location, product, amount, Now.AddHours(-hoursAgo));

    public Task<ImportBatchResult> RunImport(string trigger = "Test", ImportOptions? options = null)
    {
        var db = NewDb();
        var svc = new ImportService(db, [Uptown, Downtown, Online], new TransactionReconciler(NullLogger<TransactionReconciler>.Instance),
            Options.Create(options ?? new ImportOptions()), Clock, _lock, NullLogger<ImportService>.Instance);
        return svc.RunAsync(trigger);
    }

    public void Dispose() => _conn.Dispose();
}

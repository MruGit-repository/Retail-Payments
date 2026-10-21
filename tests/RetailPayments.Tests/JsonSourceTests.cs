using System.Text.Json;
using RetailPayments.Api.Sources.JsonFile;

namespace RetailPayments.Tests;

public class JsonFileTransactionSourceTests : IDisposable
{
    private static readonly DateTime Anchor = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
    private readonly string _dir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"rp-json-{Guid.NewGuid():N}")).FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    // wraps the records in the shared file shape: { "Online": [ ... ] }
    private JsonFileTransactionSource SourceFor(string recordsJson)
    {
        var path = Path.Combine(_dir, "source.json");
        File.WriteAllText(path, "{ \"Online\": " + recordsJson + " }");
        return new JsonFileTransactionSource("Online", path, Anchor);
    }

    [Fact]
    public async Task Reads_records_with_iso_and_relative_times()
    {
        var source = SourceFor("""
            [
              { "sourceSystem": "Online", "transactionId": 7, "cardNumber": "***1", "locationCode": "ONLINE",
                "productName": "Webcam", "amount": 54.5, "transactionTime": "2026-02-28T09:30:00Z" },
              { "sourceSystem": "Online", "transactionId": 8, "locationCode": "ONLINE",
                "productName": "Mouse", "amount": 10, "transactionTime": "now-5h" },
              { "sourceSystem": "Online", "transactionId": 9, "locationCode": "ONLINE",
                "productName": "Cable", "amount": 3, "transactionTime": "now-90m" }
            ]
            """);

        var rows = await source.GetSnapshotAsync(CancellationToken.None);

        Assert.Equal(3, rows.Count);
        Assert.Equal(7, rows[0].TransactionId);
        Assert.Equal(54.5m, rows[0].Amount);
        Assert.Equal(new DateTime(2026, 2, 28, 9, 30, 0, DateTimeKind.Utc), rows[0].TransactionTime);
        Assert.Null(rows[1].CardNumber);
        Assert.Equal(Anchor.AddHours(-5), rows[1].TransactionTime);
        Assert.Equal(Anchor.AddMinutes(-90), rows[2].TransactionTime);
    }

    [Fact]
    public async Task Missing_or_unreadable_time_comes_through_as_default_so_validation_rejects_it()
    {
        var source = SourceFor("""
            [
              { "sourceSystem": "Online", "transactionId": 1, "locationCode": "L", "productName": "P", "amount": 1 },
              { "sourceSystem": "Online", "transactionId": 2, "locationCode": "L", "productName": "P", "amount": 1,
                "transactionTime": "yesterday-ish" }
            ]
            """);

        var rows = await source.GetSnapshotAsync(CancellationToken.None);

        Assert.All(rows, r => Assert.Equal(default, r.TransactionTime));
    }

    [Fact]
    public async Task Each_source_reads_only_its_own_entry_and_a_missing_entry_fails_the_source()
    {
        var path = Path.Combine(_dir, "shared.json");
        File.WriteAllText(path, """
            {
              "Online": [ { "sourceSystem": "Online", "transactionId": 1, "locationCode": "L", "productName": "P",
                            "amount": 1, "transactionTime": "now-1h" } ],
              "Store Uptown": []
            }
            """);

        var online = await new JsonFileTransactionSource("Online", path, Anchor).GetSnapshotAsync(CancellationToken.None);
        var uptown = await new JsonFileTransactionSource("Store Uptown", path, Anchor).GetSnapshotAsync(CancellationToken.None);
        Assert.Single(online);
        Assert.Empty(uptown);

        // no entry at all is a failure, not an empty snapshot (which would revoke recent transactions)
        var downtown = new JsonFileTransactionSource("Store Downtown", path, Anchor);
        await Assert.ThrowsAsync<InvalidDataException>(() => downtown.GetSnapshotAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Missing_file_and_broken_json_fail_the_whole_source()
    {
        var missing = new JsonFileTransactionSource("Online", Path.Combine(_dir, "nope.json"), Anchor);
        await Assert.ThrowsAsync<FileNotFoundException>(() => missing.GetSnapshotAsync(CancellationToken.None));

        var broken = SourceFor("[ { not json");
        await Assert.ThrowsAsync<JsonException>(() => broken.GetSnapshotAsync(CancellationToken.None));
    }
}

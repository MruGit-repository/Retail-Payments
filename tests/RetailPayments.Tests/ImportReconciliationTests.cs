using Microsoft.EntityFrameworkCore;
using RetailPayments.Api.Domain;
using RetailPayments.Api.Import;

namespace RetailPayments.Tests;

public class ImportReconciliationTests : IDisposable
{
    private readonly ImportHarness _h = new();
    public void Dispose() => _h.Dispose();

    private async Task<Transaction> Load(string source, int id)
    {
        using var db = _h.NewDb();
        return await db.Transactions.AsNoTracking().SingleAsync(t => t.SourceSystem == source && t.TransactionId == id);
    }

    private async Task<List<AuditEntry>> Audit(string source, int id)
    {
        using var db = _h.NewDb();
        return await db.AuditEntries.AsNoTracking()
            .Where(a => a.Transaction!.SourceSystem == source && a.Transaction.TransactionId == id)
            .OrderBy(a => a.Id).ToListAsync();
    }

    [Fact]
    public async Task New_transactions_are_inserted_with_created_audit()
    {
        _h.Uptown.Data = [_h.Tx(_h.Uptown, 100001, amount: 24.99m, product: "Wireless Mouse")];
        _h.Downtown.Data = [_h.Tx(_h.Downtown, 100001)];
        _h.Online.Data = [_h.Tx(_h.Online, 100001)];

        var result = await _h.RunImport();

        Assert.Equal("Succeeded", result.Status);
        Assert.Equal(3, result.Totals.Created);
        using var db = _h.NewDb();
        Assert.Equal(3, await db.Transactions.CountAsync());
        var mouse = await Load("Store Uptown", 100001);
        Assert.Equal(24.99m, mouse.Amount);
        Assert.Equal(TransactionStatus.Active, mouse.Status);
        Assert.Single(await Audit("Store Uptown", 100001), a => a.ChangeType == AuditChangeType.Created);
    }

    [Fact]
    public async Task A_source_that_hangs_is_timed_out_and_the_others_still_import()
    {
        _h.Uptown.Hang = true;
        _h.Online.Data = [_h.Tx(_h.Online, 1)];

        var result = await _h.RunImport(options: new ImportOptions { SourceTimeoutSeconds = 1 });

        Assert.Equal("PartiallySucceeded", result.Status);
        var uptown = result.Sources.Single(s => s.SourceSystem == "Store Uptown");
        Assert.False(uptown.Succeeded);
        Assert.Contains("timed out", uptown.Error);
        Assert.Equal(1, result.Totals.Created);
    }

    [Fact]
    public async Task Same_transaction_id_in_different_sources_are_distinct()
    {
        _h.Uptown.Data = [_h.Tx(_h.Uptown, 100001, product: "A")];
        _h.Online.Data = [_h.Tx(_h.Online, 100001, product: "B")];
        await _h.RunImport();

        Assert.Equal("A", (await Load("Store Uptown", 100001)).ProductName);
        Assert.Equal("B", (await Load("Online", 100001)).ProductName);
    }

    [Fact]
    public async Task Running_twice_with_identical_data_changes_nothing()
    {
        _h.Uptown.Data = [_h.Tx(_h.Uptown, 1), _h.Tx(_h.Uptown, 2, hoursAgo: 30)];
        _h.Online.Data = [_h.Tx(_h.Online, 1)];
        await _h.RunImport();

        using var before = _h.NewDb();
        var txBefore = await before.Transactions.AsNoTracking().OrderBy(t => t.Id).ToListAsync();
        var auditBefore = await before.AuditEntries.CountAsync();

        var second = await _h.RunImport();

        using var after = _h.NewDb();
        var txAfter = await after.Transactions.AsNoTracking().OrderBy(t => t.Id).ToListAsync();
        Assert.Equal(0, second.Totals.Created + second.Totals.Updated + second.Totals.Revoked + second.Totals.Restored + second.Totals.Finalized);
        Assert.Equal(auditBefore, await after.AuditEntries.CountAsync());
        Assert.Equal(txBefore.Count, txAfter.Count);
        Assert.All(txBefore.Zip(txAfter), p =>
        {
            Assert.Equal(p.First.Revision, p.Second.Revision);
            Assert.Equal(p.First.Status, p.Second.Status);
            Assert.Equal(p.First.LastUpdatedAt, p.Second.LastUpdatedAt);
        });
    }

    [Fact]
    public async Task Changed_fields_are_updated_and_audited_per_field()
    {
        _h.Uptown.Data = [_h.Tx(_h.Uptown, 1, amount: 9.99m, product: "USB Cable", card: "***1111")];
        await _h.RunImport();

        _h.Uptown.Data = [_h.Tx(_h.Uptown, 1, amount: 11.49m, product: "USB Cable", card: "***3333")];
        var result = await _h.RunImport();

        Assert.Equal(1, result.Totals.Updated);
        var tx = await Load("Store Uptown", 1);
        Assert.Equal(11.49m, tx.Amount);
        Assert.Equal("***3333", tx.CardNumber);
        Assert.Equal(2, tx.Revision);

        var changes = (await Audit("Store Uptown", 1)).Where(a => a.ChangeType == AuditChangeType.FieldChanged).ToList();
        Assert.Equal(2, changes.Count);
        var amount = Assert.Single(changes, c => c.FieldName == "Amount");
        Assert.Equal("9.99", amount.OldValue);
        Assert.Equal("11.49", amount.NewValue);
        Assert.Single(changes, c => c.FieldName == "CardNumber" && c.OldValue == "***1111" && c.NewValue == "***3333");
    }

    [Fact]
    public async Task Missing_transaction_inside_window_is_revoked_and_audited()
    {
        _h.Downtown.Data = [_h.Tx(_h.Downtown, 1, hoursAgo: 8), _h.Tx(_h.Downtown, 2, hoursAgo: 8)];
        await _h.RunImport();

        _h.Downtown.Data = [_h.Tx(_h.Downtown, 1, hoursAgo: 8)];
        var result = await _h.RunImport();

        Assert.Equal(1, result.Totals.Revoked);
        Assert.Equal(TransactionStatus.Revoked, (await Load("Store Downtown", 2)).Status);
        Assert.Equal(TransactionStatus.Active, (await Load("Store Downtown", 1)).Status);
        var revoke = Assert.Single(await Audit("Store Downtown", 2), a => a.ChangeType == AuditChangeType.Revoked);
        Assert.Equal("Active", revoke.OldValue);
        Assert.Equal("Revoked", revoke.NewValue);

        // idempotent: a second run must not revoke again
        var again = await _h.RunImport();
        Assert.Equal(0, again.Totals.Revoked);
        Assert.Single(await Audit("Store Downtown", 2), a => a.ChangeType == AuditChangeType.Revoked);
    }

    [Fact]
    public async Task Revoked_transaction_that_reappears_is_restored()
    {
        _h.Downtown.Data = [_h.Tx(_h.Downtown, 2, hoursAgo: 8)];
        await _h.RunImport();
        _h.Downtown.Data = [];
        await _h.RunImport();

        _h.Downtown.Data = [_h.Tx(_h.Downtown, 2, hoursAgo: 8, amount: 12m)];
        var result = await _h.RunImport();

        Assert.Equal(1, result.Totals.Restored);
        var tx = await Load("Store Downtown", 2);
        Assert.Equal(TransactionStatus.Active, tx.Status);
        Assert.Equal(12m, tx.Amount);
        Assert.Contains(await Audit("Store Downtown", 2), a => a.ChangeType == AuditChangeType.Restored);
    }

    [Fact]
    public async Task Failed_source_fetch_does_not_revoke_and_does_not_block_other_sources()
    {
        _h.Online.Data = [_h.Tx(_h.Online, 1)];
        _h.Uptown.Data = [_h.Tx(_h.Uptown, 1)];
        await _h.RunImport();

        _h.Online.Failure = new HttpRequestException("503");
        _h.Uptown.Data = [_h.Tx(_h.Uptown, 1, amount: 99m)];
        var result = await _h.RunImport();

        Assert.Equal("PartiallySucceeded", result.Status);
        Assert.False(result.Sources.Single(s => s.SourceSystem == "Online").Succeeded);
        Assert.Equal(TransactionStatus.Active, (await Load("Online", 1)).Status);
        Assert.Equal(99m, (await Load("Store Uptown", 1)).Amount);
    }

    [Fact]
    public async Task Empty_snapshot_after_a_good_one_revokes_only_recent_transactions()
    {
        _h.Online.Data = [_h.Tx(_h.Online, 1, hoursAgo: 2), _h.Tx(_h.Online, 2, hoursAgo: 30)];
        await _h.RunImport();   // #2 is already past the window -> finalized on arrival
        _h.Online.Data = [];
        await _h.RunImport();

        Assert.Equal(TransactionStatus.Revoked, (await Load("Online", 1)).Status);
        Assert.Equal(TransactionStatus.Finalized, (await Load("Online", 2)).Status);
    }

    [Fact]
    public async Task Transactions_older_than_window_are_finalized_and_become_immutable()
    {
        _h.Uptown.Data = [_h.Tx(_h.Uptown, 1, hoursAgo: 20, amount: 10m)];
        await _h.RunImport();
        Assert.Equal(TransactionStatus.Active, (await Load("Store Uptown", 1)).Status);

        _h.Clock.Advance(TimeSpan.FromHours(6)); // now 26h old
        var tx = _h.Tx(_h.Uptown, 1, hoursAgo: 26, amount: 10m);
        _h.Uptown.Data = [tx with { Amount = 500m }]; // late correction must be ignored
        var result = await _h.RunImport();

        Assert.Equal(1, result.Totals.Finalized);
        var stored = await Load("Store Uptown", 1);
        Assert.Equal(TransactionStatus.Finalized, stored.Status);
        Assert.Equal(10m, stored.Amount);

        // and missing from later snapshots does not revoke it
        _h.Uptown.Data = [];
        await _h.RunImport();
        Assert.Equal(TransactionStatus.Finalized, (await Load("Store Uptown", 1)).Status);
        Assert.Contains(await Audit("Store Uptown", 1), a => a.ChangeType == AuditChangeType.Finalized);
    }

    [Fact]
    public async Task Revoked_transaction_past_the_window_cannot_be_restored()
    {
        _h.Uptown.Data = [_h.Tx(_h.Uptown, 1, hoursAgo: 20)];
        await _h.RunImport();
        _h.Uptown.Data = [];
        await _h.RunImport();
        Assert.Equal(TransactionStatus.Revoked, (await Load("Store Uptown", 1)).Status);

        _h.Clock.Advance(TimeSpan.FromHours(6));
        _h.Uptown.Data = [_h.Tx(_h.Uptown, 1, hoursAgo: 26)];
        await _h.RunImport();

        Assert.Equal(TransactionStatus.Revoked, (await Load("Store Uptown", 1)).Status);
    }

    [Fact]
    public async Task Invalid_records_are_rejected_and_never_cause_revocation()
    {
        _h.Uptown.Data = [_h.Tx(_h.Uptown, 1, product: "OK product")];
        await _h.RunImport();

        _h.Uptown.Data =
        [
            _h.Tx(_h.Uptown, 1, product: new string('x', 60)), // malformed correction of existing
            _h.Tx(_h.Uptown, 2, product: new string('x', 60)), // malformed new
            _h.Tx(_h.Downtown, 3),                                              // wrong feed
        ];
        var result = await _h.RunImport();

        Assert.Equal(3, result.Totals.Rejected);
        Assert.Equal(0, result.Totals.Revoked);
        Assert.Equal(TransactionStatus.Active, (await Load("Store Uptown", 1)).Status);
        Assert.Equal("OK product", (await Load("Store Uptown", 1)).ProductName);
        using var db = _h.NewDb();
        Assert.Equal(1, await db.Transactions.CountAsync());

        var rejected = await db.RejectedRecords.OrderBy(r => r.Id).ToListAsync();
        Assert.Equal(3, rejected.Count);
        Assert.All(rejected, r => Assert.Equal(result.BatchId, r.ImportBatchId));
        Assert.Contains("productName", rejected[0].Reason);
        Assert.Contains("does not match feed", rejected[2].Reason);
        Assert.Contains("\"TransactionId\":2", rejected[1].Payload);
    }

    [Fact]
    public async Task Duplicate_ids_in_one_snapshot_keep_the_first_and_reject_the_rest()
    {
        _h.Uptown.Data = [_h.Tx(_h.Uptown, 1, amount: 5m), _h.Tx(_h.Uptown, 1, amount: 6m)];
        var result = await _h.RunImport();

        Assert.Equal(1, result.Totals.Created);
        Assert.Equal(1, result.Totals.Rejected);
        Assert.Equal(5m, (await Load("Store Uptown", 1)).Amount);
    }

    [Fact]
    public async Task Empty_card_number_is_allowed_and_amounts_are_rounded_to_two_decimals()
    {
        _h.Online.Data = [_h.Tx(_h.Online, 1, card: "", amount: 10.005m)];
        await _h.RunImport();

        var tx = await Load("Online", 1);
        Assert.Equal("", tx.CardNumber);
        Assert.Equal(10.01m, tx.Amount);
    }

    [Fact]
    public async Task Each_run_is_recorded_as_an_import_batch()
    {
        _h.Uptown.Data = [_h.Tx(_h.Uptown, 1)];
        var r = await _h.RunImport("Scheduler");

        using var db = _h.NewDb();
        var batch = await db.ImportBatches.SingleAsync();
        Assert.Equal(r.BatchId, batch.Id);
        Assert.Equal("Scheduler", batch.TriggeredBy);
        Assert.Equal(ImportBatchStatus.Succeeded, batch.Status);
        Assert.Equal(1, batch.Created);
        Assert.NotNull(batch.CompletedAt);
    }
}

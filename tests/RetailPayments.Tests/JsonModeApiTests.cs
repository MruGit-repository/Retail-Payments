using System.Net;
using System.Text.Json;

namespace RetailPayments.Tests;

// the real app reading SampleData/step0.json .. step3.json (Sources:Mode defaults to Json)
public class JsonModeApiTests(JsonModeFactory factory) : IClassFixture<JsonModeFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    // one test on purpose: the steps build on each other and share one database
    [Fact]
    public async Task Steps_walk_through_create_update_revoke_restore_and_a_failing_source()
    {
        await SetStep(0);
        var step0 = await Import();
        Assert.Equal("Succeeded", step0.Status);
        Assert.Equal((11, 0, 0, 0, 1, 0, 1), step0.Counts); // 11 created, 1 finalized (30h old), 1 rejected (no product)

        var again = await Import();
        Assert.Equal((0, 0, 0, 0, 0, 11, 1), again.Counts); // nothing changes on a repeat

        await SetStep(1);
        var step1 = await Import();
        Assert.Equal((1, 1, 1, 0, 0, 9, 1), step1.Counts); // Online 100005 new, Uptown 100002 corrected, Downtown 100002 gone

        await SetStep(2);
        var step2 = await Import();
        Assert.Equal((0, 1, 1, 1, 0, 9, 1), step2.Counts); // Online 100001 corrected, Online 100003 gone, Downtown 100002 back

        await SetStep(3);
        var step3 = await Import();
        Assert.Equal("PartiallySucceeded", step3.Status);
        Assert.Equal((0, 0, 0, 0, 0, 7, 2), step3.Counts); // Online has no entry so nothing is revoked; 2 bad records
        var failure = step3.Failures.Single();
        Assert.Contains("Online", failure);
        Assert.Contains("step3.json", failure);
        Assert.DoesNotContain("Retail Payments", failure); // no folder path in the message
        Assert.DoesNotContain(@"\", failure);
    }

    [Fact]
    public async Task Step_selector_reports_its_range_and_rejects_values_outside_it()
    {
        var current = JsonDocument.Parse(await _client.GetStringAsync("/api/dev/scenario")).RootElement;
        Assert.Equal(3, current.GetProperty("maxStep").GetInt32());

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/api/dev/scenario/4", null)).StatusCode);
    }

    private async Task SetStep(int step) =>
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync($"/api/dev/scenario/{step}", null)).StatusCode);

    private async Task<ImportOutcome> Import()
    {
        var response = await _client.PostAsync("/api/import", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var totals = root.GetProperty("totals");
        int Count(string name) => totals.GetProperty(name).GetInt32();

        var failures = root.GetProperty("sources").EnumerateArray()
            .Where(s => !s.GetProperty("succeeded").GetBoolean())
            .Select(s => $"{s.GetProperty("sourceSystem").GetString()}: {s.GetProperty("error").GetString()}")
            .ToList();

        return new ImportOutcome(
            root.GetProperty("status").GetString()!,
            (Count("created"), Count("updated"), Count("revoked"), Count("restored"),
             Count("finalized"), Count("unchanged"), Count("rejected")),
            failures);
    }

    // counts in the order created, updated, revoked, restored, finalized, unchanged, rejected
    private record ImportOutcome(string Status, (int, int, int, int, int, int, int) Counts, List<string> Failures);
}

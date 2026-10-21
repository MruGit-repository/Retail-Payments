using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace RetailPayments.Tests;

public class ApiTests : IClassFixture<MockModeFactory>
{
    private readonly HttpClient _client;
    public ApiTests(MockModeFactory f) => _client = f.CreateClient();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Import_then_list_and_drill_into_audit_history()
    {
        var import = await _client.PostAsync("/api/import", null);
        Assert.Equal(HttpStatusCode.OK, import.StatusCode);
        using var body = JsonDocument.Parse(await import.Content.ReadAsStringAsync());
        Assert.Equal("Succeeded", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(11, body.RootElement.GetProperty("totals").GetProperty("created").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("totals").GetProperty("rejected").GetInt32()); // Downtown record with no product name

        using var list = JsonDocument.Parse(await _client.GetStringAsync("/api/transactions?sortBy=amount&sortDir=asc&pageSize=3"));
        Assert.Equal(11, list.RootElement.GetProperty("total").GetInt32());
        var items = list.RootElement.GetProperty("items");
        Assert.Equal(3, items.GetArrayLength());
        Assert.Equal(9.99m, items[0].GetProperty("amount").GetDecimal());

        using var online = JsonDocument.Parse(await _client.GetStringAsync("/api/transactions?sourceSystem=Online&status=Finalized"));
        Assert.Equal(1, online.RootElement.GetProperty("total").GetInt32()); // the 30h-old tx

        var detail = await _client.GetAsync("/api/transactions/Store%20Uptown/100001");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var d = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.Equal(1, d.RootElement.GetProperty("audit").GetArrayLength());
    }

    [Fact]
    public async Task Unknown_transaction_is_404_and_bad_sort_is_400()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/transactions/Nope/1")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/transactions?sortBy=bogus")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/transactions?status=bogus")).StatusCode);
    }
}

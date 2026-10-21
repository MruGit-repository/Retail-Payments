namespace RetailPayments.Api.Sources.Mock;

// Fake data for the three sources. The step (POST /api/dev/scenario/{step}) simulates the
// sources changing between imports:
//   0  baseline, one Online transaction is already over 24h old
//   1  Uptown 100002 corrected, Downtown 100002 dropped, Online 100005 added
//   2  Downtown 100002 back, Online 100001 corrected, Online 100003 dropped
//   3  Online is down, Uptown sends a bad record
// Times are relative to app start so repeated imports see the same data, but they age in real time.
public class MockScenario : IScenario
{
    public const string Uptown = "Store Uptown";
    public const string Downtown = "Store Downtown";
    public const string Online = "Online";

    private const int LastStep = 3;

    private readonly DateTime _anchor;
    private volatile int _step;

    public MockScenario(TimeProvider clock)
    {
        var n = clock.GetUtcNow().UtcDateTime;
        _anchor = new DateTime(n.Year, n.Month, n.Day, n.Hour, n.Minute, n.Second, DateTimeKind.Utc);
    }

    public int MaxStep => LastStep;

    public int Step
    {
        get => _step;
        set => _step = value is >= 0 and <= LastStep ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    public IReadOnlyList<SourceTransaction> Snapshot(string source)
    {
        var step = _step;
        var list = new List<SourceTransaction>();

        SourceTransaction Txn(int id, string? card, string loc, string product, decimal amount, double hoursAgo) =>
            new(source, id, card, loc, product, amount, _anchor.AddHours(-hoursAgo));

        switch (source)
        {
            case Uptown:
                list.Add(Txn(100001, "***1111", "UPTOWN", "Wireless Mouse", 24.99m, 2));
                list.Add(step >= 1
                    ? Txn(100002, "***3333", "UPTOWN", "USB Cable", 11.49m, 5)
                    : Txn(100002, "***1111", "UPTOWN", "USB Cable", 9.99m, 5));
                list.Add(Txn(100003, "***2222", "UPTOWN", "Headset", 59.00m, 10));
                if (step >= 3)
                    list.Add(Txn(100009, "***9999", "UPTOWN", new string('x', 60), 5m, 1));
                break;

            case Downtown:
                list.Add(Txn(100001, "***0002", "DOWNTOWN", "Mechanical Keyboard", 89.99m, 3));
                if (step != 1) // dropped at step 1, back at step 2
                    list.Add(Txn(100002, "***0003", "DOWNTOWN", "Monitor Stand", 45.50m, 8));
                list.Add(Txn(100002, "***0004", "DOWNTOWN", "", 45.50m, 9));
                list.Add(Txn(100003, "***0005", "DOWNTOWN", "Mouse", 45.50m, 8));
                list.Add(Txn(100004, "***0006", "DOWNTOWN", "Wireless Keyboard", 45.50m, 8));
                break;

            case Online:
                if (step >= 3) throw new HttpRequestException("Online source API returned 503 Service Unavailable (simulated).");
                list.Add(step >= 2
                    ? Txn(100001, "***4444", "ONLINE", "USB-C Dock Pro", 134.95m, 1)
                    : Txn(100001, "***4444", "ONLINE", "USB-C Docking Station", 129.95m, 1));
                list.Add(Txn(100002, "***5555", "ONLINE", "Webcam", 54.00m, 6));
                if (step < 2) list.Add(Txn(100003, "", "ONLINE", "SSD 1TB", 79.00m, 20)); // no card, paid another way
                list.Add(Txn(100004, "***4444", "ONLINE", "Laptop Bag", 39.90m, 30));      // over 24h old
                if (step >= 1) list.Add(Txn(100005, "***6666", "ONLINE", "Wireless Charger", 19.99m, 0.5));
                break;
        }

        return list;
    }
}

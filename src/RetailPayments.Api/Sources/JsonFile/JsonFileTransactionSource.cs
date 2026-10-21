using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RetailPayments.Api.Sources.JsonFile;

// Reads one source system's snapshot from a shared JSON file. The file is an object with one
// array of transactions per source system:
//   { "Store Uptown": [ {...}, {...} ], "Store Downtown": [ ... ], "Online": [ ... ] }
// Edit the file and run the import again to see changes, removals and corrections.
//
// transactionTime can be an ISO date ("2026-10-21T14:30:00Z") or a time relative to app start
// ("now-5h", "now-90m"). Relative times are measured from a fixed anchor so repeated imports
// of an unchanged file see identical data.
//
// A missing file, broken JSON or a missing entry for this source fails the whole source (nothing
// gets revoked). A record with a bad or missing field is passed on and rejected by the
// reconciler's validation.
public class JsonFileTransactionSource(string sourceSystem, Func<string> currentPath, DateTime anchor) : ITransactionSource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex RelativeTime = new(@"^now-(\d+(?:\.\d+)?)([hm])$", RegexOptions.Compiled);

    // currentPath is a function so the demo steps can point the same source at a different file
    public JsonFileTransactionSource(string sourceSystem, string path, DateTime anchor)
        : this(sourceSystem, () => path, anchor)
    {
    }

    public string SourceSystem { get; } = sourceSystem;

    public async Task<IReadOnlyList<SourceTransaction>> GetSnapshotAsync(CancellationToken ct)
    {
        var path = currentPath();

        // messages go to the UI and the logs, so they name the file but not the folder it sits in
        var fileName = Path.GetFileName(path);

        if (!File.Exists(path))
            throw new FileNotFoundException($"Source file not found: {fileName}");

        await using var stream = File.OpenRead(path);
        var file = await JsonSerializer.DeserializeAsync<Dictionary<string, List<FileRecord>>>(stream, JsonOptions, ct)
            ?? throw new InvalidDataException($"{fileName} is empty");

        // an absent entry must not look like an empty snapshot, that would revoke everything recent
        var entries = new Dictionary<string, List<FileRecord>>(file, StringComparer.OrdinalIgnoreCase);
        if (!entries.TryGetValue(SourceSystem, out var records))
            throw new InvalidDataException($"{fileName} has no entry for '{SourceSystem}'");

        return records.Select(ToSourceTransaction).ToList();
    }

    private SourceTransaction ToSourceTransaction(FileRecord r) => new(
        r.SourceSystem ?? "",
        r.TransactionId,
        r.CardNumber,
        r.LocationCode,
        r.ProductName,
        r.Amount,
        ParseTime(r.TransactionTime));

    // returns default (which validation rejects) when the value is missing or not understood
    private DateTime ParseTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return default;

        var relative = RelativeTime.Match(text.Trim());
        if (relative.Success)
        {
            var amount = double.Parse(relative.Groups[1].Value, CultureInfo.InvariantCulture);
            return relative.Groups[2].Value == "h" ? anchor.AddHours(-amount) : anchor.AddMinutes(-amount);
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.UtcDateTime
            : default;
    }

    private record FileRecord(
        string? SourceSystem,
        int TransactionId,
        string? CardNumber,
        string? LocationCode,
        string? ProductName,
        decimal Amount,
        string? TransactionTime);
}

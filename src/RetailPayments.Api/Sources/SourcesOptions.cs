using RetailPayments.Api.Sources.JsonFile;

namespace RetailPayments.Api.Sources;

public enum SourceMode
{
    // read the transactions from a JSON file (the default)
    Json,

    // built-in demo data with scenario steps
    Mock,
}

// "Sources" section of appsettings.json. Read once at startup because it decides which sources get registered.
public class SourcesOptions
{
    public const string Section = "Sources";

    public SourceMode Mode { get; set; } = SourceMode.Json;

    public JsonSourceOptions Json { get; set; } = new();
}

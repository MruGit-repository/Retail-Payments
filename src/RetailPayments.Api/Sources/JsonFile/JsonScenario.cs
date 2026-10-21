namespace RetailPayments.Api.Sources.JsonFile;

// Current step of the JSON demo: step N reads the Nth file in Sources:Json:Files.
public class JsonScenario(int fileCount) : IScenario
{
    private volatile int _step;

    public int MaxStep { get; } = fileCount - 1;

    public int Step
    {
        get => _step;
        set => _step = value >= 0 && value <= MaxStep ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }
}

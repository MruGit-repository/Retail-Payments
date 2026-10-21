namespace RetailPayments.Api.Sources.JsonFile;

// "Sources:Json" section: the file(s) holding the data of every source system
public class JsonSourceOptions
{
    // relative paths are resolved against the app's content root (the project folder)
    public string Folder { get; set; } = "SampleData";

    // One file means a fixed data set. Several files add a step selector: step 0 reads the first file,
    // step 1 the second, and so on, so you can show how the import reacts to changing data.
    public List<string> Files { get; set; } = new();

    // one source is registered per name; each reads its own entry in the file
    public List<string> SourceSystems { get; set; } = new();
}

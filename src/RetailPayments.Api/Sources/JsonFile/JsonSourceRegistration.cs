namespace RetailPayments.Api.Sources.JsonFile;

public static class JsonSourceRegistration
{
    // Registers one JsonFileTransactionSource per name in SourceSystems.
    // Fails at startup if nothing is configured, so a typo doesn't silently import nothing.
    // With more than one file it also registers a step selector (see JsonScenario).
    public static IServiceCollection AddJsonSources(
        this IServiceCollection services, JsonSourceOptions options, string contentRoot)
    {
        if (options.SourceSystems.Count == 0)
            throw new InvalidOperationException("Sources:Mode is Json but Sources:Json:SourceSystems is empty.");
        if (options.Files.Count == 0)
            throw new InvalidOperationException("Sources:Mode is Json but Sources:Json:Files is empty.");

        var folder = Path.IsPathRooted(options.Folder) ? options.Folder : Path.Combine(contentRoot, options.Folder);
        var paths = options.Files.Select(file => Path.Combine(folder, file)).ToList();

        var now = DateTime.UtcNow;
        var anchor = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, DateTimeKind.Utc);

        Func<string> currentPath = () => paths[0];
        if (paths.Count > 1)
        {
            var scenario = new JsonScenario(paths.Count);
            services.AddSingleton(scenario);
            services.AddSingleton<IScenario>(scenario);
            currentPath = () => paths[scenario.Step];
        }

        foreach (var sourceSystem in options.SourceSystems)
            services.AddSingleton<ITransactionSource>(new JsonFileTransactionSource(sourceSystem, currentPath, anchor));

        return services;
    }
}

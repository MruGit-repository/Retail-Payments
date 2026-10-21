using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RetailPayments.Tests;

// runs the real app against a throwaway SQLite file; sourceMode null keeps the app default (Json)
public abstract class TestApiFactory(string? sourceMode) : WebApplicationFactory<Program>
{
    private readonly string _dbFile = Path.Combine(Path.GetTempPath(), $"rp-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_dbFile}");
        if (sourceMode is not null)
            builder.UseSetting("Sources:Mode", sourceMode);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbFile); } catch (IOException) { }
    }
}

public sealed class MockModeFactory() : TestApiFactory("Mock");

public sealed class JsonModeFactory() : TestApiFactory(null);

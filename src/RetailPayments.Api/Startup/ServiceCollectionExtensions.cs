using System.Text.Json.Serialization;
using RetailPayments.Api.Import;
using RetailPayments.Api.Sources;
using RetailPayments.Api.Sources.JsonFile;
using RetailPayments.Api.Sources.Mock;

namespace RetailPayments.Api.Startup;

// service registration, grouped so Program.cs stays short
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddImport(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<ImportOptions>()
            .Bind(config.GetSection(ImportOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ImportLock>();
        services.AddScoped<TransactionReconciler>();
        services.AddScoped<IImportService, ImportService>();
        return services;
    }

    // A real integration would register its own ITransactionSource implementations here instead.
    public static IServiceCollection AddSources(
        this IServiceCollection services, SourcesOptions options, string contentRoot)
    {
        if (options.Mode == SourceMode.Mock)
        {
            services.AddSingleton<MockScenario>();
            services.AddSingleton<IScenario>(sp => sp.GetRequiredService<MockScenario>());
            foreach (var name in new[] { MockScenario.Uptown, MockScenario.Downtown, MockScenario.Online })
                services.AddSingleton<ITransactionSource>(sp => new MockTransactionSource(name, sp.GetRequiredService<MockScenario>()));
        }
        else
        {
            services.AddJsonSources(options.Json, contentRoot);
        }
        return services;
    }

    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration config)
    {
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddProblemDetails();
        services.AddHealthChecks().AddDbContextCheck<Data.PaymentsDbContext>("database");
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        var origins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));
        return services;
    }
}

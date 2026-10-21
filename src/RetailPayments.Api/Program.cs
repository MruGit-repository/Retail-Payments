using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using RetailPayments.Api.Data;
using RetailPayments.Api.Endpoints;
using RetailPayments.Api.Sources;
using RetailPayments.Api.Startup;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
var sources = config.GetSection(SourcesOptions.Section).Get<SourcesOptions>() ?? new SourcesOptions();

// console + daily log file, configured in appsettings.json ("Serilog" section)
builder.Host.UseSerilog((context, logger) => logger.ReadFrom.Configuration(context.Configuration));

builder.Services.AddDbContext<PaymentsDbContext>(options =>
    options.UseSqlite(config.GetConnectionString("Default") ?? "Data Source=retailpayments.db"));
builder.Services.AddImport(config);
builder.Services.AddSources(sources, builder.Environment.ContentRootPath);
builder.Services.AddApiServices(config);

var app = builder.Build();

app.UseExceptionHandler(errors => errors.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    app.Logger.LogError(error, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new { title = "An unexpected error occurred.", status = 500 });
}));
app.UseSerilogRequestLogging();
app.UseCors();

if (config.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => Results.Redirect(app.Environment.IsDevelopment() ? "/swagger" : "/api/transactions"))
    .ExcludeFromDescription();
app.MapHealthChecks("/health");
app.MapApi();
// the step selector only exists when there is something to step through (Mock mode, or several JSON files)
if (app.Services.GetService<IScenario>() is not null)
    app.MapDevApi();

app.Run();

public partial class Program;

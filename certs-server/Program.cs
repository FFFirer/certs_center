using AgileConfig.Client;

using CertsServer;
using CertsServer.Acme;
using CertsServer.Data;

using Microsoft.EntityFrameworkCore;

using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables("CERTS_");

builder.Host.UseSerilog((ctx, sp, serilog) =>
{
    serilog
        .ReadFrom.Configuration(ctx.Configuration);
});

try
{
    // Build a resource configuration action to set service information.
    Action<ResourceBuilder> configureResource = r => r.AddService(
        serviceName: builder.Configuration.GetValue("ServiceName", defaultValue: "certs-center")!,
        serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
        serviceInstanceId: Environment.MachineName);

    var DefaultEndpoint = builder.Configuration.GetValue("Oltp:Endpoint", "http://127.0.0.1:4317");

    builder.Services.AddOpenTelemetry()
        .ConfigureResource(configureResource)
        .WithTracing(tracing =>
        {
            tracing
                .AddHttpClientInstrumentation()
                .AddAspNetCoreInstrumentation();

            // Use IConfiguration binding for AspNetCore instrumentation options.
            builder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(
            builder.Configuration.GetSection("AspNetCoreInstrumentation"));

            tracing.AddOtlpExporter(otlpOptions =>
            {
                // Use IConfiguration directly for OTLP exporter endpoint option.
                otlpOptions.Endpoint = new Uri(
                    builder.Configuration.GetValue("Otlp:Endpoint", defaultValue: DefaultEndpoint)!);
            });
        })
        .WithMetrics(metrics =>
        {
            metrics
                .AddHttpClientInstrumentation()
                .AddAspNetCoreInstrumentation();

            metrics.AddOtlpExporter(otlpOptions =>
            {
                // Use IConfiguration directly for OTLP exporter endpoint option.
                otlpOptions.Endpoint = new Uri(
                    builder.Configuration.GetValue("Otlp:Endpoint", defaultValue: DefaultEndpoint)!);
            });
        });

    builder.Services
        .AddOpenApiDocument()
        .AddEndpointsApiExplorer()
        .AddCors()
        .AddProblemDetails()
        .AddHttpContextAccessor()
        .AddAuthorization()
        .AddAuthentication();

    builder.Services
        .AddOptions<AcmeOptions>("AcmeOptions");
    builder.Services
        .AddAcmeDefaults();

    var db = builder.Configuration.GetValue("Db", "sqlite");

    switch (db)
    {
        case "postgres":
            // builder.Services.AddSqliteModelCreating<CertsServerDbContext>();
            builder.Services.AddDbContext<CertsServerDbContext>(
                options => options.UseNpgsql(builder.Configuration.GetConnectionString("Default"))
            );
            break;
        default:
            builder.Services.AddSqliteModelCreating<CertsServerDbContext>();
            builder.Services.AddDbContext<CertsServerDbContext>(
                options => options.UseSqlite(builder.Configuration.GetConnectionString("Default"))
            );
            break;
    }

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseOpenApi();
        app.UseSwaggerUi();
    }

    
    app.UseStaticFiles();

    app.UseRouting();

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseCors();

    // custom

    // endpoints
    app.MapEndpoints().ProducesProblem(500).ProducesValidationProblem();

    app.Run();
    return 0;
}
catch (System.Exception ex)
{
    Log.Logger.Fatal(ex, "App exited");
    return 1;
}

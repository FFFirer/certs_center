using AgileConfig.Client;

using CertsCenter.Acme;

using CertsServer;
using CertsServer.Acme;
using CertsServer.Data;
using CertsServer.QuartzJobs;
using CertsServer.Services;

using Microsoft.EntityFrameworkCore;

using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

using Quartz;
using Quartz.AspNetCore;

using Serilog;

using Vite.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddUserSecrets<Program>()
    .AddEnvironmentVariables("CERTS_");

DnsUtil.Configure(builder.Configuration);

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

    builder.Services.Configure<HostOptions>(options =>
    {
        options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
    });

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

    builder.Services.AddViteServices(viteOptions =>
    {
        viteOptions.Base = "dist";
        viteOptions.Server.AutoRun = true;
        viteOptions.Server.PackageManager = "pnpm";
    });

    builder.Services
        .AddRazorPages();

    builder.Services
        .AddOpenApiDocument()
        .AddEndpointsApiExplorer()
        .AddCors()
        .AddProblemDetails()
        .AddHttpContextAccessor()
        .AddAuthorization()
        .AddAuthentication();

    builder.Services.AddMediatR(cfg =>
    {
        cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
    });

    builder.Services
        .AddQuartz(quartz =>
        {
            quartz.InterruptJobsOnShutdown = true;
            quartz.InterruptJobsOnShutdownWithWait = true;
            quartz.AddJob<HandleTicketJob>(job =>
            {
                job.WithIdentity(HandleTicketJob.JobKey).StoreDurably(true);
            });
        })
        .AddQuartzServer(quartzServer =>
        {
            quartzServer.WaitForJobsToComplete = true;
        })
        .AddCertsServerHostedService();

    builder.Services
        .AddAcmeDefaults()
        .AddScoped<TicketOrderExportService>();

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

    if (app.Environment.IsDevelopment() || app.Configuration.GetValue("EnableSwagger", false) == true)
    {
        app.UseOpenApi();
        app.UseSwaggerUi();
    }

    // app.UseStaticFiles();

    app.UseRouting();

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseCors();

    // custom

    // endpoints
    app.MapStaticAssets();
    app.MapRazorPages()
        .WithStaticAssets();
    app.MapEndpoints().ProducesProblem(500).ProducesValidationProblem();

    if (app.Environment.IsDevelopment())
    {
        // WebSockets support is required for HMR (hot module reload).
        // Uncomment the following line if your pipeline doesn't contain it.
        app.UseWebSockets();
        // Enable all required features to use the Vite Development Server.
        // Pass true if you want to use the integrated middleware.
        app.UseViteDevelopmentServer(/* false */);
    }

    app.Run();
    return 0;
}
catch (System.Exception ex)
{
    Log.Logger.Fatal(ex, "App exited");
    return 1;
}

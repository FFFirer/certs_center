using System;
using System.Diagnostics.CodeAnalysis;

using CertsServer.QuartzJobs;

using Microsoft.EntityFrameworkCore;

using Quartz;

namespace CertsServer;

public class HandleTicketStartupHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger _logger;

    public HandleTicketStartupHostedService(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<HandleTicketStartupHostedService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceScopeFactory.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<CertsServerDbContext>();
        var tickets = await db.Tickets.AsNoTracking().ToListAsync(stoppingToken);

        var schd = await scope.ServiceProvider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        foreach (var t in tickets)
        {
            var trigger = TriggerBuilder.Create()
                .ForJob(HandleTicketJob.JobKey)
                .WithIdentity(t.Id.ToString())
                .WithSimpleSchedule(plan =>
                {
                    plan.WithInterval(TimeSpan.FromDays(1));
                })
                .UsingJobData(new JobDataMap((IDictionary<string, object>)new Dictionary<string, object>
                {
                    [HandleTicketJob.DataMapKeys.TicketId] = t.Id
                }))
                .Build();

            await schd.ScheduleJob(trigger, stoppingToken);
            _logger.LogInformation("Trigger HandleTicketJob for {TicketId}", t.Id);
        } 
    }
}

public static class Extensions
{
    public static bool IsNullOrEmpty<T>([NotNullWhen(false)] this IEnumerable<T>? source)
    {
        return source is null || !source.Any();
    }


    public static IServiceCollection AddCertsServerHostedService(this IServiceCollection services)
    {
        services.AddHostedService<HandleTicketStartupHostedService>();

        return services;
    }
}

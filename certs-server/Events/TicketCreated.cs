using System;

using CertsServer.QuartzJobs;

using MediatR;

using Quartz;

namespace CertsServer.Events;

public class TicketCreated(Guid id) : INotification
{
    public Guid Id { get; set; } = id;
}

public class TicketCreatedHandler : INotificationHandler<TicketCreated>
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly ILogger<TicketCreatedHandler> _logger;
    private readonly CertsServerDbContext _db;

    public TicketCreatedHandler(
        ISchedulerFactory schedulerFactory,
        ILogger<TicketCreatedHandler> logger,
        CertsServerDbContext db)
    {
        _schedulerFactory = schedulerFactory;
        _logger = logger;
        _db = db;
    }

    public async Task Handle(TicketCreated notification, CancellationToken cancellationToken)
    {
        var ticket = await _db.Tickets.FindAsync(notification.Id, cancellationToken);
        if (ticket is null)
        {
            throw new InvalidOperationException("Ticket is null");
        }

        var schd = await _schedulerFactory.GetScheduler();

        var trigger = TriggerBuilder.Create()
            .ForJob(HandleTicketJob.JobKey)
            .WithIdentity(ticket.Id.ToString())
            .WithSimpleSchedule(plan =>
            {
                plan.WithInterval(TimeSpan.FromMinutes(5));
                plan.RepeatForever();
            })
            .UsingJobData(new JobDataMap
            {
                [HandleTicketJob.DataMapKeys.TicketId] = ticket.Id
            })
            .Build();

        await schd.ScheduleJob(trigger, cancellationToken);
        _logger.LogInformation("Ticket {Id} created, started trigger {@Key} for create certificate", ticket.Id, trigger.Key);
    }
}
using System;

using MediatR;

using Quartz;

namespace CertsServer.Events;

public class TicketDeleted(Guid id) : INotification
{
    public Guid Id { get; set; } = id;
}

public class TicketDeletedHandler : INotificationHandler<TicketDeleted>
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly ILogger<TicketCreatedHandler> _logger;
    private readonly CertsServerDbContext _db;

    public TicketDeletedHandler(
        ISchedulerFactory schedulerFactory,
        ILogger<TicketCreatedHandler> logger,
        CertsServerDbContext db)
    {
        _schedulerFactory = schedulerFactory;
        _logger = logger;
        _db = db;
    }

    public async Task Handle(TicketDeleted notification, CancellationToken cancellationToken)
    {
        var ticket = await _db.Tickets.FindAsync(notification.Id, cancellationToken);
        if (ticket is null)
        {
            throw new InvalidOperationException("Ticket is null");
        }

        var schd = await _schedulerFactory.GetScheduler();

        var triggerKey = new TriggerKey(ticket.Id.ToString());
        var trigger = await schd.GetTrigger(triggerKey, cancellationToken);

        if (trigger is null)
        {
            return;
        }

        await schd.UnscheduleJob(trigger.Key, cancellationToken);
        _logger.LogInformation("Ticket {Id} deleted, unschedule trigger for create certificate", ticket.Id);
    }
}

using System;
using System.Security.Cryptography.X509Certificates;

using ACMESharp.Protocol;
using ACMESharp.Protocol.Resources;

using CertsServer.Acme;
using CertsServer.Data;

using Microsoft.EntityFrameworkCore;

using Quartz;

namespace CertsServer.QuartzJobs;

public static class FailReasons
{
    public const string DomainNamesIsEmpty = "Domain Names为空";

    public const string CertificateNotGenerated = "证书未生成";
}

public class HandleTicketJob : IJob
{
    public static JobKey JobKey => new JobKey(nameof(HandleTicketJob));

    public static class DataMapKeys
    {
        public const string TicketId = nameof(TicketId);
        public const string AcmeOrderUrl = nameof(AcmeOrderUrl);
    }

    private readonly CertsServerDbContext _db;
    private readonly ILogger _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public HandleTicketJob(
        IServiceScopeFactory serviceScopeFactory,
        CertsServerDbContext db,
        ILogger<HandleTicketJob> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _db = db;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        if (context.MergedJobDataMap.TryGetGuid(DataMapKeys.TicketId, out var ticketId) == false
            || ticketId == Guid.Empty)
        {
            throw new ArgumentException($"未设置'{DataMapKeys.TicketId}'");
        }

        // context.MergedJobDataMap.TryGetString(DataMapKeys.AcmeOrderUrl, out var acmeOrderUrl);

        var ticket = await _db.Tickets.FindAsync(ticketId, context.CancellationToken);

        if (ticket is null)
        {
            _logger.LogWarning("Ticket不存在: {TicketId}", ticketId);
            return;
        }

        if (ticket.DomainNames.IsNullOrEmpty())
        {
            ticket.Failed(FailReasons.DomainNamesIsEmpty);

            _logger.LogWarning("{Reason}: {TicketId}", FailReasons.DomainNamesIsEmpty, ticketId);

            await _db.SaveChangesAsync(context.CancellationToken);
            return;
        }

        var ticketOrder = await _db.TicketOrders
            .Where(x => x.TicketId == ticket.Id && x.Deleted == false)
            .OrderByDescending(x => x.CreatedTime)
            .FirstOrDefaultAsync(context.CancellationToken);

        try
        {
            using var scope = _serviceScopeFactory.CreateAsyncScope();
            var certificateStore = scope.ServiceProvider.GetRequiredService<ICertificateStore>();

            X509Certificate2? certificate;

            if (string.IsNullOrWhiteSpace(ticketOrder?.Certificate?.Path) == false)
            {
                certificate = await certificateStore.FindAsync(ticketOrder.Certificate.Path, context.CancellationToken);
                if (certificate is not null && NotExpired(certificate))
                {
                    _logger.LogInformation("Certificate for ticket '{Id}' is valid before {NotAfter}.", ticketId, DateTimeExtensions.ConvertToUtc(certificate.NotAfter));

                    ticket.Finished();

                    return;
                }
            }

            ticket.Processing();
            await _db.SaveChangesAsync(context.CancellationToken);

            var certificateFactory = scope.ServiceProvider.GetRequiredService<AcmeCertificateFactory>();
            var account = await certificateFactory.GetOrCreateAccountAsync(context.CancellationToken);

            OrderDetails? order = null;

            if (string.IsNullOrWhiteSpace(ticketOrder?.OrderUrl) == false)
            {
                // try to load existing order
                order = await certificateFactory.GetOrderAsync(ticketOrder.OrderUrl, order, context.CancellationToken);
            }

            // create ticket-order if not exists || expired || invalid
            if (ticketOrder is null || ticketOrder.Expired() || order?.Payload?.Status == AcmeConst.InvalidStatus)
            {
                if(ticketOrder is not null)
                {
                    ticketOrder.Delete();
                    _db.Update(ticketOrder);
                }

                ticketOrder = new TicketOrderEntity(string.Empty, ticket.Id);
                _db.Add(ticketOrder);
                await _db.SaveChangesAsync(context.CancellationToken);
                _logger.LogInformation("Created new order {TicketOrderId}:{OrderUrl}", ticketOrder.Id, ticketOrder.OrderUrl);
            }

            // until here ticketOrder must not null
            var certReq = new CertsServer.Acme.CertificateRequest(ticketOrder.Id, ticketId, ticket.DomainNames)
            {
                PfxPassword = ticket.PfxPassword,
            };

            // load or create
            order = await certificateFactory.GetOrCreateOrderAsync(certReq, ticketOrder.OrderUrl, true, context.CancellationToken);
            
            if(ticketOrder.OrderUrl != order.OrderUrl)
            {
                ticketOrder.OrderUrl = order.OrderUrl;
                ticketOrder.LastUpdatedTime = DateTimeOffset.UtcNow;
                _logger.LogInformation("Updated order URL for ticket {TicketId} to {OrderUrl}", ticketId, order.OrderUrl);
                await _db.SaveChangesAsync(context.CancellationToken);
            }

            certificate = await certificateFactory.CreateCertificateAsync(certReq, order, context.CancellationToken);

            if (certificate is null)
            {
                ticket.Failed(FailReasons.CertificateNotGenerated);
                await _db.SaveChangesAsync(context.CancellationToken);
                return;
            }

            // Reload
            order = await certificateFactory.GetOrderAsync(ticketOrder.OrderUrl, order, context.CancellationToken);
            var path = await certificateStore.SaveAsync(certificate, context.CancellationToken);

            ticketOrder.Expires = certificate.NotAfter;
            ticketOrder.Certificate = new TicketCertificateEntity(path, ticketOrder.Id, ticketId, TicketCertificateStatus.Active, certificate.NotBefore, certificate.NotAfter)
            {
                AcmeOrderUrl = order.OrderUrl
            };
            ticket.Finished();

            await _db.SaveChangesAsync(context.CancellationToken);
        }
        catch (StateTerminatedException)
        {
            _logger.LogInformation("State terminated");
        }
        catch (System.Exception ex)
        {
            ticket.Failed(ex.Message);
            await _db.SaveChangesAsync(context.CancellationToken);

            _logger.LogError(ex, "Ticket order {id} failed", ticketOrder?.Id);
        }
    }

    private bool NotExpired(X509Certificate2 certificate)
    {
        return DateTimeExtensions.ConvertToUtc(certificate.NotAfter) >= DateTime.UtcNow;
    }
}

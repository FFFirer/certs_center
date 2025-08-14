using System;
using System.Security.Cryptography.X509Certificates;

using ACMESharp.Protocol;

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
            var certReq = new CertsServer.Acme.CertificateRequest(ticketId, ticket.DomainNames);

            OrderDetails order = await certificateFactory.GetOrCreateOrderAsync(certReq, ticketOrder?.OrderUrl, true, context.CancellationToken);

            if (order.OrderUrl != ticketOrder?.OrderUrl)
            {
                var oldTicketOrder = ticketOrder;
                if (oldTicketOrder is not null)
                {
                    oldTicketOrder.Delete();
                    _db.Update(oldTicketOrder);
                }
                ticketOrder = new TicketOrderEntity(order.OrderUrl, ticket.Id);
                _db.Add(ticketOrder);
                await _db.SaveChangesAsync(context.CancellationToken);
                _logger.LogInformation("Created new order {TicketOrderId}:{OrderUrl}", ticketOrder.Id, ticketOrder.OrderUrl);
            }
            else
            {
                _logger.LogInformation("Loaded existing order {OrderUrl}", ticketOrder.OrderUrl);
            }

            certificate = await certificateFactory.CreateCertificateAsync(certReq, order, context.CancellationToken);

            if (certificate is null)
            {
                ticket.Failed(FailReasons.CertificateNotGenerated);
                await _db.SaveChangesAsync(context.CancellationToken);
                return;
            }

            var path = await certificateStore.SaveAsync(certificate, context.CancellationToken);
            var ticketCertificate = new TicketCertificateEntity(path, ticketOrder.Id, ticketId, TicketCertificateStatus.Active, certificate.NotBefore, certificate.NotAfter)
            {
                AcmeOrderUrl = order.OrderUrl
            };
            ticket.Finished(ticketCertificate);
            await _db.AddAsync(ticketCertificate);
            await _db.SaveChangesAsync(context.CancellationToken);
        }
        catch (StateTerminatedException)
        {
            _logger.LogInformation("State terminated");
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Unhandled error");
        }
    }

    private bool NotExpired(X509Certificate2 certificate)
    {
        return DateTimeExtensions.ConvertToUtc(certificate.NotAfter) >= DateTime.UtcNow;
    }
}

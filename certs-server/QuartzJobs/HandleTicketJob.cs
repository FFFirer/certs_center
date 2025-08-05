using System;
using System.Security.Cryptography.X509Certificates;

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

        var ticketCertificates = await _db.TicketCertificates.Where(a => a.TicketId == ticketId && a.Status == Data.TicketCertificateStatus.Active).ToListAsync(cancellationToken: context.CancellationToken);

        try
        {
            using var scope = _serviceScopeFactory.CreateAsyncScope();
            var certificateStore = scope.ServiceProvider.GetRequiredService<ICertificateStore>();

            X509Certificate2? certificate;

            if (ticket.Status == Data.TicketStatus.Finished || ticketCertificates.IsNullOrEmpty() == false)
            {
                var ticketCert = ticketCertificates.OrderByDescending(x => x.CreatedTime).First();

                certificate = await certificateStore.FindAsync(ticketCert.Path, context.CancellationToken);
                if (certificate is not null && NotExpired(certificate))
                {
                    _logger.LogInformation("Certificate for ticket '{Id}' is valid.", ticketId);

                    ticket.Finished();

                    return;
                }
            }

            ticket.Processing();

            await _db.SaveChangesAsync(context.CancellationToken);

            var certificateFactory = scope.ServiceProvider.GetRequiredService<AcmeCertificateFactory>();

            var account = await certificateFactory.GetOrCreateAccountAsync(context.CancellationToken);

            certificate = await certificateFactory.CreateCertificateAsync(new(ticketId, ticket.DomainNames), context.CancellationToken);

            if (certificate is null)
            {
                ticket.Failed(FailReasons.CertificateNotGenerated);
                await _db.SaveChangesAsync(context.CancellationToken);
                return;
            }

            var path = await certificateStore.SaveAsync(certificate, context.CancellationToken);
            var ticketCertificate = new TicketCertificateEntity(path, ticketId, TicketCertificateStatus.Active, certificate.NotBefore, certificate.NotAfter);
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

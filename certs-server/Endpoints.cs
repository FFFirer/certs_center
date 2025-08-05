using CertsServer.Acme;
using CertsServer.Data;
using CertsServer.QuartzJobs;

using LettuceEncrypt;

using McMaster.AspNetCore.Kestrel.Certificates;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using NSwag.Annotations;

using Quartz;

using System.Security.Cryptography.X509Certificates;

namespace CertsServer;

public static class Endpoints
{
    public static IEndpointConventionBuilder MapEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var apis = endpoints.MapGroup("/api");

        apis.MapGet("/ticket/all", TicketHttpHandlers.ListAll);
        apis.MapGet("/ticket/{id:guid}", TicketHttpHandlers.Get);
        apis.MapGet("/ticket/{id:guid}/plan", TicketHttpHandlers.GetPlanInfo);

        return apis;
    }
}

public record TicketDto(Guid Id, DateTimeOffset CreatedTime, TicketStatus Status, string? Remark, string[]? Domains, CertificateDto[] Certificates);
public record CertificateDto(Guid Id, string Path, TicketCertificateStatus Status, DateTime? NotBefore, DateTime? NotAfter, DateTimeOffset CreatedTime);

public static class TicketHttpHandlers
{
    [OpenApiOperation("Ticket_All", "获取所有Ticket", "")]
    public static async Task<List<TicketDto>> ListAll(
        [FromServices] CertsServerDbContext db,
        CancellationToken cancellationToken)
    {
        var tickets = await db.Tickets.ToListAsync(cancellationToken);
        return tickets.Select(x => new TicketDto(
            x.Id,
            x.CreatedTime,
            x.Status,
            x.Remark,
            x.DomainNames,
            x.Certificates.Select(c => new CertificateDto(
                c.Id,
                c.Path,
                c.Status,
                c.NotBefore,
                c.NotAfter,
                c.CreatedTime)).ToArray())).ToList();
    }

    [OpenApiOperation("Ticket_Get", "获取Ticket", "")]
    public static async Task<Results<FileContentHttpResult, NotFound>> Get(
        [FromRoute] Guid id,
        [FromServices] ICertificateStore store,
        [FromServices] CertsServerDbContext certDbContext,
        CancellationToken cancellationToken)
    {
        var ticketCertificate = await certDbContext.TicketCertificates.FindAsync(id, cancellationToken);
        if (ticketCertificate is null)
        {
            return TypedResults.NotFound();
        }

        var certificate = await store.FindAsync(ticketCertificate.Path, cancellationToken);
        if (certificate is null)
        {
            return TypedResults.NotFound();
        }

        var ticket = await certDbContext.Tickets.FindAsync(ticketCertificate.TicketId, cancellationToken);
        var fileBytes = certificate.Export(X509ContentType.Pkcs12, ticket?.PfxPassword);

        return TypedResults.File(fileBytes, "application/octet-stream", certificate.Thumbprint + ".pfx");
    }

    [OpenApiOperation("Ticket_GetPlanInfo", "获取Ticket信息", "")]
    public static async Task<Results<Ok<TicketPlanInfo>, NotFound>> GetPlanInfo(
        [FromRoute] Guid id,
        [FromServices] ISchedulerFactory schedulerFactory,
        CancellationToken cancellationToken)
    {
        var schd = await schedulerFactory.GetScheduler();
        var trigger = await schd.GetTrigger(new TriggerKey(id.ToString()), cancellationToken);

        if (trigger is null)
        {
            return TypedResults.NotFound();
        }

        var planInfo = new TicketPlanInfo(
            trigger.GetNextFireTimeUtc()?.ToLocalTime(),
            trigger.GetPreviousFireTimeUtc()?.ToLocalTime());

        return TypedResults.Ok(planInfo);
    }
}

public record TicketPlanInfo(DateTimeOffset? NextFireTime, DateTimeOffset? PreviousFireTime);
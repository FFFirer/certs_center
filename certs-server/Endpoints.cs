using LettuceEncrypt;
using McMaster.AspNetCore.Kestrel.Certificates;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSwag.Annotations;
using System.Security.Cryptography.X509Certificates;

namespace CertsServer;

public static class Endpoints
{
    public static IEndpointConventionBuilder MapEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var apis = endpoints.MapGroup("/api");

        apis.MapGet("/certificate/all", CertificateHttpHandlers.ListAll);
        apis.MapGet("/certificate", CertificateHttpHandlers.Get);

        return apis;
    }
}

public record CertificateDto(string Name);

public static class CertificateHttpHandlers
{
    [OpenApiOperation("Certificates_All", "获取所有证书", "")]
    public static async Task<List<CertificateDto>> ListAll([FromServices] ICertificateSource certificateSource, CancellationToken cancellationToken)
    {
        var certificates = await certificateSource.GetCertificatesAsync(cancellationToken);
        return certificates.SelectMany(cert => X509CertificateHelpers.GetAllDnsNames(cert))
            .Select(name => new CertificateDto(name)).ToList();
    }

    [OpenApiOperation("Certificates_Get", "获取证书", "")]
    public static async Task<Results<FileContentHttpResult, NotFound>> Get([FromQuery] string? domain, [FromServices] ICertificateStore store, [FromServices] CertDbContext certDbContext, CancellationToken cancellationToken)
    {
        var cert = await store.SelectAsync(domain, cancellationToken);


        return cert switch
        {
            not null => TypedResults.File(cert.Data, "application/octet-stream", domain + ".pfx"),
            _ => TypedResults.NotFound()
        };
    }
}
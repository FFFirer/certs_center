using LettuceEncrypt;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;

namespace CertsServer;

/// <summary>
/// 单例
/// </summary>
public class CertificateStore : ICertificateRepository, ICertificateSource, ICertificateStore
{
    private readonly IServiceProvider serviceProvider;
    private readonly string? pfxPassword;
    private readonly ConcurrentDictionary<string, CertificateFile> _certs = new ConcurrentDictionary<string, CertificateFile>();

    public CertificateStore(IServiceProvider serviceProvider, string? pfxPassword)
    {
        this.serviceProvider = serviceProvider;
        this.pfxPassword = pfxPassword;
    }

    public async Task<IEnumerable<X509Certificate2>> GetCertificatesAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        using var certDbContext = scope.ServiceProvider.GetRequiredService<CertDbContext>();

        var domains = await certDbContext.DomainNames.AsNoTracking().Where(x => x.CertificateFileId != null).ToListAsync(cancellationToken);
        var certFileIds = domains.Select(x => x.CertificateFileId).Distinct().ToList();

        //var query = from domain in certDbContext.DomainNames
        //            where domain.CertificateFileId != null
        //            join certFile in certDbContext.CertificateFiles on domain.CertificateFileId equals certFile.Id
        //            select certFile;

        var files = await certDbContext.CertificateFiles.AsNoTracking()
            .AsNoTracking()
            .Where(x => certFileIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        var certs = new List<X509Certificate2>();

        foreach (var certFile in files)
        {
            if (certFile.Data is null)
            {
                continue;
            }

            var cert = LoadCertificate(certFile);
            certs.Add(cert);
        }

        return certs;
    }

    static X509Certificate2 LoadCertificate(CertificateFile certFile) => X509CertificateLoader.LoadPkcs12(certFile.Data, certFile.Password);

    public async Task SaveAsync(X509Certificate2 certificate, CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        using var certDbContext = scope.ServiceProvider.GetRequiredService<CertDbContext>();

        var dnsNames = X509CertificateHelpers.GetAllDnsNames(certificate).ToList();
        var domainNames = await certDbContext.DomainNames.Where(x => dnsNames.Contains(x.Id)).ToListAsync(cancellationToken);

        var notSavedDomainNames = dnsNames.Except(domainNames.Select(x => x.Id)).ToList();

        var certFile = new CertificateFile(certificate.Export(X509ContentType.Pfx, pfxPassword), Guid.NewGuid())
        {
            NotAfter = certificate.NotAfter,
            NotBefore = certificate.NotBefore,
            Password = this.pfxPassword
        };

        certDbContext.Add(certFile);

        foreach (var domainName in notSavedDomainNames)
        {
            var newDomainName = new DomainName(domainName)
            {
                CertificateFileId = certFile.Id,
            };

            certDbContext.Add(newDomainName);
            certDbContext.Add(newDomainName);
            certDbContext.Add(new DomainNameAction(domainName, "Created"));

            _certs.AddOrUpdate(domainName, certFile, (d, old) => certFile.Created > old.Created ? certFile : old);
        }

        foreach (var domainName in domainNames)
        {
            domainName.CertificateFileId = certFile.Id;
            domainName.LastModified = DateTimeOffset.UtcNow;
            certDbContext.Add(new DomainNameAction(domainName.Id, "Updated"));

            _certs.AddOrUpdate(domainName.Id, certFile, (d, old) => certFile.Created > old.Created ? certFile : old);
        }

        await certDbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<CertificateFile?> SelectAsync(string? domain, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return null;
        }

        if (_certs.TryGetValue(domain, out var cert))
        {
            return cert;
        }

        using var scope = serviceProvider.CreateScope();
        using var certDbCtx = scope.ServiceProvider.GetRequiredService<CertDbContext>();

        var domainName = await certDbCtx.DomainNames.FindAsync(domain, cancellationToken);
        if (domainName?.CertificateFileId is null || domainName.CertificateFileId == Guid.Empty)
        {
            return null;
        }

        var certFile = await certDbCtx.CertificateFiles.FindAsync(domainName.CertificateFileId, cancellationToken);
        if (certFile is null)
        {
            return null;
        }

        _certs.AddOrUpdate(domain, certFile, (d, old) => certFile);

        return certFile;
    }
}

public interface ICertificateStore
{
    Task<CertificateFile?> SelectAsync(string? domain, CancellationToken cancellationToken);
}
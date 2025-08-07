using System;
using System.Threading.Tasks;

using CertsCenter.Acme;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CertsServer.Acme;

public class AlibabaCloudOptions
{
    public string AccessKeyId { get; set; } = "";
    public string AccessKeySecret { get; set; } = "";
    public string Address { get; set; } = "";
}

public static class AlibabaCloudServiceCollectionExtensions
{
    public static IServiceCollection AddAlibabaCloud(this IServiceCollection services)
    {
        services.ConfigureOptionsFromConfiguration<AlibabaCloud.OpenApiClient.Models.Config>("AlibabaCloud");

        services.AddScoped<AlibabaCloud.SDK.Alidns20150109.Client>(sp =>
        {
            var clientConfig = sp.GetRequiredService<IOptionsSnapshot<AlibabaCloud.OpenApiClient.Models.Config>>().Value;
            return new AlibabaCloud.SDK.Alidns20150109.Client(clientConfig);
        });

        return services;
    }

    public static IServiceCollection AddAlibabaCloudDnsChallengeProvider(this IServiceCollection services)
    {
        services.TryAddScoped<IDnsChallengeProvider, AliDnsChallengeProvider>();
        return services.AddAlibabaCloud();
    }
}

public class AliDnsChallengeProvider : IDnsChallengeProvider
{
    private readonly ILogger<AliDnsChallengeProvider> _logger;
    private readonly AlibabaCloud.SDK.Alidns20150109.Client _client;

    public AliDnsChallengeProvider(ILogger<AliDnsChallengeProvider> logger, AlibabaCloud.SDK.Alidns20150109.Client client)
    {
        _logger = logger;
        _client = client;
    }

    public async Task<DomainTxtRecordContext> AddDomainTxtRecordAsync(string acmeDomain, string txtRecord, CancellationToken cancellationToken)
    {
        var (rr, rootDomainName) = SplitDomainName(acmeDomain);

        var existsRecordId = await ExistsDomainTxtRecordAsync(rootDomainName, rr, txtRecord, cancellationToken);

        if (existsRecordId is null)
        {
            _logger.LogDebug("[AliDns] Dns01 Challenge: {DomainName}. RootDomain:{domain}, Record value:{rr}", acmeDomain, rootDomainName, rr);

            var resp = await _client.AddDomainRecordAsync(new AlibabaCloud.SDK.Alidns20150109.Models.AddDomainRecordRequest
            {
                DomainName = rootDomainName,
                RR = rr,
                Type = "TXT",
                Value = txtRecord
            });

            if (resp.StatusCode != 200)
            {
                _logger.LogInformation("Create DomainRecord failed: {@Response}", resp);
                throw new Exception("Create Domain TXT Record failed");
            }

            return new DomainTxtRecordContext(acmeDomain, txtRecord, resp.Body.RecordId);
        }

        return new DomainTxtRecordContext(acmeDomain, txtRecord, existsRecordId);
    }

    private async Task<string?> ExistsDomainTxtRecordAsync(string rootDomainName, string rr, string txtRecord, CancellationToken cancellationToken)
    {
        try
        {
            var exists = await _client.DescribeDomainRecordsAsync(new()
            {
                DomainName = rootDomainName,
                Type = "TXT"
            });

            var same = exists.Body.DomainRecords.Record.FirstOrDefault(x => x.Type == "TXT" && x.RR == rr && x.Value == txtRecord);

            return same?.RecordId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Check {RR} of {RootDomain} for {TxtValue}", rr, rootDomainName, txtRecord);
            return null;
        }
    }

    public async Task RemoveTxtRecordAsync(DomainTxtRecordContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.Key))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var resp = await _client.DeleteDomainRecordAsync(new AlibabaCloud.SDK.Alidns20150109.Models.DeleteDomainRecordRequest
        {
            RecordId = context.Key,
        });

        _logger.LogDebug("Delete domain record result: {@Response}", resp);
    }

    private (string rr, string rootDomainName) SplitDomainName(string domainName)
    {
        var spans = domainName.Split('.').AsSpan<string>();

        var index = spans.Length - 2;

        var rr = string.Join(".", spans[..index]!);
        var domain = string.Join(".", spans[index..]!);

        return (rr, domain);
    }
}

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using DnsClient;
using DnsClient.Protocol;

using Microsoft.Extensions.Configuration;

namespace CertsCenter.Acme;

public static class AcmeHelper
{
    public static string ComputeHash(string value)
    {
        using (var sha = SHA256.Create())
        {
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
            return BitConverter.ToString(hash).Replace("-", "");
        }
    }
}

internal static class EnumerableExtensions
{
    internal static bool IsNullOrEmpty<T>([NotNullWhen(false)]this IEnumerable<T>? source)
    {
        return source == null || !source.Any();
    }
}

public static class DnsUtil
{
    public static void Configure(IConfiguration configuration) => configuration?.GetSection(nameof(DnsServers))?.Bind(DnsServers);

    public static string[] DnsServers = [];

    private static readonly Lazy<LookupClient> _Client = new Lazy<LookupClient>(() => BuildLookupClient(DnsServers));

    public static LookupClient BuildLookupClient(params IEnumerable<string> dnsServers)
    {
        if (dnsServers.IsNullOrEmpty())
        {
            return new DnsClient.LookupClient();
        }
        else
        {
            var nameServers = dnsServers.SelectMany(x => Dns.GetHostAddresses(x)).ToArray();
            return new DnsClient.LookupClient(nameServers);
        }
    }


    public static LookupClient Client => _Client.Value;

    public static async Task<IEnumerable<string>?> LookupRecordAsync(string type, string name, CancellationToken cancellationToken)
    {
        var dnsType = (DnsClient.QueryType)Enum.Parse(typeof(DnsClient.QueryType), type);
        var dnsResp = await Client.QueryAsync(name, dnsType, cancellationToken: cancellationToken);

        if (dnsResp.HasError)
        {
            if ("Non-Existent Domain".Equals(dnsResp.ErrorMessage,
                    StringComparison.OrdinalIgnoreCase))
                return null;
            throw new Exception("DNS lookup error:  " + dnsResp.ErrorMessage);
        }

        return dnsResp.AllRecords.SelectMany(x => x.ValueAsStrings());
    }

    public static IEnumerable<string> ValueAsStrings(this DnsResourceRecord drr)
    {
        switch (drr)
        {
            case TxtRecord txt:
                return txt.Text;
            default:
                return new[] { drr.ToString() };
        }
    }
}


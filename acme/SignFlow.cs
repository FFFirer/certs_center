using System;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Security.Cryptography.X509Certificates;

using ACMESharp;
using ACMESharp.Authorizations;
using ACMESharp.Protocol;
using ACMESharp.Protocol.Resources;

using CertsCenter.Acme;

using DnsClient.Protocol;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PKISharp.SimplePKI;

namespace CertsServer.Acme;


#region Service-AcmeState

public class SignFlowContext(CertificateRequest request)
{
    public CertificateRequest Request { get; set; } = request;

    public bool ReCreateOrder { get; set; }
    public string KeyAlgor { get; set; } = "ec";
    public int? KeySize { get; set; } = 256;

    public int TimeoutSeconds { get; set; } = 300;
}

public abstract class SignFlowState : AcmeState
{
    protected readonly ILogger _logger;
    protected readonly IOptionsSnapshot<SignFlowContext> _flowContext;

    public SignFlowState(
        ILogger<SignFlowState> logger,
        IOptionsSnapshot<SignFlowContext> flowContext,
        AcmeStateMachineContext context) : base(context)
    {
        _logger = logger;
        _flowContext = flowContext;
    }
}


public abstract class SyncSignFlowState : SignFlowState
{
    protected SyncSignFlowState(
        ILogger<SyncSignFlowState> logger,
        IOptionsSnapshot<SignFlowContext> flowContext,
        AcmeStateMachineContext context)
        : base(
            logger,
            flowContext,
            context)
    {

    }

    public override Task<IAcmeState> MoveNextAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var next = MoveNext();

        return Task.FromResult(next);
    }

    public abstract IAcmeState MoveNext();
}

#endregion

/// <summary>
/// 检查证书有效性
/// </summary>
public class CheckForRenewal : SignFlowState
{
    private readonly IAcmeStore _store;

    public CheckForRenewal(
        IAcmeStore store,
        AcmeClientFactory clientFactory,
        ILogger<CheckForRenewal> logger,
        IOptionsSnapshot<SignFlowContext> flowContext,
        AcmeStateMachineContext context) : base(logger, flowContext, context)
    {
        _store = store;
    }

    public override async Task<IAcmeState> MoveNextAsync(CancellationToken cancellationToken)
    {
        // TODO: 逐个检查当前上下文中申请的域名所对应的最新的证书是否到期

        var certificateBytes = await _store.LoadRawAsync<byte[]>(AcmeStoreKeys.PfxFile, cancellationToken, _flowContext.Value.Request.Id);

        if (certificateBytes.IsNullOrEmpty())
        {
            return MoveTo<BeginCertificateCreation>();
        }

        using (var cert = X509CertificateLoader.LoadPkcs12(certificateBytes, null))
        {
            if (cert.NotAfter <= DateTime.Now)
            {
                return MoveTo<BeginCertificateCreation>();
            }
        }

        return MoveTo<TerminalState>();
    }
}

/// <summary>
/// 加载或创建订单
/// </summary>
public class BeginCertificateCreation : SignFlowState
{
    private readonly AcmeCertificateFactory _certificateFactory;
    private readonly ICertificateStore _certificateStore;

    public BeginCertificateCreation(
        ICertificateStore certificateStore,
        AcmeCertificateFactory certificateFactory,
        ILogger<SignFlowState> logger,
        IOptionsSnapshot<SignFlowContext> flowContext,
        AcmeStateMachineContext context) : base(logger, flowContext, context)
    {
        _certificateStore = certificateStore;
        _certificateFactory = certificateFactory;
    }

    public override async Task<IAcmeState> MoveNextAsync(CancellationToken cancellationToken)
    {
        var domainNames = _flowContext.Value.Request.Domains;

        try
        {
            var account = await _certificateFactory.GetOrCreateAccountAsync(cancellationToken);

            _logger.LogInformation("Using account {UserId}", account.Id);

            var order = await _certificateFactory.GetOrCreateOrderAsync(_flowContext.Value.Request, null, true, cancellationToken);
            var certificate = await _certificateFactory.CreateCertificateAsync(_flowContext.Value.Request, order, cancellationToken);

            _logger.LogInformation("Created certificate {Subject} {Thumbprint}", certificate.Subject, certificate.Thumbprint);

            await SaveCertificateAsync(certificate, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to automatically create a certificate for {hostname}", domainNames);
            throw;
        }

        return MoveTo<TerminalState>();
    }

    private async Task SaveCertificateAsync(X509Certificate2 certificate, CancellationToken cancellationToken)
    {
        await _certificateStore.SaveAsync(certificate, cancellationToken);
    }
}

public class X509StoreCertificateStore : ICertificateStore
{
    public Task<X509Certificate2?> FindAsync(string path, CancellationToken cancellationToken)
    {
        using var x509store = new X509Store(StoreName.My, StoreLocation.CurrentUser);

        x509store.Open(OpenFlags.ReadOnly);
        var cert = x509store.Certificates.Where(x => x.Thumbprint == path).FirstOrDefault();

        x509store.Close();
        return Task.FromResult(cert);
    }

    public Task<string> SaveAsync(X509Certificate2 certificate, CancellationToken cancellationToken)
    {
        using var x509store = new X509Store(StoreName.My, StoreLocation.CurrentUser);

        x509store.Open(OpenFlags.ReadWrite);

        x509store.Add(certificate);

        x509store.Close();

        return Task.FromResult(certificate.Thumbprint);
    }


}

public interface ICertificateStore
{
    Task<X509Certificate2?> FindAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="certificate"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>
    /// path
    /// </returns>
    Task<string> SaveAsync(X509Certificate2 certificate, CancellationToken cancellationToken);
}

public interface IDnsChallengeProvider
{
    Task RemoveTxtRecordAsync(DomainTxtRecordContext context, CancellationToken cancellationToken);
    Task<DomainTxtRecordContext> AddDomainTxtRecordAsync(string acmeDomain, string txtRecord, CancellationToken cancellationToken);
}

public enum ExportCertType
{
    Pfx,
    Pem
}

public class AcmeCertificateFactory
{
    private readonly ILogger _logger;
    private readonly AcmeClientFactory _clientFactory;
    private readonly IDnsChallengeProvider _dnsChallengeProvider;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IAcmeStore _acmeStore;

    public AcmeCertificateFactory(
        IDnsChallengeProvider dnsChallengeProvider,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<AcmeCertificateFactory> logger,
        AcmeClientFactory clientFactory,
        IAcmeStore acmeStore)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _dnsChallengeProvider = dnsChallengeProvider;
        _logger = logger;
        _clientFactory = clientFactory;
        _acmeStore = acmeStore;
    }

    public async Task<Account> GetOrCreateAccountAsync(CancellationToken cancellationToken)
    {
        return await _clientFactory.GetOrCreateAccountAsync(cancellationToken);
    }

    public async Task<X509Certificate2> CreateCertificateAsync(CertificateRequest request, OrderDetails order, CancellationToken cancellationToken)
    {
        var authUrls = order.Payload.Authorizations;
        cancellationToken.ThrowIfCancellationRequested();
        await Task.WhenAll(BeginValidateAllAuthorizations(authUrls, cancellationToken));

        cancellationToken.ThrowIfCancellationRequested();
        return await CompleteCertificateRequestAsync(order, request, cancellationToken);
    }

    public async Task<X509Certificate2> CreateCertificateAsync(CertificateRequest request, string? orderUrl, CancellationToken cancellationToken)
    {
        var acme = await _clientFactory.Create(cancellationToken: cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var order = await GetOrCreateOrderAsync(request, orderUrl, true, cancellationToken);

        if (order is null)
        {
            throw new InvalidOperationException("Order is null: '" + orderUrl + "'");
        }

        return await CreateCertificateAsync(request, order, cancellationToken);
    }

    public async Task<OrderDetails> GetOrCreateOrderAsync(CertificateRequest request, string? orderUrl, bool reCreateIfInvalid, CancellationToken cancellationToken)
    {
        var acme = await _clientFactory.Create(cancellationToken: cancellationToken);

        OrderDetails? order = null;

        if (string.IsNullOrWhiteSpace(orderUrl))
        {
            order = await acme.CreateOrderAsync(request.Domains.Distinct(), cancel: cancellationToken);
            _logger.LogInformation("Created order: {OrderUrl}", order.OrderUrl);
        }
        else
        {
            order = await acme.GetOrderDetailsAsync(orderUrl, null, cancellationToken);
            _logger.LogInformation("Loaded order: {OrderUrl}", orderUrl);
        }

        if (reCreateIfInvalid && order is null || order.Payload.Status == AcmeConst.InvalidStatus)
        {
            order = await acme.CreateOrderAsync(request.Domains.Distinct(), cancel: cancellationToken);
            _logger.LogInformation("Created order: {OrderUrl}", order.OrderUrl);
        }

        return order;
    }

    private async Task<X509Certificate2> CompleteCertificateRequestAsync(OrderDetails order, CertificateRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Completing order {Url}", order.OrderUrl);

        var acme = await _clientFactory.Create(cancellationToken: cancellationToken);

        // check all authorizations
        order = await acme.GetOrderDetailsAsync(order.OrderUrl, existing: order, cancellationToken);

        var keyPair = CreateKeyPair(request.KeyAlgor, request.KeySize);
        var certCsr = CreateCertCsr(request.Domains, keyPair);

        order = await acme.FinalizeOrderAsync(order.Payload.Finalize, certCsr, cancellationToken);

        _logger.LogInformation("Finalizing order {Url}", order.OrderUrl);

        var testUtil = DateTime.Now.Add(TimeSpan.FromMinutes(10));

        while (!cancellationToken.IsCancellationRequested && DateTime.Now < testUtil)
        {
            if (order.Payload.Status == AcmeConst.ValidStatus)
            {
                _logger.LogInformation("Order {Url} is valid", order.OrderUrl);
                await SaveOrderWithPkiKeyPair(request, order, keyPair, default); // 因为密钥对已经用于Finalize，所以保存KeyPair大部分情况不应被打断
                break;
            }

            if (order.Payload.Status == AcmeConst.InvalidStatus)
            {
                _logger.LogWarning("Order is invalid, Details: {@Content}", order);
                throw new Exception("Cannot generate certificate because order is " + order.Payload.Status);
            }

            if (DateTime.Now < testUtil)
            {
                _logger.LogDebug("Checking order status {OrderUrl} waiting for 5s... Current: {Status}", order.OrderUrl, order.Payload.Status);
                await Task.Delay(TimeSpan.FromSeconds(5));
                order = await acme.GetOrderDetailsAsync(order.OrderUrl, order, cancellationToken);
            }
        }

        _logger.LogInformation("Completed order {Url}", order.OrderUrl);

        var certBytes = await LoadOrderCert(request, order, cancellationToken);

        _logger.LogInformation("Exported certificate for order {Url}", order.OrderUrl);

        return X509CertificateLoader.LoadCertificate(certBytes);
    }

    /// <summary>
    /// 创建密钥对
    /// </summary>
    /// <param name="keyAlgor"></param>
    /// <param name="keySize"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static PkiKeyPair CreateKeyPair(string keyAlgor, int? keySize)
    {
        PkiKeyPair? keyPair = null; // public key + private key

        switch (keyAlgor)
        {
            case AcmeConst.RsaKeyType:
                keyPair = PkiKeyPair.GenerateRsaKeyPair(keySize ?? AcmeConst.DefaultAlgorKeySizeMap[keyAlgor]);
                break;
            case AcmeConst.EcKeyType:
                keyPair = PkiKeyPair.GenerateEcdsaKeyPair(keySize ?? AcmeConst.DefaultAlgorKeySizeMap[keyAlgor]);
                break;
            default:
                throw new Exception($"Unknown key algorithm type [{keyAlgor}]");
        }

        return keyPair;
    }

    /// <summary>
    /// create csr
    /// </summary>
    /// <param name="domainNames"></param>
    /// <param name="keyPair"></param>
    /// <returns></returns>
    public static byte[] CreateCertCsr(string[] domainNames, PkiKeyPair keyPair)
    {
        PkiCertificateSigningRequest? csr = null;

        csr = GenerateCsr(domainNames, keyPair);

        var certCsr = csr.ExportSigningRequest(PkiEncodingFormat.Der);

        return certCsr;
    }

    public async Task<byte[]> Export(CertificateRequest request, string orderUrl, ExportCertType exportCertType, CancellationToken cancellationToken)
    {
        return exportCertType switch
        {
            ExportCertType.Pfx => await ExportPfx(request, orderUrl, cancellationToken),
            ExportCertType.Pem => await ExportPem(request, orderUrl, cancellationToken),
            _ => throw new NotSupportedException($"Not supported export type: {exportCertType}")
        };
    }

    public async Task<byte[]> ExportPfx(CertificateRequest request, string orderUrl, CancellationToken cancellationToken)
    {
        var acme = await _clientFactory.Create(false, cancellationToken);
        var order = await acme.GetOrderDetailsAsync(orderUrl, null, cancellationToken);
        if (order.Payload.Status != AcmeConst.ValidStatus)
        {
            throw new InvalidOperationException($"Order status for id: {request.Id} is not valid");
        }

        var keyPair = await LoadOrderKeyPair(request, order, cancellationToken);
        var certBytes = await LoadOrderCert(request, order, cancellationToken);

        var pfxPassword = request.PfxPassword;
        if (string.IsNullOrWhiteSpace(pfxPassword))
        {
            pfxPassword = null;
        }

        using var cert = X509CertificateLoader.LoadCertificate(certBytes);
        var pkiCert = PkiCertificate.From(cert);
        var pfx = pkiCert.Export(PkiArchiveFormat.Pkcs12,
            privateKey: keyPair.PrivateKey,
            password: pfxPassword?.ToCharArray());

        return pfx;
    }

    private async Task<byte[]> LoadOrderCert(CertificateRequest request, OrderDetails order, CancellationToken cancellationToken)
    {
        if (await _acmeStore.ExistsAsync(AcmeStoreKeys.AcmeOrderCert, cancellationToken, request.Id))
        {
            return (await _acmeStore.LoadRawAsync<byte[]>(AcmeStoreKeys.AcmeOrderCert, cancellationToken, request.Id))!;
        }

        var acme = await _clientFactory.Create(false, cancellationToken);
        var certBytes = await GetOrderCertificate(acme, order, cancellationToken);

        await _acmeStore.SaveRawAsync(certBytes, AcmeStoreKeys.AcmeOrderCert, cancellationToken, request.Id);
        return certBytes;
    }

    private async Task<byte[]> ExportPem(CertificateRequest request, string orderUrl, CancellationToken cancellationToken)
    {
        var acme = await _clientFactory.Create(false, cancellationToken);
        var order = await acme.GetOrderDetailsAsync(orderUrl, null, cancellationToken);
        if (order.Payload.Status != AcmeConst.ValidStatus)
        {
            throw new InvalidOperationException($"Order status for order: {request.Id} is not valid");
        }

        var certBytes = await LoadOrderCert(request, order, cancellationToken);

        return certBytes;
    }

    private async Task<PkiKeyPair> LoadOrderKeyPair(CertificateRequest request, OrderDetails order, CancellationToken cancellationToken)
    {
        var certKey = await _acmeStore.LoadRawAsync<string>(AcmeStoreKeys.AcmeOrderCertKey, cancellationToken, request.Id);
        if (certKey is null)
        {
            throw new InvalidOperationException($"Certkey for request-id: {request.Id} not exists");
        }

        return Base64ToPkiKeyPair(certKey);
    }

    /// <summary>
    /// 生成PFX
    /// </summary>
    /// <param name="acme"></param>
    /// <param name="order"></param>
    /// <param name="keyPair"></param>
    /// <param name="pfxPassword"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public static async Task<X509Certificate2> ExportPfx(AcmeProtocolClient acme, OrderDetails order, PkiKeyPair keyPair, string? pfxPassword, CancellationToken cancellationToken)
    {
        var certBytes = await GetOrderCertificate(acme, order, cancellationToken);
        return ExportPfx(certBytes, keyPair, pfxPassword, cancellationToken);
    }

    public static X509Certificate2 ExportPfx(byte[] fullChain, PkiKeyPair keyPair, string? pfxPassword, CancellationToken cancellationToken)
    {
        // 生成证书
        var privateKey = keyPair.PrivateKey;

        using var cert = X509CertificateLoader.LoadCertificate(fullChain);

        var pkiCert = PkiCertificate.From(cert);

        var pfx = pkiCert.Export(PkiArchiveFormat.Pkcs12, privateKey: privateKey, password: pfxPassword?.ToCharArray());

        return X509CertificateLoader.LoadPkcs12(pfx, pfxPassword);
    }

    /// <summary>
    /// 生成PFX
    /// </summary>
    /// <param name="acme"></param>
    /// <param name="order"></param>
    /// <param name="keyPair"></param>
    /// <param name="pfxPassword"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public static async Task<byte[]> GetOrderCertificate(AcmeProtocolClient acme, OrderDetails order, CancellationToken cancellationToken)
    {
        // 生成证书
        var certResp = await acme.GetAsync(order.Payload.Certificate);
        certResp.EnsureSuccessStatusCode();

        var certBytes = await certResp.Content.ReadAsByteArrayAsync(cancellationToken);

        return certBytes;
    }

    public virtual async Task SaveOrderWithPkiKeyPair(CertificateRequest request, OrderDetails order, PkiKeyPair keyPair, CancellationToken cancellationToken)
    {
        var certKeys = PkiKeyPairToBase64(keyPair);
        await _acmeStore.SaveRawAsync(certKeys, AcmeStoreKeys.AcmeOrderCertKey, cancellationToken, request.Id);
    }

    /// <summary>
    /// 密钥对
    /// </summary>
    /// <param name="keyPair"></param>
    /// <returns></returns>
    public static string PkiKeyPairToBase64(PkiKeyPair keyPair)
    {
        using (var ms = new MemoryStream())
        {
            keyPair.Save(ms);
            return Convert.ToBase64String(ms.ToArray());
        }
    }

    /// <summary>
    /// 密钥对
    /// </summary>
    /// <param name="base64Str"></param>
    /// <returns></returns>
    public static PkiKeyPair Base64ToPkiKeyPair(string base64Str)
    {
        using (var ms = new MemoryStream(Convert.FromBase64String(base64Str)))
        {
            return PkiKeyPair.Load(ms);
        }
    }

    private IEnumerable<Task> BeginValidateAllAuthorizations(string[] authzDetailUrls, CancellationToken cancellationToken)
    {
        foreach (var authzDetailUrl in authzDetailUrls)
        {
            yield return ValidateDomainOwnershipAsync(authzDetailUrl, cancellationToken);
        }
    }

    public static PkiCertificateSigningRequest GenerateCsr(IEnumerable<string> dnsNames,
        PkiKeyPair keyPair)
    {
        var firstDns = dnsNames.First();
        var csr = new PkiCertificateSigningRequest($"CN={firstDns}", keyPair,
            PkiHashAlgorithm.Sha256);

        csr.CertificateExtensions.Add(
            PkiCertificateExtension.CreateDnsSubjectAlternativeNames(dnsNames));

        return csr;
    }

    private async Task ValidateDomainOwnershipAsync(string authzDetailUrl, CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateAsyncScope();

        var acme = await (scope.ServiceProvider.GetRequiredService<AcmeClientFactory>()).Create(cancellationToken: cancellationToken);
        var authz = await acme.GetAuthorizationDetailsAsync(authzDetailUrl, cancellationToken);

        _logger.LogInformation("Authz {Url} is {Status}", authzDetailUrl, authz.Status);

        if (authz.Status == AcmeConst.ValidStatus)
        {
            return;
        }

        var domainName = authz.Identifier.Value;

        var dnsChallenges = authz.Challenges.Where(x => x.Type.Equals("dns-01", StringComparison.OrdinalIgnoreCase)).ToList();

        _logger.LogInformation("Start validate {DomainNames} ownership for {Authz} with {Count} DNS-01 challenge", domainName, authzDetailUrl, dnsChallenges.Count);

        await Task.WhenAll(dnsChallenges.Select(c => DoChallenges(c, authzDetailUrl, authz, domainName, cancellationToken)));
    }

    private async Task DoChallenges(Challenge dnsChallenge, string authzDetailUrl, Authorization authz, string domainName, CancellationToken cancellationToken)
    {
        if (dnsChallenge is null)
        {
            _logger.LogInformation("No 'dns-01' challenge for {Authz}", authzDetailUrl);
            throw new InvalidOperationException($"No 'dns-01' challenge for {authzDetailUrl}");
        }

        _logger.LogInformation("Start challenge {Domain} using {ChallengeUrl} for {Authz} ", domainName, authzDetailUrl, dnsChallenge.Url);

        using var scope = _serviceScopeFactory.CreateAsyncScope();
        var acme = await (scope.ServiceProvider.GetRequiredService<AcmeClientFactory>()).Create(cancellationToken: cancellationToken);

        var context = new DomainTxtRecordContext(domainName, string.Empty, string.Empty);

        try
        {
            context = await PrepareDns01ChallengeResponseAsync(acme, authz, dnsChallenge, cancellationToken);
            await WaitForChallengeResultAsync(acme, authzDetailUrl, dnsChallenge, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Challenge {Url} failed", dnsChallenge.Url);
            throw;
        }
        finally
        {
            await _dnsChallengeProvider.RemoveTxtRecordAsync(context, cancellationToken);
        }
    }

    private async Task WaitForChallengeResultAsync(AcmeProtocolClient acme, string authzDetailsUrl, Challenge dnsChallenge, CancellationToken cancellationToken)
    {
        var challengeUpdated = await acme.AnswerChallengeAsync(dnsChallenge.Url, cancellationToken);

        var testUtil = DateTime.Now.Add(TimeSpan.FromMinutes(10));

        while (!cancellationToken.IsCancellationRequested && DateTime.Now < testUtil)
        {
            var authzUpdated = await acme.GetAuthorizationDetailsAsync(authzDetailsUrl, cancellationToken);

            if (authzUpdated.Status == AcmeConst.ValidStatus)
            {
                _logger.LogInformation("Authz {Url} is valid", authzDetailsUrl);
                break;
            }

            if (authzUpdated.Status == AcmeConst.InvalidStatus)
            {
                _logger.LogDebug("Authz is invalid, Details: {@Content}", authzUpdated);
                throw new Exception($"Authorization is invalid");
            }

            if (DateTime.Now < testUtil)
            {
                _logger.LogInformation("Waiting for challenge {Challenge} result ...5s, Current is {Status}", dnsChallenge.Url, authzUpdated.Status);
                await Task.Delay(5 * 1000);
            }
        }
    }

    /// <summary>
    /// Prepare dns-01 challenge
    /// </summary>
    /// <param name="acme"></param>
    /// <param name="authz"></param>
    /// <param name="dnsChallenge"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    private async Task<DomainTxtRecordContext> PrepareDns01ChallengeResponseAsync(AcmeProtocolClient acme, Authorization authz, Challenge dnsChallenge, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var challengeValidation = AuthorizationDecoder.DecodeChallengeValidation(authz, dnsChallenge.Type, acme.Signer);

        if (challengeValidation is Dns01ChallengeValidationDetails dns01ChallengeValidation)
        {
            var acmeDomain = dns01ChallengeValidation.DnsRecordName;
            var txtRecord = dns01ChallengeValidation.DnsRecordValue;

            var context = await _dnsChallengeProvider.AddDomainTxtRecordAsync(acmeDomain, txtRecord, cancellationToken);

            _logger.LogInformation("Validating dns challenge");
            await ValidateChallengeAsync(context, dnsChallenge, cancellationToken);

            return context;
        }

        throw new InvalidOperationException($"Except dns-01 but {challengeValidation.ChallengeType}");
    }

    /// <summary>
    /// validate dns challenge
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    private async Task ValidateChallengeAsync(DomainTxtRecordContext context, Challenge dnsChallenge, CancellationToken cancellationToken)
    {
        var testUtil = DateTime.Now.Add(TimeSpan.FromMinutes(10));

        while (!cancellationToken.IsCancellationRequested && DateTime.Now < testUtil)
        {
            string? err = null;

            try
            {
                var lookup = await DnsUtil.LookupRecordAsync("TXT", context.Domain, cancellationToken);
                var dnsValues = lookup?.Select(x => x.Trim('"'));

                if (dnsValues.IsNullOrEmpty())
                {
                    err = "Could not resolve *any* DNS entries for Challenge record name";
                }
                else if (dnsValues.Contains(context.Txt) == false)
                {
                    var dnsValuesFlattened = string.Join(",", dnsValues);
                    err = $"DNS entry does not match expected '{context.Txt}' for Challenge record name s{context.Domain} not in ({dnsValuesFlattened})";
                }
                else
                {
                    err = null;
                    _logger.LogInformation("Found expected DNS entry for Challenge {Name}: {Values}", context.Domain, dnsValues);

                    break;
                }
            }
            catch (Exception ex1)
            {
                err = ex1.Message;
                _logger.LogDebug(ex1, "Checking dns-01 Failed");
            }

            if (err is not null)
            {
                _logger.LogWarning("Failed: {error}", err);
            }

            if (DateTime.Now < testUtil)
            {
                await Task.Delay(5 * 1000);
                _logger.LogInformation("Waiting for validate challenge...5s");
            }
        }
    }

    public async Task<OrderDetails> GetOrderAsync(string orderUrl, OrderDetails? existsing, CancellationToken cancellationToken)
    {
        var acme = await _clientFactory.Create(cancellationToken: cancellationToken);
        var order = await acme.GetOrderDetailsAsync(orderUrl, existsing, cancellationToken);
     
        return order;
    }
}

public record DomainTxtRecordContext(string Domain, string Txt, string Key);
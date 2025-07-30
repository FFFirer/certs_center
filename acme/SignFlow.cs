using System;
using System.IO.Compression;
using System.Reflection.Emit;
using System.Security.Cryptography.X509Certificates;
using ACMESharp;
using ACMESharp.Authorizations;
using ACMESharp.Protocol;
using ACMESharp.Protocol.Resources;
using CertsCenter.Acme;
using DnsClient.Protocol;
using Microsoft.AspNetCore.Authorization;
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

            var certificate = await _certificateFactory.CreateCertificateAsync(_flowContext.Value.Request, cancellationToken);

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
    public ValueTask SaveAsync(X509Certificate2 certificate, CancellationToken cancellationToken)
    {
        using var x509store = new X509Store(StoreName.My, StoreLocation.LocalMachine);

        x509store.Add(certificate);

        return ValueTask.CompletedTask;
    }
}

public interface ICertificateStore
{
    ValueTask SaveAsync(X509Certificate2 certificate, CancellationToken cancellationToken);
}

public interface IDnsChallengeProvider
{
    Task RemoveTxtRecordAsync(DomainTxtRecordContext context, CancellationToken cancellationToken);
    Task<DomainTxtRecordContext> AddDomainTxtRecordAsync(string acmeDomain, string txtRecord, CancellationToken cancellationToken);
}

public class AcmeCertificateFactory
{
    private readonly ILogger _logger;
    private readonly AcmeClientFactory _clientFactory;
    private readonly IDnsChallengeProvider _dnsChallengeProvider;

    public AcmeCertificateFactory(
        IDnsChallengeProvider dnsChallengeProvider,
        ILogger<AcmeCertificateFactory> logger,
        AcmeClientFactory clientFactory)
    {
        _dnsChallengeProvider = dnsChallengeProvider;
        _logger = logger;
        _clientFactory = clientFactory;
    }

    public async Task<Account> GetOrCreateAccountAsync(CancellationToken cancellationToken)
    {
        var acme = await _clientFactory.Create(cancellationToken: cancellationToken);
        return acme.Account.Payload;
    }

    public async Task<X509Certificate2> CreateCertificateAsync(CertificateRequest request, CancellationToken cancellationToken)
    {
        var acme = await _clientFactory.Create(cancellationToken: cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        var order = await acme.CreateOrderAsync(request.Domains.Distinct(), cancel: cancellationToken);

        var authUrls = order.Payload.Authorizations;
        cancellationToken.ThrowIfCancellationRequested();
        await Task.WhenAll(BeginValidateAllAuthorizations(acme, authUrls, cancellationToken));

        cancellationToken.ThrowIfCancellationRequested();
        return await CompleteCertificateRequestAsync(order, request, cancellationToken);
    }

    private async Task<X509Certificate2> CompleteCertificateRequestAsync(OrderDetails order, CertificateRequest request, CancellationToken cancellationToken)
    {
        var acme = await _clientFactory.Create(cancellationToken: cancellationToken);
        // check all authorizations
        order = await acme.GetOrderDetailsAsync(order.OrderUrl, existing: order, cancellationToken);

        var keyPair = CreateKeyPair(request.KeyAlgor, request.KeySize);
        var certCsr = CreateCertCsr(request.Domains, keyPair);

        order = await acme.FinalizeOrderAsync(order.Payload.Finalize, certCsr, cancellationToken);

        var testUtil = DateTime.Now.Add(TimeSpan.FromMinutes(10));
        while (!cancellationToken.IsCancellationRequested && DateTime.Now < testUtil)
        {
            switch (order.Payload.Status)
            {
                case AcmeConst.ValidStatus:
                    SaveOrderWithPkiKeyPair(order, keyPair);
                    break;

                case AcmeConst.InvalidStatus:
                    throw new Exception("Cannot generate certificate because order is " + order.Payload.Status);

                default:
                    if (DateTime.Now < testUtil)
                    {
                        _logger.LogDebug("Wait for check order status ...5s");
                        await Task.Delay(TimeSpan.FromSeconds(5));
                    }
                    break;
            }
        }

        return await ExportPfx(acme, order, keyPair, request.PfxPassword, cancellationToken);
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

    public virtual void SaveOrderWithPkiKeyPair(OrderDetails order, PkiKeyPair keyPair)
    {

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

    private IEnumerable<Task> BeginValidateAllAuthorizations(AcmeProtocolClient acme, string[] authzDetailUrls, CancellationToken cancellationToken)
    {
        foreach (var authzDetailUrl in authzDetailUrls)
        {
            yield return ValidateDomainOwnershipAsync(acme, authzDetailUrl, cancellationToken);
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

    private async Task ValidateDomainOwnershipAsync(AcmeProtocolClient acme, string authzDetailUrl, CancellationToken cancellationToken)
    {
        var authz = await acme.GetAuthorizationDetailsAsync(authzDetailUrl, cancellationToken);

        if (authz.Status == AcmeConst.ValidStatus)
        {
            return;
        }

        var domainName = authz.Identifier.Value;

        var dnsChallenge = authz.Challenges.Where(x => x.Type.Equals("dns-01", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();

        if (dnsChallenge is null)
        {
            _logger.LogInformation("No challenge for dns-01");
            return;
        }

        var context = new DomainTxtRecordContext(domainName, string.Empty, string.Empty);

        try
        {
            context = await PrepareDns01ChallengeResponseAsync(acme, authz, dnsChallenge, cancellationToken);
            await WaitForChallengeResultAsync(acme, authzDetailUrl, dnsChallenge, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Challenge failed");
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

            switch (authzUpdated.Status)
            {
                case AcmeConst.ValidStatus:
                    break;

                case AcmeConst.InvalidStatus:
                    _logger.LogDebug("Current authz is {@Details}", authzUpdated);
                    throw new Exception($"Authorization is invalid");

                default:
                    if (DateTime.Now < testUtil)
                    {
                        await Task.Delay(5 * 1000);
                        _logger.LogInformation("Waiting for challenge result ...5s");
                    }
                    break;
            }
        }
    }

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

        while (!cancellationToken.IsCancellationRequested || DateTime.Now < testUtil)
        {
            string? err = null;

            try
            {
                var lookup = await DnsUtil.LookupRecordAsync("TXT", context.Txt, cancellationToken);
                var dnsValues = lookup?.Select(x => x.Trim('"'));

                if (dnsValues.IsNullOrEmpty())
                {
                    err = "Could not resolve *any* DNS entries for Challenge record name";
                }
                else if (dnsValues.Contains(context.Domain) == false)
                {
                    var dnsValuesFlattened = string.Join(",", dnsValues);
                    err = $"DNS entry does not match expected value for Challenge record name ({context.Domain} not in {dnsValuesFlattened})";
                }
                else
                {
                    err = null;
                    _logger.LogInformation("Found expected DNS entry for Challenge record name: {@names}", dnsValues);

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
}

public record DomainTxtRecordContext(string Domain, string Txt, string Key);
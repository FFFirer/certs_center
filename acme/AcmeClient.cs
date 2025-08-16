using System;
using System.Diagnostics.CodeAnalysis;

using ACMESharp.Crypto.JOSE;
using ACMESharp.Protocol;
using ACMESharp.Protocol.Resources;

using CertsCenter.Acme;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CertsServer.Acme;

public class AcmeClientFactory : IDisposable
{
    private readonly IHttpClientFactory _factory;
    private readonly ILogger<AcmeClientFactory> _logger;
    private readonly IOptions<AcmeOptions> _options;
    private readonly IAcmeStore _store;
    private bool _disposed;
    private readonly ILoggerFactory _loggerFactory;

    public AcmeClientFactory(
        ILoggerFactory loggerFactory,
        IHttpClientFactory factory,
        ILogger<AcmeClientFactory> logger,
        IOptions<AcmeOptions> options,
        IAcmeStore store)
    {
        _loggerFactory = loggerFactory;
        _factory = factory;
        _logger = logger;
        _options = options;
        _store = store;
    }

    private AcmeProtocolClient? _client;

    public async ValueTask<AcmeProtocolClient> Create(bool refresh = false, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AcmeClientFactory));
        }

        await GetOrCreateAccountAsync(cancellationToken);

        //if (refresh)
        //{
        //    _client = await CreateClient(refresh, cancellationToken).ConfigureAwait(false);
        //}

        //if (_client is null)
        //{
        //    _client = await CreateClient(refresh, cancellationToken).ConfigureAwait(false);
        //}

        return await CreateClient(refresh,  cancellationToken).ConfigureAwait(false);
    }

    private async Task<AcmeProtocolClient> CreateClient(bool refresh, CancellationToken cancellationToken)
    {
        var http = _factory.CreateClient(_options.Value.CaName);

        var clientLogger = _loggerFactory.CreateLogger<AcmeProtocolClient>();
        var acme = new AcmeProtocolClient(http, _serviceDirectory, _account, _accountSinger, logger: clientLogger, usePostAsGet: true);

        await CheckServiceDirectoryAsync(acme, cancellationToken);
        await CheckNonceAsync(acme, cancellationToken);

        return acme;
    }

    private async Task CheckServiceDirectoryAsync(AcmeProtocolClient acme, CancellationToken cancellationToken)
    {
        if (_serviceDirectory is null)
        {
            _serviceDirectory = await _store.LoadAsync<ServiceDirectory>(AcmeStoreKeys.AcmeDirectory, cancellationToken);
        }

        if (_serviceDirectory is null)
        {
            _serviceDirectory = await acme.GetDirectoryAsync(cancellationToken);
            await _store.SaveAsync(_serviceDirectory, AcmeStoreKeys.AcmeDirectory, cancellationToken);
        }

        acme.Directory = _serviceDirectory;
    }

    private async Task<AcmeProtocolClient> CheckNonceAsync(AcmeProtocolClient client, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(client.NextNonce))
        {
            await client.GetNonceAsync(cancellationToken);
        }

        return client;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // TODO: 释放托管状态(托管对象)
                _client?.Dispose();
            }

            // TODO: 释放未托管的资源(未托管的对象)并重写终结器
            // TODO: 将大型字段设置为 null
            _disposed = true;
        }
    }

    // // TODO: 仅当“Dispose(bool disposing)”拥有用于释放未托管资源的代码时才替代终结器
    // ~AcmeClientFactory()
    // {
    //     // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private AccountDetails? _account { get; set; }
    private IJwsTool? _accountSinger { get; set; }
    private ServiceDirectory? _serviceDirectory { get; set; }

    private async Task<ServiceDirectory> GetServiceDirectory(CancellationToken cancellationToken)
    {
        using var acme = await CreateClient(true, cancellationToken);
        return await acme.GetDirectoryAsync(cancellationToken);
    }

    private readonly Semaphore _semaphore = new Semaphore(1, 1);

    public async Task<Account> GetOrCreateAccountAsync(CancellationToken cancellationToken)
    {
        if (_account?.Payload is not null)
        {
            return _account.Payload;
        }

        _semaphore.WaitOne();

        try
        {
            _account = await _store.LoadAsync<AccountDetails>(AcmeStoreKeys.AcmeAccountDetails, cancellationToken);
            var accountKey = await _store.LoadAsync<AccountKey>(AcmeStoreKeys.AcmeAccountKey, cancellationToken);

            if (_account is null || accountKey is null)
            {

                if (_options.Value.Email.IsNullOrEmpty())
                {
                    throw new ArgumentException("Email is required");
                }

                if (_options.Value.AcceptTermOfService == false)
                {
                    throw new ArgumentException("Accept of teamservice is required");
                }

                using var acme = await CreateClient(true, cancellationToken);

                var account = await acme.CreateAccountAsync(_options.Value.Email.Select(x => $"mailto:{x}"), _options.Value.AcceptTermOfService, cancel: cancellationToken);
                _accountSinger = acme.Signer;
                accountKey = new AccountKey(_accountSinger.JwsAlg, _accountSinger.Export());

                await _store.SaveAsync(account, AcmeStoreKeys.AcmeAccountDetails, cancellationToken);
                await _store.SaveAsync(accountKey, AcmeStoreKeys.AcmeAccountKey, cancellationToken);

                _account = account;
            }


            if (_accountSinger is null)
            {
                _accountSinger = accountKey.GenerateTool();
            }

            return _account.Payload;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Get or create account failed");
        }
        finally
        {
            _semaphore.Release();
        }

        throw new Exception("Create account failed");
    }
}

using System;
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

    public AcmeClientFactory(
        IHttpClientFactory factory,
        ILogger<AcmeClientFactory> logger,
        IOptions<AcmeOptions> options,
        IAcmeStore store)
    {
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

        if (refresh)
        {
            _client = await CreateClient(refresh, cancellationToken).ConfigureAwait(false);
        }

        if (_client is null)
        {
            _client = await CreateClient(refresh, cancellationToken).ConfigureAwait(false);
        }

        return _client;
    }

    private async Task<AcmeProtocolClient> CreateClient(bool refresh, CancellationToken cancellationToken)
    {
        var acmeDir = await _store.LoadAsync<ServiceDirectory>(AcmeStoreKeys.AcmeDirectory, cancellationToken);
        var account = await _store.LoadAsync<AccountDetails>(AcmeStoreKeys.AcmeAccountDetails, cancellationToken);
        var accountKey = await _store.LoadAsync<AccountKey>(AcmeStoreKeys.AcmeAccountKey, cancellationToken);

        IJwsTool? accountSigner = default;
        string? accountKeyHash = default;

        if (accountKey is not null)
        {
            accountSigner = accountKey.GenerateTool();
            accountKeyHash = AcmeHelper.ComputeHash(accountSigner.Export());
        }

        var http = _factory.CreateClient(_options.Value.CaEndpoint);
        var acme = new AcmeProtocolClient(http, acmeDir, account, accountSigner, logger: _logger, usePostAsGet: true);

        if (acmeDir is null || refresh)
        {
            acmeDir = await acme.GetDirectoryAsync(cancellationToken);
            await _store.SaveAsync(acmeDir, AcmeStoreKeys.AcmeDirectory, cancellationToken);
            acme.Directory = acmeDir;
        }

        await CheckNonceAsync(acme, cancellationToken);

        return acme;
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
}

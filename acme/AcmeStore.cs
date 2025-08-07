using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CertsServer.Acme;


public interface IAcmeStore
{
    Task<T?> LoadAsync<T>(string keyFormat, CancellationToken cancellationToken = default, params IEnumerable<object> keyParams);

    Task<T?> LoadRawAsync<T>(string keyFormat, CancellationToken cancellationToken = default, params IEnumerable<object> keyParams);
    Task SaveAsync<T>(T value, string keyFormat, CancellationToken cancellationToken = default, params IEnumerable<object> keyParams);
    Task SaveRawAsync<T>(T value, string keyFormat, CancellationToken cancellationToken = default, params IEnumerable<object> keyParams);

    Task<bool> ExistsAsync(string keyFormat, CancellationToken cancellationToken = default, params IEnumerable<object> keyParams);
    Task RemoveAsync(string keyFormat, CancellationToken cancellationToken = default, params IEnumerable<object> keyParams);

}

#region 
public sealed class FileSystemAcmeStoreOptions
{
    public string Directory { get; set; } = "App_Data/acme_store";
}


public sealed class FileSystemAcmeStore : IAcmeStore
{
    private readonly IOptions<FileSystemAcmeStoreOptions> _options;
    private readonly ILogger<FileSystemAcmeStore> _logger;

    private readonly object _lockOfDir = new object();

    public FileSystemAcmeStore(
        IOptions<FileSystemAcmeStoreOptions> options,
        ILogger<FileSystemAcmeStore> logger)
    {
        _options = options;
        _logger = logger;
    }

    private DirectoryInfo? _dir;
    public string DirectoryPath
    {
        get
        {
            CheckDirectory();
            return _dir.FullName;
        }
    }

    [MemberNotNull(nameof(_dir))]
    private void CheckDirectory()
    {
        if (string.IsNullOrWhiteSpace(_options.Value.Directory))
        {
            throw new ArgumentNullException("FileSystemAcmeStoreOptions.Directory");
        }

        if (_dir is null)
        {
            lock (_lockOfDir)
            {
                if (_dir is null)
                {
                    if (Directory.Exists(_options.Value.Directory))
                    {
                        _dir = new DirectoryInfo(_options.Value.Directory);
                    }
                    else
                    {
                        _dir = Directory.CreateDirectory(_options.Value.Directory);
                    }
                }
            }
        }
    }

    public async Task<T?> LoadAsync<T>(string keyFormat, CancellationToken cancellationToken = default, params IEnumerable<object> keyParams)
    {
        var fileName = string.Format(keyFormat, keyParams.ToArray());
        var filePath = Path.Combine(DirectoryPath!, $"{fileName}");

        if (File.Exists(filePath) == false)
        {
            return default(T);
        }

        _logger.LogDebug("Load file from {Path}", filePath);

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: cancellationToken);
    }

    public async Task SaveAsync<T>(T value, string keyFormat, CancellationToken cancellationToken = default, params IEnumerable<object> keyParams)
    {
        var fileName = string.Format(keyFormat, keyParams.ToArray());
        var filePath = Path.Combine(DirectoryPath!, $"{fileName}");

        using var fs = File.Exists(filePath)
            ? new FileStream(filePath, FileMode.Truncate, FileAccess.Write, FileShare.Read)
            : new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);


        _logger.LogDebug("Save file to {Path}", filePath);

        await JsonSerializer.SerializeAsync(fs, value, cancellationToken: cancellationToken);
    }

    public Task<bool> ExistsAsync(string keyFormat, CancellationToken cancellationToken = default, params IEnumerable<object> keyParams)
    {
        var fileName = string.Format(keyFormat, keyParams.ToArray());
        var filePath = Path.Combine(DirectoryPath!, $"{fileName}");

        var exists = File.Exists(filePath);


        _logger.LogDebug("Exists file({Exists}): {Path}", exists, filePath);

        return Task.FromResult(exists);
    }

    public async Task<T?> LoadRawAsync<T>(string keyFormat, CancellationToken cancellationToken = default, params IEnumerable<object> keyParams)
    {
        var fileName = string.Format(keyFormat, keyParams.ToArray());
        var filePath = Path.Combine(DirectoryPath!, $"{fileName}");

        if (File.Exists(filePath) == false)
        {
            return default(T?);
        }

        if (typeof(T) == typeof(string))
        {
            _logger.LogDebug("Load raw <string> from {Path}", filePath);

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var sr = new StreamReader(fs);
            return (T)(object)(await sr.ReadToEndAsync(cancellationToken));
        }

        if (typeof(T) == typeof(byte[]))
        {
            _logger.LogDebug("Load raw <byte[]> from {Path}", filePath);

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var bytes = new byte[fs.Length];
            using var sr = new StreamReader(fs);
            // while(await sr.ReadAsync())

            using var ms = new MemoryStream();
            await fs.CopyToAsync(ms, cancellationToken);
            return (T)(object)(ms.ToArray());
        }

        if (typeof(T) == typeof(Stream) || typeof(T) == typeof(FileStream))
        {
            _logger.LogDebug("Load raw <stream> from {Path}", filePath);

            return (T)(object)(new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read));
        }

        throw new ArgumentException("Unsupported return type; must be one of:  string, byte[], Stream",
                    nameof(T));
    }

    public async Task SaveRawAsync<T>(T value, string keyFormat, CancellationToken cancellationToken = default, params IEnumerable<object> keyParams)
    {
        var fileName = string.Format(keyFormat, keyParams.ToArray());
        var filePath = Path.Combine(DirectoryPath!, $"{fileName}");

        using var fs = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);

        if (value is string str)
        {
            _logger.LogDebug("Save raw <string> to {Path}", filePath);

            using var sw = new StreamWriter(fs);
            await sw.WriteAsync(str);
            return;
        }

        if (value is byte[] bytes)
        {
            _logger.LogDebug("Save raw <byte[]> to {Path}", filePath);

            using var ms = new MemoryStream(bytes);
            await ms.CopyToAsync(fs, cancellationToken);
            return;
        }

        if (value is Stream stream)
        {
            _logger.LogDebug("Save raw <stream> to {Path}", filePath);

            await stream.CopyToAsync(fs, cancellationToken);
            return;
        }

        throw new ArgumentException("Unsupported value type; must be one of:  string, byte[], Stream",
                nameof(value));
    }

    public Task RemoveAsync(string keyFormat, CancellationToken cancellationToken = default, params IEnumerable<object> keyParams)
    {
        var fileName = string.Format(keyFormat, keyParams.ToArray());
        var filePath = Path.Combine(DirectoryPath!, $"{fileName}");

        if (File.Exists(filePath))
        {
            File.Delete(filePath);

            _logger.LogDebug("Removed from {Path}", filePath);
        }

        return Task.CompletedTask;
    }

    public Task DeleteOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        var files = Directory.GetFiles(DirectoryPath, $"04_Order_{orderId}_*");

        foreach (var file in files)
        {
            File.Delete(file);
        }

        return Task.CompletedTask;
    }
}
#endregion
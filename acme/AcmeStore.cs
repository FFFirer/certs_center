using System;
using System.Text.Json;
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

    public FileSystemAcmeStore(
        IOptions<FileSystemAcmeStoreOptions> options,
        ILogger<FileSystemAcmeStore> logger)
    {
        _options = options;
        _logger = logger;
    }


    private string DirectoryPath => Path.Combine(AppContext.BaseDirectory, _options.Value.Directory);

    private void CheckDirectory()
    {
        if (string.IsNullOrWhiteSpace(DirectoryPath))
        {
            throw new ArgumentNullException("FileSystemAcmeStoreOptions.Directory");
        }

        if (Directory.Exists(DirectoryPath))
        {
            return;
        }

        Directory.CreateDirectory(DirectoryPath);
    }

    public async Task<T?> LoadAsync<T>(string keyFormat, CancellationToken cancellationToken = default, params IEnumerable<object> keyParams)
    {
        CheckDirectory();

        var fileName = string.Format(keyFormat, keyParams.ToArray());
        var filePath = Path.Combine(DirectoryPath!, $"{fileName}");

        if (File.Exists(filePath) == false)
        {
            return default(T);
        }

        using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: cancellationToken);
    }

    public async Task SaveAsync<T>(T value, string keyFormat, CancellationToken cancellationToken = default, params IEnumerable<object> keyParams)
    {
        CheckDirectory();

        var fileName = string.Format(keyFormat, keyParams.ToArray());
        var filePath = Path.Combine(DirectoryPath!, $"{fileName}");

        using var fs = File.Exists(filePath)
            ? new FileStream(filePath, FileMode.Truncate, FileAccess.Write)
            : new FileStream(filePath, FileMode.Create, FileAccess.Write);

        await JsonSerializer.SerializeAsync(fs, value, cancellationToken: cancellationToken);
    }

    public Task<bool> ExistsAsync(string keyFormat, CancellationToken cancellationToken = default, params IEnumerable<object> keyParams)
    {
        var fileName = string.Format(keyFormat, keyParams.ToArray());
        var filePath = Path.Combine(DirectoryPath!, $"{fileName}");

        var exists = File.Exists(filePath);

        return Task.FromResult(exists);
    }

    public async Task<T?> LoadRawAsync<T>(string keyFormat, CancellationToken cancellationToken = default, params IEnumerable<object> keyParams)
    {
        CheckDirectory();

        var fileName = string.Format(keyFormat, keyParams.ToArray());
        var filePath = Path.Combine(DirectoryPath!, $"{fileName}");

        if (File.Exists(filePath) == false)
        {
            return default(T?);
        }

        if (typeof(T) == typeof(string))
            return (T)(object)(await File.ReadAllTextAsync(filePath, cancellationToken));
        else if (typeof(T) == typeof(byte[]))
            return (T)(object)(await File.ReadAllBytesAsync(filePath, cancellationToken));
        else if (typeof(T) == typeof(Stream) || typeof(T) == typeof(FileStream))
            return (T)(object)(new FileStream(filePath, FileMode.Open));
        else
            throw new ArgumentException("Unsupported return type; must be one of:  string, byte[], Stream",
                    nameof(T));
    }

    public async Task SaveRawAsync<T>(T value, string keyFormat, CancellationToken cancellationToken = default, params IEnumerable<object> keyParams)
    {
        CheckDirectory();

        var fileName = string.Format(keyFormat, keyParams.ToArray());
        var filePath = Path.Combine(DirectoryPath!, $"{fileName}");

        switch (value)
        {
            case string s:
                await File.WriteAllTextAsync(filePath, s, cancellationToken);
                break;
            case byte[] b:
                await File.WriteAllBytesAsync(filePath, b, cancellationToken);
                break;
            case Stream m:
                using (var fs = new FileStream(filePath, FileMode.Create))
                    m.CopyTo(fs);
                break;
            default:
                throw new ArgumentException("Unsupported value type; must be one of:  string, byte[], Stream",
                        nameof(value));
        }
    }

    public Task RemoveAsync(string keyFormat, CancellationToken cancellationToken = default, params IEnumerable<object> keyParams)
    {
        var fileName = string.Format(keyFormat, keyParams.ToArray());
        var filePath = Path.Combine(DirectoryPath!, $"{fileName}");

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
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
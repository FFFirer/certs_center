using System;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CertsServer.Acme;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAcmeDefaults(this IServiceCollection services)
    {
        return services
            .AddAcme()
            .AddAlibabaCloudDnsChallengeProvider();
    }

    public static IServiceCollection ConfigureOptionsFromConfiguration<TOptions>(
        this IServiceCollection services,
        string sectionPath)
        where TOptions : class
    {
        services
            .AddOptions<TOptions>()
            .Configure((TOptions options, IServiceProvider sp) =>
            {
                sp.GetService<IConfiguration>()?.GetSection(sectionPath)?.Bind(options);

            });

        return services;
    }

    public static IServiceCollection AddAcme(this IServiceCollection services)
    {
        services.AddSingleton<AcmeClientFactory>();
        services.AddScoped<AcmeCertificateFactory>();

        services.ConfigureOptionsFromConfiguration<AcmeOptions>("Acme");
        services.ConfigureOptionsFromConfiguration<FileSystemAcmeStoreOptions>("AcmeStore");

        services.AddSingleton<IAcmeStore, FileSystemAcmeStore>();
        services.AddSingleton<ICertificateStore, X509StoreCertificateStore>();

        // acmestate
        services.AddScoped<AcmeStateMachineContext>();
        services.AddTransient<CheckForRenewal>();
        services.AddTransient<BeginCertificateCreation>();
        services.AddTransient<TerminalState>();

        // http client
        services.AddHttpClient(CaConst.LetsEncrypt.Name, http =>
        {
            http.BaseAddress = new Uri(CaConst.LetsEncrypt.Endpoint);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            Proxy = null,
            UseProxy = false,
            ServerCertificateCustomValidationCallback = (a, b, c, d) => true
        })
        ;
        services.AddHttpClient(CaConst.LetsEncryptStaging.Name, http =>
        {
            http.BaseAddress = new Uri(CaConst.LetsEncryptStaging.Endpoint);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            Proxy = null,
            UseProxy = false,
            ServerCertificateCustomValidationCallback = (a, b, c, d) => true
        })
        ;

        return services;
    }
}

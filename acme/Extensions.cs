using System;
using Microsoft.Extensions.DependencyInjection;

namespace CertsServer.Acme;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAcmeDefaults(this IServiceCollection services)
    {
        return services
            .AddAcme()
            .AddAlibabaCloudDnsChallengeProvider();
    }

    public static IServiceCollection AddAcme(this IServiceCollection services)
    {
        services.AddScoped<AcmeClientFactory>();
        services.AddScoped<AcmeCertificateFactory>();

        services.AddOptions<AcmeOptions>("Acme");
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
        });
        services.AddHttpClient(CaConst.LetsEncryptStaging.Name, http =>
        {
            http.BaseAddress = new Uri(CaConst.LetsEncryptStaging.Endpoint);
        });

        return services;
    }
}

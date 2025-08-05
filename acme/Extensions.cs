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


    public static IServiceCollection AddOptionsBySectionPath<T>(this IServiceCollection services, string path)
        where T : class
    {
        return services.AddTransient<IConfigureOptions<T>>(sp => new ConfigureOptions<T>(options => sp.GetRequiredService<IConfiguration>().GetSection(path)?.Bind(options)));
    }

    public static IServiceCollection AddAcme(this IServiceCollection services)
    {
        services.AddSingleton<AcmeClientFactory>();
        services.AddScoped<AcmeCertificateFactory>();

        services.AddOptionsBySectionPath<AcmeOptions>("Acme");

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

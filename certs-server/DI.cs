using LettuceEncrypt;

namespace CertsServer;

public static class DI
{
    public static IServiceCollection AddStore(this IServiceCollection services, string? pfxPassword = "")
    {
        services.AddSingleton<CertificateStore>(sp => new CertificateStore(sp, pfxPassword));

        services.AddSingleton<ICertificateRepository>(sp => sp.GetRequiredService<CertificateStore>());
        services.AddSingleton<ICertificateSource>(sp => sp.GetRequiredService<CertificateStore>());
        services.AddSingleton<ICertificateStore>(sp => sp.GetRequiredService<CertificateStore>());

        return services;
    }
}

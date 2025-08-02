using CertsServer.Acme;

using LettuceEncrypt;

namespace CertsServer;

public static class DI
{
    public static IServiceCollection AddStore(this IServiceCollection services, string? pfxPassword = "")
    {
        
        return services;
    }
}

using System;
using System.Security.Cryptography.X509Certificates;

using AutoFixture;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Serilog;

namespace CertsServer.Acme.Tests;

public class SignFlowUnitTests
{
    public static IServiceCollection ConfigureAcmeServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.ClearProviders().AddConsole();
        });
        services.Configure<AlibabaCloud.OpenApiClient.Models.Config>(options =>
        {
            configuration.GetSection("AlibabaCloud")?.Bind(options);
        });
        services.AddAcmeDefaults();
        services.AddOptions<AcmeOptions>().Configure(o =>
        {
            o.Email = ["0sdf0@sina.cn"];
            o.CaName = CaConst.LetsEncryptStaging.Name;
            o.AcceptTermOfService = true;
        });

        return services;
    }

    public static IServiceProvider BuildServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets("5be23d08-54c4-488c-acff-cca75c11ef03").Build();

        var services = new ServiceCollection();

        ConfigureAcmeServices(services, configuration);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Sign_Except_Success()
    {
        var serviceProvider = BuildServiceProvider();

        using var scope = serviceProvider.CreateAsyncScope();

        var clientFactory = scope.ServiceProvider.GetRequiredService<AcmeClientFactory>();
        var account = await clientFactory.GetOrCreateAccountAsync(default);

        var certFactory = scope.ServiceProvider.GetRequiredService<AcmeCertificateFactory>();

        var id = Guid.Empty;

        var request = new CertificateRequest(0, id, ["test.fffirer.top"]);
        var order = await certFactory.GetOrCreateOrderAsync(request, null, true, default);
        var cert = await certFactory.CreateCertificateAsync(request, order, default);

        // workaround for https://github.com/dotnet/aspnetcore/issues/21183
        using var chain = new X509Chain
        {
            ChainPolicy =
                {
                    RevocationMode = X509RevocationMode.NoCheck
                }
        };

        Assert.NotNull(cert);
        Assert.True(chain.Build(cert));
    }
}

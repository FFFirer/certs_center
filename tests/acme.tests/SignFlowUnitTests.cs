using System;

using AutoFixture;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Serilog;

namespace CertsServer.Acme.Tests;

public class SignFlowUnitTests
{
    private IServiceProvider BuildServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets("5be23d08-54c4-488c-acff-cca75c11ef03").Build();

        var services = new ServiceCollection();

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

        var cert = await certFactory.CreateCertificateAsync(new CertificateRequest(id, ["test.fffirer.top"]), default);

        Assert.NotNull(cert);
    }
}

using System;

using Microsoft.Extensions.Configuration;

namespace CertsServer.Cli;

public static class ConfigurationBuilderExtensions
{
    public static string CurrentEnvironment()
    {
        var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        if (string.IsNullOrWhiteSpace(env))
        {
            env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        }
        if (string.IsNullOrWhiteSpace(env))
        {
            env = "Development";
        }

        return env;
    }

    public static IConfigurationBuilder AddCertsServer(this IConfigurationBuilder builder, string env = "Development")
    {
        return builder
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables("CERTS_")
            .AddUserSecrets("5be23d08-54c4-488c-acff-cca75c11ef03");
    }
}

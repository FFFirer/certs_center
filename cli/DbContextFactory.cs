using System;

using Microsoft.Extensions.Configuration;

namespace CertsServer.Cli;

public static class DbContextFactory
{
    public static CertsServerDbContext Create(TextWriter writer, IConfiguration? configuration = default)
    {
        var env = ConfigurationBuilderExtensions.CurrentEnvironment();
        writer.WriteLine("Environment: {0}", env);

        configuration ??= new ConfigurationBuilder().AddCertsServer(env).Build();

        var connectionString = configuration.GetCertsServerConnectionString();
        writer.WriteLine("Using connection string: {0}", connectionString);

        var dbContextFactory = new CertDbContextDesignTimeFactory();
        var dbContext = dbContextFactory.CreateDbContext([$@"--ConnectionStrings:Default={connectionString}"]);
        return dbContext;
    }
}

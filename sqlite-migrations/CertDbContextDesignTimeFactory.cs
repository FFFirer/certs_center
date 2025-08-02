using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace CertsServer;

public class CertDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CertsServerDbContext>
{
    public CertsServerDbContext CreateDbContext(string[] args)
    {
        Console.WriteLine("Args: {0}", string.Join(";", args));

        var configuration = new ConfigurationBuilder()
            .AddCommandLine(args)
            .Build();

        var connectionName = configuration.GetValue("DefaultConnectionName", "Default");
        var connectionString = configuration.GetConnectionString(connectionName);

        Console.WriteLine("Target Connection: {0}={1}", connectionName, connectionString);

        var optionsBuilder = new DbContextOptionsBuilder<CertsServerDbContext>();
        optionsBuilder.UseSqlite(connectionString, sqlite =>
        {
            sqlite.MigrationsAssembly("CertsServer.Migrations.Sqlite");
        });

        return new CertsServerDbContext(optionsBuilder.Options);
    }
}

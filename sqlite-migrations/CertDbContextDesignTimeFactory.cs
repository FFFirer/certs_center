using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CertsServer;

public class CertDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CertDbContext>
{
    public CertDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CertDbContext>();
        optionsBuilder.UseSqlite("Data Source=certs.db", sqlite =>
        {
            sqlite.MigrationsAssembly("CertsServer.Migrations.Sqlite");
        });

        return new CertDbContext(optionsBuilder.Options);
    }
}

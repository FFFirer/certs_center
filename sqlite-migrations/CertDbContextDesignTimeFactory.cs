using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CertsServer;

public class CertDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CertsServerDbContext>
{
    public CertsServerDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CertsServerDbContext>();
        optionsBuilder.UseSqlite("Data Source=certs.db", sqlite =>
        {
            sqlite.MigrationsAssembly("CertsServer.Migrations.Sqlite");
        });

        return new CertsServerDbContext(optionsBuilder.Options);
    }
}

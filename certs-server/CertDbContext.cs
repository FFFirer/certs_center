using Microsoft.EntityFrameworkCore;

namespace CertsServer;

public class CertDbContext : DbContext
{
    public CertDbContext(DbContextOptions<CertDbContext> options) : base(options)
    {

    }

    public DbSet<DomainName> DomainNames => Set<DomainName>();
    public DbSet<CertificateFile> CertificateFiles => Set<CertificateFile>();
    public DbSet<DomainNameAction> DomainNameActions => Set<DomainNameAction>();


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new DomainNameEntityTypeConfiguration());
        modelBuilder.ApplyConfiguration(new CertificateFileEntityTypeConfiguration());
    }
}

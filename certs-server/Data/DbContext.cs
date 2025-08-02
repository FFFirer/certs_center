using CertsServer.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CertsServer;


public class ModelCreatingOptions<TDbContext> where TDbContext : DbContext
{
    public Action<ModelBuilder>? ModelCreatingAction { get; set; }
}

public class CertsServerDbContext : DbContext
{
    private readonly ModelCreatingOptions<CertsServerDbContext>? _modelCreating;

    public CertsServerDbContext(
        DbContextOptions<CertsServerDbContext> options, 
        ModelCreatingOptions<CertsServerDbContext>? modelCreating = default) : base(options)
    {
        _modelCreating = modelCreating;
    }

    public DbSet<TicketEntity> Tickets => Set<TicketEntity>();
    public DbSet<TicketCertificateEntity> TicketCertificates => Set<TicketCertificateEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        _modelCreating?.ModelCreatingAction?.Invoke(modelBuilder);
    }
}

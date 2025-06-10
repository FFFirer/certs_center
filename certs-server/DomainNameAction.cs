using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Runtime.CompilerServices;

namespace CertsServer;

public class DomainNameAction(string name, string action)
{
    public long Id { get; set; } = 0;
    public string Name { get; set; } = name;
    public string Action { get; set; } = action;
    public string Message { get; set; } = string.Empty;

    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
}


public class DomainNameActionEntityTypeConfiguration : IEntityTypeConfiguration<DomainNameAction>
{
    public void Configure(EntityTypeBuilder<DomainNameAction> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired();
        builder.Property(x => x.Action).IsRequired();
        builder.Property(x => x.Created).HasConversion(new LocalDateTimeOffsetValueConverter());
    }
}
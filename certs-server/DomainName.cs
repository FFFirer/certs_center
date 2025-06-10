using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CertsServer;

public class DomainName(string id)
{
    public string Id { get; set; } = id;
    public Guid? CertificateFileId { get; set; }

    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastModified { get; set; } = DateTimeOffset.UtcNow;
}

public class DomainNameEntityTypeConfiguration : IEntityTypeConfiguration<DomainName>
{
    public void Configure(EntityTypeBuilder<DomainName> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Created).HasConversion(new LocalDateTimeOffsetValueConverter());
        builder.Property(x => x.LastModified).HasConversion(new LocalDateTimeOffsetValueConverter());
    }
}

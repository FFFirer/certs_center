using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CertsServer;

public class CertificateFile(byte[] data, Guid id)
{
    public Guid Id { get; set; } = id;
    public string? Path { get; set; }
    public DateTimeOffset Created { get; set; }
    public byte[] Data { get; set; } = data;
    public DateTimeOffset? NotAfter { get; set; }
    public DateTimeOffset? NotBefore { get; set; }
    public string? Password { get; set; } 
}

public class CertificateFileEntityTypeConfiguration : IEntityTypeConfiguration<CertificateFile>
{
    public void Configure(EntityTypeBuilder<CertificateFile> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Path).IsRequired(false);
        builder.Property(x => x.Created).HasConversion(new LocalDateTimeOffsetValueConverter());
        builder.Property(x => x.NotBefore).HasConversion(new LocalDateTimeOffsetValueConverter());
        builder.Property(x => x.NotAfter).HasConversion(new LocalDateTimeOffsetValueConverter());
    }
}

using System;

using CertsServer.Data;

using Grpc.Core;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CertsServer.Data;

public enum TicketStatus
{
    Created,
    Processing,
    Finished,
    Failed,
}

public class TicketEntity
{
    public Guid Id { get; set; }
    public string[]? DomainNames { get; set; }
    public string? PfxPassword { get; set; }

    public TicketStatus Status { get; set; }

    public DateTimeOffset CreatedTime { get; set; }
    public DateTimeOffset UpdatedTime { get; set; }
    public string? Remark { get; set; }

    public ICollection<TicketCertificateEntity> Certificates { get; set; } = [];

    public void Finished(TicketCertificateEntity? certificate = default)
    {
        if (certificate is not null)
        {
            Certificates.Add(certificate);
        }

        Status = TicketStatus.Finished;
        Remark = string.Empty;
        UpdatedTime = DateTimeOffset.UtcNow;
    }

    public void Failed(string? reason)
    {
        Status = TicketStatus.Failed;
        Remark = reason;
        UpdatedTime = DateTimeOffset.UtcNow;
    }

    public void Processing()
    {
        Status = TicketStatus.Processing;
        Remark = string.Empty;
        UpdatedTime = DateTimeOffset.UtcNow;
    }
}

public class TicketCertificateEntity
{
    public TicketCertificateEntity(
        string path,
        Guid? ticketId = default,
        TicketCertificateStatus status = default,
        DateTime? notBefore = default,
        DateTime? notAfter = default)
    {
        Id = Guid.NewGuid();
        TicketId = ticketId;
        CreatedTime = DateTimeOffset.UtcNow;
        NotBefore = notBefore;
        NotAfter = notAfter;
        Path = path;
        Status = status;
    }

    public Guid Id { get; set; }
    public Guid? TicketId { get; set; }
    public DateTimeOffset CreatedTime { get; set; }
    public DateTime? NotBefore { get; set; }
    public DateTime? NotAfter { get; set; }
    public string Path { get; set; }
    public TicketCertificateStatus Status { get; set; }
}

public enum TicketCertificateStatus
{
    Active,
    Expired,
}

public static class ModelCreatingExtensions
{
    public static ModelBuilder ApplySqliteConfigurations(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SqliteTicketCertificateEntityConfiguration());
        modelBuilder.ApplyConfiguration(new SqliteTicketEntityConfiguration());

        return modelBuilder;
    }
}


public class SqliteTicketEntityConfiguration : IEntityTypeConfiguration<TicketEntity>
{
    public void Configure(EntityTypeBuilder<TicketEntity> builder)
    {
        builder.ToTable("tickets");

        builder.HasKey(e => e.Id);
        builder.Property(x => x.Status).HasConversion<string>();
        builder.Property(x => x.CreatedTime).HasConversion(SqliteDateTimeOffsetValueConverter.Instance);
        builder.Property(x => x.UpdatedTime).HasConversion(SqliteDateTimeOffsetValueConverter.Instance);
        builder.Property(x => x.DomainNames).HasConversion(ObjectValueConverter<string[]>.Singleton);
    }
}

public class SqliteTicketCertificateEntityConfiguration : IEntityTypeConfiguration<TicketCertificateEntity>
{
    public void Configure(EntityTypeBuilder<TicketCertificateEntity> builder)
    {
        builder.ToTable("ticket_certificates");

        builder.HasKey(e => e.Id);
        builder.Property(x => x.CreatedTime).HasConversion(SqliteDateTimeOffsetValueConverter.Instance);
        builder.Property(e => e.NotBefore).HasConversion(SqliteDateTimeValueConverter.Instance);
        builder.Property(e => e.NotAfter).HasConversion(SqliteDateTimeValueConverter.Instance);
    }
}
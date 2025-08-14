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
    Deleted,
}

public class TicketEntity
{
    public TicketEntity()
    {
        Id = Guid.NewGuid();
        CreatedTime = DateTimeOffset.Now;
    }

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
        long ticketOrderId,
        Guid? ticketId = default,
        TicketCertificateStatus status = default,
        DateTime? notBefore = default,
        DateTime? notAfter = default)
    {
        Id = Guid.NewGuid();
        TicketId = ticketId;
        TicketOrderId = ticketOrderId;
        CreatedTime = DateTimeOffset.UtcNow;
        NotBefore = notBefore;
        NotAfter = notAfter;
        Path = path;
        Status = status;
    }

    public Guid Id { get; set; }
    public Guid? TicketId { get; set; }
    public long TicketOrderId { get; set; }
    public DateTimeOffset CreatedTime { get; set; }
    public DateTime? NotBefore { get; set; }
    public DateTime? NotAfter { get; set; }
    public string Path { get; set; }
    public TicketCertificateStatus Status { get; set; }
    public string? AcmeOrderUrl { get; set; }
}

public class TicketOrderEntity
{
    public TicketOrderEntity(string orderUrl, Guid ticketId)
    {
        OrderUrl = orderUrl;
        TicketId = ticketId;
        CreatedTime = DateTimeOffset.UtcNow;
        LastUpdatedTime = DateTimeOffset.UtcNow;
    }

    public long Id { get; set; }

    public string OrderUrl { get; set; }
    public Guid TicketId { get; set; }

    public DateTimeOffset CreatedTime { get; set; }

    public DateTimeOffset? LastUpdatedTime { get; set; }

    public TicketCertificateEntity? Certificate { get; set; }

    public bool Deleted { get; set; } = false;

    internal void Delete()
    {
        Deleted = true;
        LastUpdatedTime = DateTimeOffset.UtcNow;
    }
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
        // modelBuilder.ApplyConfiguration(new SqliteTicketCertificateEntityConfiguration());
        modelBuilder.ApplyConfiguration(new SqliteTicketEntityConfiguration());
        modelBuilder.ApplyConfiguration(new SqliteTicketOrderEntityConfiguration());

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

public class SqliteTicketOrderEntityConfiguration : IEntityTypeConfiguration<TicketOrderEntity>
{
    public void Configure(EntityTypeBuilder<TicketOrderEntity> builder)
    {
        builder.ToTable("ticket_orders");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CreatedTime).HasConversion(SqliteDateTimeOffsetValueConverter.Instance);
        builder.Property(x => x.LastUpdatedTime).HasConversion(SqliteDateTimeOffsetValueConverter.Instance);

        builder.OwnsOne(x => x.Certificate, o => o.ToJson());
    }
}
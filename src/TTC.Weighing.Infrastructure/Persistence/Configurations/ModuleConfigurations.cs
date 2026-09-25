using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Infrastructure.Persistence.Converters;

namespace TTC.Weighing.Infrastructure.Persistence.Configurations;

public class ExternalDocumentConfiguration : IEntityTypeConfiguration<ExternalDocument>
{
    public void Configure(EntityTypeBuilder<ExternalDocument> b)
    {
        b.ToTable("external_documents");
        b.HasKey(x => x.Id);

        b.Property(x => x.SourceSystem).HasMaxLength(20).IsRequired();
        b.Property(x => x.DocumentType).HasMaxLength(30).IsRequired();
        b.Property(x => x.ReferenceNo).HasMaxLength(100).IsRequired();
        b.Property(x => x.ShipmentNo).HasMaxLength(100);
        b.Property(x => x.OrganizationCode).HasMaxLength(50);
        b.Property(x => x.BusinessUnit).HasMaxLength(50);
        b.Property(x => x.SourceVersion).HasMaxLength(50);
        b.Property(x => x.SourceHash).HasMaxLength(128);
        b.Property(x => x.Status).HasMaxLength(50);
        b.Property(x => x.RawSnapshotRef).HasMaxLength(300);

        b.HasIndex(x => new { x.SourceSystem, x.DocumentType, x.ReferenceNo });

        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.DocumentId);
    }
}

public class TripDocumentLineConfiguration : IEntityTypeConfiguration<TripDocumentLine>
{
    public void Configure(EntityTypeBuilder<TripDocumentLine> b)
    {
        b.ToTable("trip_document_lines");
        b.HasKey(x => x.Id);

        b.Property(x => x.SourceLineId).HasMaxLength(100);
        b.Property(x => x.ItemCode).HasMaxLength(100).IsRequired();
        b.Property(x => x.PlannedUom).HasMaxLength(20);
        b.Property(x => x.ConfirmedUom).HasMaxLength(20);

        b.Property(x => x.PlannedQuantity).HasPrecision(18, 4);
        b.Property(x => x.ConfirmedQuantity).HasPrecision(18, 4);
        b.Property(x => x.RemainingQuantity).HasPrecision(18, 4);

        b.HasIndex(x => new { x.TripId, x.MovementSegmentId });
    }
}

public class BomSnapshotConfiguration : IEntityTypeConfiguration<BomSnapshot>
{
    public void Configure(EntityTypeBuilder<BomSnapshot> b)
    {
        b.ToTable("bom_snapshots");
        b.HasKey(x => x.Id);

        b.Property(x => x.SourceSystem).HasMaxLength(20).IsRequired();
        b.Property(x => x.ItemCode).HasMaxLength(100).IsRequired();
        b.Property(x => x.Uom).HasMaxLength(20).IsRequired();
        b.Property(x => x.SourceVersion).HasMaxLength(50);
        b.Property(x => x.SnapshotHash).HasMaxLength(128);
        b.Property(x => x.MassBasis).HasConversion<string>().HasMaxLength(30);

        b.Property(x => x.NominalKgPerUnit).HasPrecision(15, 3);
        b.Property(x => x.MinKgPerUnit).HasPrecision(15, 3);
        b.Property(x => x.MaxKgPerUnit).HasPrecision(15, 3);
        b.Property(x => x.PackagingTareKg).HasPrecision(15, 3);
        b.Property(x => x.PalletTareKg).HasPrecision(15, 3);

        b.HasIndex(x => new { x.SourceSystem, x.ItemCode, x.EffectiveAt });
    }
}

public class QualitySampleConfiguration : IEntityTypeConfiguration<QualitySample>
{
    public void Configure(EntityTypeBuilder<QualitySample> b)
    {
        b.ToTable("quality_samples");
        b.HasKey(x => x.Id);

        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Source).HasMaxLength(20).IsRequired();
        b.Property(x => x.SampleQrRef).HasMaxLength(100);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.ApprovalRef).HasMaxLength(100);

        b.Property(x => x.SampleBeforeKg).HasPrecision(15, 3);
        b.Property(x => x.SampleAfterKg).HasPrecision(15, 3);
        b.Property(x => x.ImpurityPercent).HasPrecision(9, 4);
        b.Property(x => x.CcsValue).HasPrecision(9, 4);

        b.HasOne<QualitySample>().WithMany().HasForeignKey(x => x.SupersedesId);

        // Mỗi loại mẫu chỉ có một bản đang hiệu lực cho một chuyến.
        b.HasIndex(x => new { x.TripId, x.Type, x.IsEffective })
            .HasFilter("is_effective = true")
            .IsUnique()
            .HasDatabaseName("uq_quality_samples_trip_type_effective");
    }
}

public class MeasurementSessionConfiguration : IEntityTypeConfiguration<MeasurementSession>
{
    public void Configure(EntityTypeBuilder<MeasurementSession> b)
    {
        b.ToTable("measurement_sessions");
        b.HasKey(x => x.Id);

        b.Property(x => x.ItemCode).HasMaxLength(100).IsRequired();

        b.Property(x => x.DeviceIds)
            .HasColumnType("jsonb")
            .HasConversion(new JsonListConverter<Guid>(), new JsonListComparer<Guid>());

        b.Property(x => x.MassBasis).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.ReadingMode).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.IotMassOrigin).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.CounterEpoch).HasMaxLength(100);
        b.Property(x => x.HoldReason).HasMaxLength(500);

        b.Property(x => x.CounterStart).HasPrecision(18, 4);
        b.Property(x => x.CounterEnd).HasPrecision(18, 4);
        b.Property(x => x.IotMassKg).HasPrecision(15, 3);

        b.HasIndex(x => new { x.TripId, x.Status });
        b.HasIndex(x => new { x.WarehouseId, x.Status });
    }
}

public class DeviceEventConfiguration : IEntityTypeConfiguration<DeviceEvent>
{
    public void Configure(EntityTypeBuilder<DeviceEvent> b)
    {
        b.ToTable("device_events");
        b.HasKey(x => x.Id);

        b.Property(x => x.EventId).HasMaxLength(100).IsRequired();
        b.Property(x => x.BootSessionId).HasMaxLength(100).IsRequired();
        b.Property(x => x.EventType).HasMaxLength(40).IsRequired();
        b.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
        b.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.PayloadHash).HasMaxLength(128).IsRequired();
        b.Property(x => x.ProcessingStatus).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.ProcessingNote).HasMaxLength(500);

        // B-013: khóa chống trùng tối thiểu.
        b.HasIndex(x => new { x.DeviceId, x.BootSessionId, x.Sequence })
            .IsUnique()
            .HasDatabaseName("uq_device_events_dedup");

        b.HasIndex(x => new { x.MeasurementSessionId, x.Sequence });
        b.HasIndex(x => x.ProcessingStatus);
    }
}

public class ReconciliationConfiguration : IEntityTypeConfiguration<Reconciliation>
{
    public void Configure(EntityTypeBuilder<Reconciliation> b)
    {
        b.ToTable("reconciliations");
        b.HasKey(x => x.Id);

        b.Property(x => x.InputVersionsJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.ResolvedBy).HasMaxLength(100);

        b.HasIndex(x => new { x.TripId, x.IsEffective })
            .HasFilter("is_effective = true")
            .IsUnique()
            .HasDatabaseName("uq_reconciliations_trip_effective");

        b.HasMany(x => x.Checks).WithOne().HasForeignKey(x => x.ReconciliationId);

        // Thuộc tính suy diễn, không phải navigation.
        b.Ignore(x => x.Blockers);
        b.Ignore(x => x.IsPassed);
    }
}

public class ReconciliationCheckConfiguration : IEntityTypeConfiguration<ReconciliationCheck>
{
    public void Configure(EntityTypeBuilder<ReconciliationCheck> b)
    {
        b.ToTable("reconciliation_checks");
        b.HasKey(x => x.Id);

        b.Property(x => x.CheckType).HasConversion<string>().HasMaxLength(50);
        b.Property(x => x.Verdict).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Unit).HasMaxLength(20).IsRequired();
        b.Property(x => x.TolerancePolicyId).HasMaxLength(100);
        b.Property(x => x.Detail).HasMaxLength(1000);

        b.Property(x => x.ObservedValue).HasPrecision(18, 4);
        b.Property(x => x.ExpectedValue).HasPrecision(18, 4);
        b.Property(x => x.LowerBound).HasPrecision(18, 4);
        b.Property(x => x.UpperBound).HasPrecision(18, 4);
        b.Property(x => x.Difference).HasPrecision(18, 4);

        b.HasIndex(x => new { x.ReconciliationId, x.CheckType });
    }
}

public class AllocationConfiguration : IEntityTypeConfiguration<Allocation>
{
    public void Configure(EntityTypeBuilder<Allocation> b)
    {
        b.ToTable("allocations");
        b.HasKey(x => x.Id);

        b.Property(x => x.OwnerRef).HasMaxLength(100);
        b.Property(x => x.Method).HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.ApprovalRef).HasMaxLength(100);

        b.Property(x => x.AllocatedKg).HasPrecision(15, 3);
        b.Property(x => x.Ratio).HasPrecision(12, 8);
        b.Property(x => x.RoundingAdjustmentKg).HasPrecision(15, 3);

        b.HasIndex(x => x.TripId);
    }
}

public class IntegrationMessageConfiguration : IEntityTypeConfiguration<IntegrationMessage>
{
    public void Configure(EntityTypeBuilder<IntegrationMessage> b)
    {
        b.ToTable("integration_messages");
        b.HasKey(x => x.Id);

        b.Property(x => x.Destination).HasMaxLength(20).IsRequired();
        b.Property(x => x.Operation).HasMaxLength(100).IsRequired();
        b.Property(x => x.BusinessKey).HasMaxLength(300).IsRequired();
        b.Property(x => x.ShipmentNo).HasMaxLength(100);
        b.Property(x => x.PayloadRef).HasMaxLength(300).IsRequired();
        b.Property(x => x.PayloadHash).HasMaxLength(128).IsRequired();
        b.Property(x => x.InterfaceReference).HasMaxLength(100);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.MaskedResponse).HasMaxLength(4000);
        b.Property(x => x.BusinessErrorCode).HasMaxLength(100);
        b.Property(x => x.BusinessErrorMessage).HasMaxLength(1000);
        b.Property(x => x.ExternalDocumentRef).HasMaxLength(200);

        // Chống lặp ở tầng DB: một business_key chỉ có một thông điệp.
        b.HasIndex(x => x.BusinessKey)
            .IsUnique()
            .HasDatabaseName("uq_integration_messages_business_key");

        b.HasIndex(x => new { x.TripId, x.Status });
        b.HasIndex(x => x.Status);

        b.HasOne<IntegrationMessage>().WithMany().HasForeignKey(x => x.SupersededById);
    }
}

public class GateDecisionConfiguration : IEntityTypeConfiguration<GateDecision>
{
    public void Configure(EntityTypeBuilder<GateDecision> b)
    {
        b.ToTable("gate_decisions");
        b.HasKey(x => x.Id);

        b.Property(x => x.Direction).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.IdentityEvidence).HasMaxLength(200);
        b.Property(x => x.IdentityVerifiedBy).HasMaxLength(100);
        b.Property(x => x.ConditionsJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.DenyReason).HasMaxLength(1000);

        b.HasIndex(x => new { x.TripId, x.DecidedAt });
    }
}

public class GateCommandConfiguration : IEntityTypeConfiguration<GateCommand>
{
    public void Configure(EntityTypeBuilder<GateCommand> b)
    {
        b.ToTable("gate_commands");
        b.HasKey(x => x.Id);

        b.Property(x => x.CommandId).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.CommandId).IsUnique();

        b.Property(x => x.State).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ControllerFeedback).HasMaxLength(1000);
        b.Property(x => x.PassageEventRef).HasMaxLength(200);
        b.Property(x => x.PassageConfirmedBy).HasMaxLength(100);

        b.HasIndex(x => new { x.TripId, x.State });
    }
}

public class ApprovalConfiguration : IEntityTypeConfiguration<Approval>
{
    public void Configure(EntityTypeBuilder<Approval> b)
    {
        b.ToTable("approvals");
        b.HasKey(x => x.Id);

        b.Property(x => x.ActionType).HasMaxLength(50).IsRequired();
        b.Property(x => x.RequestedBy).HasMaxLength(100).IsRequired();
        b.Property(x => x.ApprovedBy).HasMaxLength(100);
        b.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        b.Property(x => x.DocumentRef).HasMaxLength(300);

        b.HasIndex(x => new { x.TripId, x.ActionType });
    }
}

public class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> b)
    {
        b.ToTable("audit_log_entries");
        b.HasKey(x => x.Id);

        b.Property(x => x.ActionType).HasMaxLength(60).IsRequired();
        b.Property(x => x.Actor).HasMaxLength(100).IsRequired();
        b.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
        b.Property(x => x.BeforeJson).HasColumnType("jsonb");
        b.Property(x => x.AfterJson).HasColumnType("jsonb");
        b.Property(x => x.Reason).HasMaxLength(1000);

        b.HasIndex(x => new { x.TripId, x.OccurredAt });
        b.HasIndex(x => x.ActionType);
    }
}

public class IncidentConfiguration : IEntityTypeConfiguration<Incident>
{
    public void Configure(EntityTypeBuilder<Incident> b)
    {
        b.ToTable("incidents");
        b.HasKey(x => x.Id);

        b.Property(x => x.IssueCode).HasMaxLength(60).IsRequired();
        b.Property(x => x.Scope).HasMaxLength(30).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000).IsRequired();
        b.Property(x => x.OpenedBy).HasMaxLength(100).IsRequired();
        b.Property(x => x.ResolvedBy).HasMaxLength(100);

        b.HasIndex(x => new { x.SiteId, x.ResolvedAt });
        b.HasIndex(x => x.TripId);
    }
}

public class WeighTicketConfiguration : IEntityTypeConfiguration<WeighTicket>
{
    public void Configure(EntityTypeBuilder<WeighTicket> b)
    {
        b.ToTable("weigh_tickets");
        b.HasKey(x => x.Id);

        b.Property(x => x.TicketNumber).HasMaxLength(50).IsRequired();
        b.Property(x => x.TemplateCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.PrintedBy).HasMaxLength(100).IsRequired();
        b.Property(x => x.SnapshotJson).HasColumnType("jsonb").IsRequired();

        b.HasIndex(x => x.TripId).HasDatabaseName("idx_weigh_tickets_trip");
        b.HasIndex(x => x.TicketNumber)
            .HasFilter("is_reprint = false")
            .IsUnique()
            .HasDatabaseName("uq_weigh_tickets_number");
    }
}

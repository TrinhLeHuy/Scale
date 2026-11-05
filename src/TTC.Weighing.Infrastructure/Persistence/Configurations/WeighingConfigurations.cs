using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Infrastructure.Persistence.Converters;

namespace TTC.Weighing.Infrastructure.Persistence.Configurations;

public class TripConfiguration : IEntityTypeConfiguration<Trip>
{
    public void Configure(EntityTypeBuilder<Trip> b)
    {
        b.ToTable("trips");
        b.HasKey(x => x.Id);

        b.Property(x => x.TripNo).HasMaxLength(50).IsRequired();
        b.HasIndex(x => x.TripNo).IsUnique();

        // Mã nguồn ngoài lưu chuỗi để giữ tiền tố và số 0 đầu (03_Du_lieu mục 2).
        b.Property(x => x.ReferenceTripNo).HasMaxLength(100);
        b.Property(x => x.SourceSystem).HasMaxLength(20).IsRequired();
        b.Property(x => x.TractorPlate).HasMaxLength(30);
        b.Property(x => x.TrailerPlate).HasMaxLength(30);

        b.Property(x => x.Lifecycle).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.ExecutionStatus).HasConversion<string>().HasMaxLength(20);

        // --- Trường mới cho trip-registration-redesign ---
        b.Property(x => x.VehicleType).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.SugarcaneType).HasMaxLength(100);
        b.Property(x => x.SugarcaneVariety).HasMaxLength(100);
        b.Property(x => x.SugarcaneOwner).HasMaxLength(200);
        b.Property(x => x.HarvestMethod).HasMaxLength(100);
        b.Property(x => x.Station).HasMaxLength(100);
        b.Property(x => x.CropSeason).HasMaxLength(50);

        // --- Phiếu cân ---
        b.Property(x => x.TicketNumber).HasMaxLength(50);
        b.Property(x => x.DigiRegistrationId).HasMaxLength(100);

        // Biển số KHÔNG unique — một biển số có nhiều chuyến theo thời gian (R-003).
        b.HasIndex(x => new { x.SiteId, x.Lifecycle });
        b.HasIndex(x => x.TractorPlate);

        b.Property(x => x.Version).IsConcurrencyToken();

        b.HasMany(x => x.Checkpoints).WithOne().HasForeignKey(x => x.TripId);
        b.HasMany(x => x.WeighSessions).WithOne().HasForeignKey(x => x.TripId);
        b.HasMany(x => x.QrTokens).WithOne().HasForeignKey(x => x.TripId);
    }
}

public class WeighSessionConfiguration : IEntityTypeConfiguration<WeighSession>
{
    public void Configure(EntityTypeBuilder<WeighSession> b)
    {
        b.ToTable("weigh_sessions");
        b.HasKey(x => x.Id);

        b.Property(x => x.Checkpoint).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.OwnershipToken).HasMaxLength(64).IsRequired();

        // B-003: một bàn cân chỉ có một phiên Open tại một thời điểm.
        b.HasIndex(x => new { x.WeighbridgeId, x.Status })
            .HasFilter("status = 'Open'")
            .IsUnique()
            .HasDatabaseName("uq_weigh_sessions_bridge_open");

        // Một chuyến không chốt đồng thời cùng checkpoint/phân đoạn ở hai bàn.
        b.HasIndex(x => new { x.TripId, x.Checkpoint, x.SegmentIndex, x.Status })
            .HasFilter("status = 'Open'")
            .IsUnique()
            .HasDatabaseName("uq_weigh_sessions_trip_checkpoint_open");

        b.Property(x => x.Version).IsConcurrencyToken();

        b.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.WeighSessionId);
    }
}

public class WeighEventConfiguration : IEntityTypeConfiguration<WeighEvent>
{
    public void Configure(EntityTypeBuilder<WeighEvent> b)
    {
        b.ToTable("weigh_events");
        b.HasKey(x => x.Id);

        b.Property(x => x.RawWeight).HasPrecision(15, 3).IsRequired();
        b.Property(x => x.RawUom).HasMaxLength(5).IsRequired();
        b.Property(x => x.NormalizedKg).HasPrecision(15, 3).IsRequired();

        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Source).HasConversion<string>().HasMaxLength(10);

        b.Property(x => x.EvidenceRefs)
            .HasColumnType("jsonb")
            .HasConversion(new JsonListConverter<string>(), new JsonListComparer<string>());

        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.ApprovalRef).HasMaxLength(100);

        b.HasOne<WeighEvent>().WithMany().HasForeignKey(x => x.SupersedesId);

        // Mỗi vai trò/phân đoạn chỉ có một bản đang hiệu lực (03_Du_lieu mục 3).
        b.HasIndex(x => new { x.TripId, x.Role, x.IsEffective })
            .HasFilter("is_effective = true")
            .IsUnique()
            .HasDatabaseName("uq_weigh_events_trip_role_effective");

        b.HasIndex(x => x.MeasuredAt);
    }
}

public class QrTokenConfiguration : IEntityTypeConfiguration<QrToken>
{
    public void Configure(EntityTypeBuilder<QrToken> b)
    {
        b.ToTable("qr_tokens");
        b.HasKey(x => x.Id);

        b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();

        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);

        // Nội dung mã hóa QR (JSON Base64) — trip-registration-redesign.
        b.Property(x => x.EncodedContent).HasColumnType("text");
    }
}

public class QueueSequenceTrackerConfiguration : IEntityTypeConfiguration<QueueSequenceTracker>
{
    public void Configure(EntityTypeBuilder<QueueSequenceTracker> b)
    {
        b.ToTable("queue_sequence_tracker");

        b.HasKey(x => new { x.SiteId, x.VehicleType, x.OperatingDate });

        b.Property(x => x.VehicleType).HasMaxLength(20);
        b.Property(x => x.LastValue).HasDefaultValue(0);
    }
}

public class WorkflowProfileConfiguration : IEntityTypeConfiguration<WorkflowProfile>
{
    public void Configure(EntityTypeBuilder<WorkflowProfile> b)
    {
        b.ToTable("workflow_profiles");
        b.HasKey(x => x.Id);

        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.HasIndex(x => new { x.Code, x.ProfileVersion }).IsUnique();

        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.SourceSystem).HasMaxLength(20);
        b.Property(x => x.Direction).HasConversion<string>().HasMaxLength(20);

        b.Property(x => x.Checkpoints)
            .HasColumnType("jsonb")
            .HasConversion(new JsonListConverter<CheckpointType>(), new JsonListComparer<CheckpointType>());

        b.Property(x => x.WeightRoleSequence)
            .HasColumnType("jsonb")
            .HasConversion(new JsonListConverter<WeightRole>(), new JsonListComparer<WeightRole>());

        // Ngưỡng lưu jsonb; null = chưa duyệt, không tự điền giá trị.
        b.OwnsOne(x => x.Thresholds, t => t.ToJson());
    }
}

public class CheckpointEventConfiguration : IEntityTypeConfiguration<CheckpointEvent>
{
    public void Configure(EntityTypeBuilder<CheckpointEvent> b)
    {
        b.ToTable("checkpoint_events");
        b.HasKey(x => x.Id);

        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.WaiverReason).HasMaxLength(500);
        b.Property(x => x.WaiverApprovalRef).HasMaxLength(100);

        b.HasIndex(x => new { x.TripId, x.Type });
    }
}

public class SiteConfigurationMap : IEntityTypeConfiguration<Site>
{
    public void Configure(EntityTypeBuilder<Site> b)
    {
        b.ToTable("sites");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.TimeZoneId).HasMaxLength(60);
    }
}

public class WeighbridgeConfiguration : IEntityTypeConfiguration<Weighbridge>
{
    public void Configure(EntityTypeBuilder<Weighbridge> b)
    {
        b.ToTable("weighbridges");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.HasIndex(x => new { x.SiteId, x.Code }).IsUnique();

        b.Property(x => x.MinCapacityKg).HasPrecision(15, 3);
        b.Property(x => x.MaxCapacityKg).HasPrecision(15, 3);
        b.Property(x => x.FirstWeightMinKg).HasPrecision(15, 3);
        b.Property(x => x.FirstWeightMaxKg).HasPrecision(15, 3);
        b.Property(x => x.SecondWeightMinKg).HasPrecision(15, 3);
        b.Property(x => x.SecondWeightMaxKg).HasPrecision(15, 3);
        b.Property(x => x.ResolutionKg).HasPrecision(15, 3);
    }
}

public class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> b)
    {
        b.ToTable("devices");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.DeviceClass).HasMaxLength(30).IsRequired();
        b.Property(x => x.MappingVersion).HasMaxLength(30);
        b.HasIndex(x => new { x.SiteId, x.Code }).IsUnique();
        b.HasIndex(x => new { x.LaneId, x.DeviceClass });
    }
}

public class GateConfiguration : IEntityTypeConfiguration<Gate>
{
    public void Configure(EntityTypeBuilder<Gate> b)
    {
        b.ToTable("gates");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200);
    }
}

public class LaneConfiguration : IEntityTypeConfiguration<Lane>
{
    public void Configure(EntityTypeBuilder<Lane> b)
    {
        b.ToTable("lanes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.HasIndex(x => new { x.SiteId, x.Code }).IsUnique();
    }
}

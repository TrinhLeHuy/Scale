using Microsoft.EntityFrameworkCore;
using TTC.Weighing.Domain.Entities;

namespace TTC.Weighing.Infrastructure.Persistence;

public class WeighingDbContext(DbContextOptions<WeighingDbContext> options) : DbContext(options)
{
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Gate> Gates => Set<Gate>();
    public DbSet<Lane> Lanes => Set<Lane>();
    public DbSet<Weighbridge> Weighbridges => Set<Weighbridge>();
    public DbSet<Device> Devices => Set<Device>();

    public DbSet<WorkflowProfile> WorkflowProfiles => Set<WorkflowProfile>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<CheckpointEvent> CheckpointEvents => Set<CheckpointEvent>();
    public DbSet<QrToken> QrTokens => Set<QrToken>();
    public DbSet<WeighSession> WeighSessions => Set<WeighSession>();
    public DbSet<WeighEvent> WeighEvents => Set<WeighEvent>();

    public DbSet<ExternalDocument> ExternalDocuments => Set<ExternalDocument>();
    public DbSet<TripDocumentLine> TripDocumentLines => Set<TripDocumentLine>();
    public DbSet<BomSnapshot> BomSnapshots => Set<BomSnapshot>();

    public DbSet<QualitySample> QualitySamples => Set<QualitySample>();
    public DbSet<MeasurementSession> MeasurementSessions => Set<MeasurementSession>();
    public DbSet<DeviceEvent> DeviceEvents => Set<DeviceEvent>();

    public DbSet<Reconciliation> Reconciliations => Set<Reconciliation>();
    public DbSet<ReconciliationCheck> ReconciliationChecks => Set<ReconciliationCheck>();
    public DbSet<Allocation> Allocations => Set<Allocation>();

    public DbSet<IntegrationMessage> IntegrationMessages => Set<IntegrationMessage>();
    public DbSet<GateDecision> GateDecisions => Set<GateDecision>();
    public DbSet<GateCommand> GateCommands => Set<GateCommand>();
    public DbSet<WeighTicket> WeighTickets => Set<WeighTicket>();

    public DbSet<Approval> Approvals => Set<Approval>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<QueueSequenceTracker> QueueSequenceTrackers => Set<QueueSequenceTracker>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WeighingDbContext).Assembly);

        // Xóa mềm (deleted_at) cho kết quả đo và lượt cân: bản đã xóa không còn hiện ở lịch sử, không còn
        // được tính là "đã đo tạp chất" / "đã cân" ở bất kỳ màn hình hay điều kiện chốt cân nào.
        modelBuilder.Entity<QualitySample>().HasQueryFilter(s => s.DeletedAt == null);
        modelBuilder.Entity<WeighEvent>().HasQueryFilter(e => e.DeletedAt == null);

        // Mỗi nhà máy (BU của tài khoản đăng nhập) chỉ thấy chuyến của mình — áp cho MỌI màn hình / truy vấn EF,
        // kể cả truy vấn con (db.Trips.Any(...)). Admin / tổng công ty (TTCBH) thấy tất cả.
        // Tham chiếu thuộc tính của DbContext để EF đọc lại giá trị ở mỗi lần truy vấn.
        modelBuilder.Entity<Trip>().HasQueryFilter(t => AllOrgUnits || t.OrganizationUnitCode == CurrentOrgUnit);
        modelBuilder.Entity<IntegrationMessage>().HasQueryFilter(m =>
            AllOrgUnits || Set<Trip>().Any(t => t.Id == m.TripId));

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>BU của tài khoản đăng nhập (đặt sau đăng nhập).</summary>
    private string? CurrentOrgUnit => Queries.OrganizationUnitFilter.CurrentOrgUnit;

    /// <summary>Không lọc BU: chưa đăng nhập theo đơn vị (admin) hoặc tổng công ty.</summary>
    private bool AllOrgUnits => Queries.OrganizationUnitFilter.SeesAllUnits;

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Mọi giá trị kg dùng numeric(15,3) — không float/double (R-008).
        builder.Properties<decimal>().HavePrecision(15, 3);
        base.ConfigureConventions(builder);
    }
}

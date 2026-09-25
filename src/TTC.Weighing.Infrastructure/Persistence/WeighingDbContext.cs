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
        base.OnModelCreating(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Mọi giá trị kg dùng numeric(15,3) — không float/double (R-008).
        builder.Properties<decimal>().HavePrecision(15, 3);
        base.ConfigureConventions(builder);
    }
}

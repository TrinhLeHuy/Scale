using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Infrastructure.Persistence;

namespace TTC.Weighing.Infrastructure.Repositories;

public sealed class QualitySampleRepository(WeighingDbContext db) : IQualitySampleRepository
{
    public Task<QualitySample?> GetEffectiveAsync(
        Guid tripId, QualitySampleType type, CancellationToken ct = default)
        => db.QualitySamples
            .Where(s => s.TripId == tripId && s.Type == type && s.IsEffective)
            .OrderByDescending(s => s.Revision)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<QualitySample>> ListByTripAsync(
        Guid tripId, CancellationToken ct = default)
        => await db.QualitySamples
            .Where(s => s.TripId == tripId)
            .OrderBy(s => s.Type).ThenByDescending(s => s.Revision)
            .ToListAsync(ct);

    public async Task AddAsync(QualitySample sample, CancellationToken ct = default)
        => await db.QualitySamples.AddAsync(sample, ct);
}

public sealed class MeasurementSessionRepository(WeighingDbContext db) : IMeasurementSessionRepository
{
    public Task<MeasurementSession?> GetByIdAsync(Guid sessionId, CancellationToken ct = default)
        => db.MeasurementSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);

    public async Task<IReadOnlyList<MeasurementSession>> ListByTripAsync(
        Guid tripId, CancellationToken ct = default)
        => await db.MeasurementSessions
            .Where(s => s.TripId == tripId)
            .OrderBy(s => s.OpenedAt)
            .ToListAsync(ct);

    public async Task<MeasurementSession?> GetOpenSessionByDeviceAsync(
        Guid deviceId, CancellationToken ct = default)
    {
        // Kiểm tra trong bộ nhớ vì DeviceIds lưu jsonb; tập phiên đang mở luôn nhỏ.
        var openSessions = await db.MeasurementSessions
            .Where(s => s.Status == MeasurementSessionStatus.Open)
            .ToListAsync(ct);

        return openSessions.FirstOrDefault(s => s.DeviceIds.Contains(deviceId));
    }

    public async Task AddAsync(MeasurementSession session, CancellationToken ct = default)
        => await db.MeasurementSessions.AddAsync(session, ct);
}

public sealed class DeviceEventRepository(WeighingDbContext db) : IDeviceEventRepository
{
    public Task<DeviceEvent?> FindByDedupKeyAsync(
        Guid deviceId, string bootSessionId, long sequence, CancellationToken ct = default)
        => db.DeviceEvents.FirstOrDefaultAsync(
            e => e.DeviceId == deviceId
                && e.BootSessionId == bootSessionId
                && e.Sequence == sequence, ct);

    public async Task<IReadOnlyList<DeviceEvent>> ListBySessionAsync(
        Guid measurementSessionId, CancellationToken ct = default)
        => await db.DeviceEvents
            .Where(e => e.MeasurementSessionId == measurementSessionId)
            .OrderBy(e => e.Sequence)
            .ToListAsync(ct);

    public async Task AddAsync(DeviceEvent deviceEvent, CancellationToken ct = default)
        => await db.DeviceEvents.AddAsync(deviceEvent, ct);
}

public sealed class ReconciliationRepository(WeighingDbContext db) : IReconciliationRepository
{
    public Task<Reconciliation?> GetEffectiveAsync(Guid tripId, CancellationToken ct = default)
        => db.Reconciliations
            .Include(r => r.Checks)
            .Where(r => r.TripId == tripId && r.IsEffective)
            .OrderByDescending(r => r.Revision)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(Reconciliation reconciliation, CancellationToken ct = default)
        => await db.Reconciliations.AddAsync(reconciliation, ct);
}

public sealed class AllocationRepository(WeighingDbContext db) : IAllocationRepository
{
    public async Task<IReadOnlyList<Allocation>> ListByTripAsync(
        Guid tripId, CancellationToken ct = default)
        => await db.Allocations
            .Where(a => a.TripId == tripId && a.DeletedAt == null)
            .OrderBy(a => a.OwnerRef)
            .ToListAsync(ct);

    public async Task ReplaceForTripAsync(
        Guid tripId, IEnumerable<Allocation> allocations, CancellationToken ct = default)
    {
        // Không xóa cứng: bản cũ được đánh dấu để giữ dấu vết (03_Du_lieu mục 3).
        var existing = await db.Allocations
            .Where(a => a.TripId == tripId && a.DeletedAt == null)
            .ToListAsync(ct);

        foreach (var item in existing)
            item.DeletedAt = DateTimeOffset.UtcNow;

        await db.Allocations.AddRangeAsync(allocations, ct);
    }
}

public sealed class IntegrationMessageRepository(WeighingDbContext db) : IIntegrationMessageRepository
{
    public Task<IntegrationMessage?> FindByBusinessKeyAsync(
        string businessKey, CancellationToken ct = default)
        => db.IntegrationMessages.FirstOrDefaultAsync(m => m.BusinessKey == businessKey, ct);

    public async Task<IReadOnlyList<IntegrationMessage>> ListByTripAsync(
        Guid tripId, CancellationToken ct = default)
        => await db.IntegrationMessages
            .Where(m => m.TripId == tripId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<IntegrationMessage>> ListRequiringAttentionAsync(
        Guid siteId, CancellationToken ct = default)
    {
        var query = db.IntegrationMessages
            .Where(m => m.Status == IntegrationMessageStatus.Unknown
                || m.Status == IntegrationMessageStatus.Failed
                || m.Status == IntegrationMessageStatus.Held);

        if (siteId != Guid.Empty)
        {
            query = query.Where(m => db.Trips.Any(t => t.Id == m.TripId && t.SiteId == siteId));
        }

        return await query.OrderByDescending(m => m.CreatedAt).ToListAsync(ct);
    }

    public async Task AddAsync(IntegrationMessage message, CancellationToken ct = default)
        => await db.IntegrationMessages.AddAsync(message, ct);
}

public sealed class GateRepository(WeighingDbContext db) : IGateRepository
{
    public Task<GateCommand?> GetActiveCommandAsync(Guid tripId, CancellationToken ct = default)
        => db.GateCommands
            .Where(c => c.TripId == tripId
                && (c.State == GateCommandState.Requested
                    || c.State == GateCommandState.Acknowledged
                    || c.State == GateCommandState.Unknown))
            .OrderByDescending(c => c.IssuedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<GateCommand>> ListUnknownCommandsAsync(
        Guid siteId, CancellationToken ct = default)
        => await db.GateCommands
            .Where(c => c.State == GateCommandState.Unknown
                && db.Trips.Any(t => t.Id == c.TripId && t.SiteId == siteId))
            .OrderByDescending(c => c.IssuedAt)
            .ToListAsync(ct);

    public async Task AddDecisionAsync(GateDecision decision, CancellationToken ct = default)
        => await db.GateDecisions.AddAsync(decision, ct);

    public async Task AddCommandAsync(GateCommand command, CancellationToken ct = default)
        => await db.GateCommands.AddAsync(command, ct);
}

public sealed class ExternalDocumentRepository(WeighingDbContext db) : IExternalDocumentRepository
{
    public Task<ExternalDocument?> GetByReferenceAsync(
        string sourceSystem, string referenceNo, CancellationToken ct = default)
        => db.ExternalDocuments
            .Include(d => d.Lines)
            .FirstOrDefaultAsync(
                d => d.SourceSystem == sourceSystem && d.ReferenceNo == referenceNo, ct);

    public async Task<IReadOnlyList<TripDocumentLine>> ListLinesByTripAsync(
        Guid tripId, CancellationToken ct = default)
        => await db.TripDocumentLines
            .Where(l => l.TripId == tripId)
            .OrderBy(l => l.MovementSegmentId)
            .ToListAsync(ct);

    public async Task AddAsync(ExternalDocument document, CancellationToken ct = default)
        => await db.ExternalDocuments.AddAsync(document, ct);

    public async Task AddLineAsync(TripDocumentLine line, CancellationToken ct = default)
        => await db.TripDocumentLines.AddAsync(line, ct);
}

public sealed class BomSnapshotRepository(WeighingDbContext db) : IBomSnapshotRepository
{
    public Task<BomSnapshot?> GetEffectiveAsync(
        string sourceSystem, string itemCode, DateTimeOffset at, CancellationToken ct = default)
        => db.BomSnapshots
            .Where(s => s.SourceSystem == sourceSystem
                && s.ItemCode == itemCode
                && (s.EffectiveAt == null || s.EffectiveAt <= at))
            .OrderByDescending(s => s.EffectiveAt)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(BomSnapshot snapshot, CancellationToken ct = default)
        => await db.BomSnapshots.AddAsync(snapshot, ct);
}

public sealed class ApprovalRepository(WeighingDbContext db) : IApprovalRepository
{
    public Task<Approval?> GetByIdAsync(Guid approvalId, CancellationToken ct = default)
        => db.Approvals.FirstOrDefaultAsync(a => a.Id == approvalId, ct);

    public async Task<IReadOnlyList<Approval>> ListPendingAsync(
        Guid siteId, CancellationToken ct = default)
        => await db.Approvals
            .Where(a => a.ApprovedAt == null
                && (siteId == Guid.Empty
                    || db.Trips.Any(t => t.Id == a.TripId && t.SiteId == siteId)))
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(Approval approval, CancellationToken ct = default)
        => await db.Approvals.AddAsync(approval, ct);
}

public sealed class IncidentRepository(WeighingDbContext db) : IIncidentRepository
{
    public Task<Incident?> GetOpenByTripAsync(Guid tripId, CancellationToken ct = default)
        => db.Incidents
            .Where(i => i.TripId == tripId && i.ResolvedAt == null)
            .OrderByDescending(i => i.OpenedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Incident>> ListOpenAsync(
        Guid siteId, CancellationToken ct = default)
        => await db.Incidents
            .Where(i => i.SiteId == siteId && i.ResolvedAt == null)
            .OrderByDescending(i => i.OpenedAt)
            .ToListAsync(ct);

    public async Task AddAsync(Incident incident, CancellationToken ct = default)
        => await db.Incidents.AddAsync(incident, ct);
}

/// <summary>
/// Ghi audit vào bảng bền vững (R-026). Chỉ thêm vào context; handler chịu trách nhiệm lưu
/// cùng transaction với thay đổi mà bản ghi này mô tả.
/// </summary>
public sealed class EfAuditLogger(WeighingDbContext db) : IAuditLogger
{
    public async Task LogAsync(
        string actionType, Guid? tripId, string actor,
        string? beforeJson, string? afterJson, string? reason,
        string correlationId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        await db.AuditLogEntries.AddAsync(new AuditLogEntry
        {
            ActionType = actionType,
            TripId = tripId,
            Actor = actor,
            BeforeJson = NormalizeJson(beforeJson),
            AfterJson = NormalizeJson(afterJson),
            Reason = reason,
            CorrelationId = correlationId,
            OccurredAt = now,
            CreatedAt = now,
            CreatedBy = actor
        }, ct);
    }

    /// <summary>
    /// Cột lưu jsonb nên nội dung phải là JSON hợp lệ. Chuỗi mô tả tự do được bọc thành
    /// đối tượng có trường text, thay vì làm mất bản ghi audit.
    /// </summary>
    private static string? NormalizeJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            using var _ = JsonDocument.Parse(value);
            return value;
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(new { text = value });
        }
    }
}

/// <summary>R-024 — còn thông điệp UNKNOWN thì không phát sinh thao tác mới.</summary>
public sealed class EfIntegrationStateQuery(WeighingDbContext db) : IIntegrationStateQuery
{
    public Task<bool> HasUnresolvedUnknownAsync(Guid tripId, CancellationToken ct = default)
        => db.IntegrationMessages.AnyAsync(
            m => m.TripId == tripId && m.Status == IntegrationMessageStatus.Unknown, ct);
}

public sealed class EfWeighTicketRepository(WeighingDbContext db) : IWeighTicketRepository
{
    public Task<WeighTicket?> GetByTripAsync(Guid tripId, CancellationToken ct = default)
        => db.WeighTickets
            .Where(t => t.TripId == tripId && !t.IsReprint)
            .OrderByDescending(t => t.PrintedAt)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(WeighTicket ticket, CancellationToken ct = default)
        => await db.WeighTickets.AddAsync(ticket, ct);

    public async Task IncrementPrintCountAsync(Guid ticketId, CancellationToken ct = default)
    {
        var ticket = await db.WeighTickets.FindAsync([ticketId], ct);
        if (ticket is not null)
            ticket.PrintCount++;
    }
}

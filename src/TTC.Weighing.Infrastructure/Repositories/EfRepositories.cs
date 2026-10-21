using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;
using TTC.Weighing.Infrastructure.Persistence;

namespace TTC.Weighing.Infrastructure.Repositories;

public sealed class EfUnitOfWork(WeighingDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default)
        => new EfTransaction(await db.Database.BeginTransactionAsync(ct));

    /// <summary>
    /// Bọc transaction của EF. Không commit thì Dispose sẽ rollback, đúng hành vi mong muốn
    /// khi luồng nghiệp vụ dừng giữa đường.
    /// </summary>
    private sealed class EfTransaction(IDbContextTransaction transaction) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken ct = default) => transaction.CommitAsync(ct);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}

public sealed class TripRepository(WeighingDbContext db) : ITripRepository
{
    public Task<Trip?> GetByIdAsync(Guid tripId, CancellationToken ct = default)
        => db.Trips
            .Include(t => t.QrTokens)
            .Include(t => t.Checkpoints)
            .FirstOrDefaultAsync(t => t.Id == tripId && t.DeletedAt == null, ct);

    public Task<Trip?> GetByTripNoAsync(string tripNo, Guid siteId, CancellationToken ct = default)
        => db.Trips
            .Include(t => t.QrTokens)
            .Include(t => t.Checkpoints)
            .FirstOrDefaultAsync(t => t.TripNo == tripNo && t.SiteId == siteId && t.DeletedAt == null, ct);

    public Task<Trip?> GetActiveByPlateAsync(string plate, Guid siteId, CancellationToken ct = default)
    {
        var normalized = plate.Replace(" ", "").Replace("-", "").Replace(".", "").ToUpper();

        return db.Trips
            .Include(t => t.QrTokens)
            .Include(t => t.Checkpoints)
            .Where(t => t.SiteId == siteId
                && t.DeletedAt == null
                && t.TractorPlate != null
                && (t.Lifecycle == TripLifecycle.Registered
                    || t.Lifecycle == TripLifecycle.InProgress
                    || t.Lifecycle == TripLifecycle.WeighingCompleted))
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(t =>
                t.TractorPlate!.Replace(" ", "").Replace("-", "").Replace(".", "").ToUpper() == normalized, ct);
    }

    public Task<Trip?> GetForUpdateAsync(Guid tripId, int expectedVersion, CancellationToken ct = default)
        => db.Trips
            .Include(t => t.QrTokens)
            .FirstOrDefaultAsync(
                t => t.Id == tripId && t.Version == expectedVersion && t.DeletedAt == null, ct);

    public async Task AddAsync(Trip trip, CancellationToken ct = default)
        => await db.Trips.AddAsync(trip, ct);

    public Task UpdateAsync(Trip trip, CancellationToken ct = default)
    {
        db.Trips.Update(trip);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountPendingByWeighbridgeAsync(
        Guid siteId, CancellationToken ct = default)
    {
        // Chuyến chờ cân: Lifecycle ∈ {Registered, InProgress} và đã gán bàn cân
        var counts = await db.Trips
            .Where(t => t.SiteId == siteId
                && t.DeletedAt == null
                && t.AssignedWeighbridgeId != null
                && (t.Lifecycle == TripLifecycle.Registered || t.Lifecycle == TripLifecycle.InProgress))
            .GroupBy(t => t.AssignedWeighbridgeId!.Value)
            .Select(g => new { WeighbridgeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.WeighbridgeId, x => x.Count, ct);

        return counts;
    }

    public async Task<string?> FindActiveByLenhDonIdAsync(string lenhDonId, CancellationToken ct = default)
    {
        return await db.Trips
            .Where(t => t.FrmLenhDonId == lenhDonId
                && t.DeletedAt == null
                && t.Lifecycle != TripLifecycle.Cancelled
                && t.Lifecycle != TripLifecycle.Closed)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => t.TripNo)
            .FirstOrDefaultAsync(ct);
    }
}

public sealed class QrTokenRepository(WeighingDbContext db) : IQrTokenRepository
{
    public Task<QrToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default)
        => db.QrTokens.FirstOrDefaultAsync(q => q.TokenHash == tokenHash, ct);

    public async Task AddAsync(QrToken token, CancellationToken ct = default)
        => await db.QrTokens.AddAsync(token, ct);
}

public sealed class WeighSessionRepository(WeighingDbContext db) : IWeighSessionRepository
{
    public Task<WeighSession?> GetByIdAsync(Guid sessionId, CancellationToken ct = default)
        => db.WeighSessions
            .Include(s => s.Events)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

    public Task<WeighSession?> GetOwningSessionAsync(Guid weighbridgeId, CancellationToken ct = default)
        => db.WeighSessions.FirstOrDefaultAsync(
            s => s.WeighbridgeId == weighbridgeId && s.Status == WeighSessionStatus.Open, ct);

    public Task<WeighEvent?> GetEffectiveEventAsync(
        Guid tripId, WeightRole role, int segmentIndex, CancellationToken ct = default)
        => db.WeighEvents
            .Where(e => e.TripId == tripId && e.Role == role && e.IsEffective)
            .OrderByDescending(e => e.Revision)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(WeighSession session, CancellationToken ct = default)
        => await db.WeighSessions.AddAsync(session, ct);

    public async Task AddEventAsync(WeighEvent weighEvent, CancellationToken ct = default)
        => await db.WeighEvents.AddAsync(weighEvent, ct);
}

public sealed class WorkflowProfileRepository(WeighingDbContext db) : IWorkflowProfileRepository
{
    public Task<WorkflowProfile?> GetEffectiveAsync(
        Guid profileId, int version, CancellationToken ct = default)
        => db.WorkflowProfiles.FirstOrDefaultAsync(
            p => p.Id == profileId && p.ProfileVersion == version, ct);

    public async Task<IReadOnlyList<WorkflowProfile>> ListApprovedAsync(
        Guid siteId, CancellationToken ct = default)
        => await db.WorkflowProfiles
            .Where(p => p.ApprovedBy != null && p.ApprovedAt != null)
            .OrderBy(p => p.Code)
            .ToListAsync(ct);
}

public sealed class WeighbridgeRepository(WeighingDbContext db) : IWeighbridgeRepository
{
    public Task<Weighbridge?> GetByIdAsync(Guid weighbridgeId, CancellationToken ct = default)
        => db.Weighbridges.FirstOrDefaultAsync(w => w.Id == weighbridgeId, ct);

    public async Task<IReadOnlyList<Weighbridge>> ListBySiteAsync(
        Guid siteId, CancellationToken ct = default)
        => await db.Weighbridges
            .Where(w => w.SiteId == siteId && w.DeletedAt == null)
            .OrderBy(w => w.Code)
            .ToListAsync(ct);
}

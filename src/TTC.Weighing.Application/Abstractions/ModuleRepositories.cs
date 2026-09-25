using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.Abstractions;

public interface IQualitySampleRepository
{
    Task<QualitySample?> GetEffectiveAsync(
        Guid tripId, QualitySampleType type, CancellationToken ct = default);

    Task<IReadOnlyList<QualitySample>> ListByTripAsync(Guid tripId, CancellationToken ct = default);

    Task AddAsync(QualitySample sample, CancellationToken ct = default);
}

public interface IMeasurementSessionRepository
{
    Task<MeasurementSession?> GetByIdAsync(Guid sessionId, CancellationToken ct = default);

    Task<IReadOnlyList<MeasurementSession>> ListByTripAsync(Guid tripId, CancellationToken ct = default);

    /// <summary>Phiên đang mở của một thiết bị. Một nguồn đo không gán hai chuyến (B-010).</summary>
    Task<MeasurementSession?> GetOpenSessionByDeviceAsync(Guid deviceId, CancellationToken ct = default);

    Task AddAsync(MeasurementSession session, CancellationToken ct = default);
}

public interface IDeviceEventRepository
{
    /// <summary>Tra theo khóa chống trùng DeviceId + BootSessionId + Sequence (B-013).</summary>
    Task<DeviceEvent?> FindByDedupKeyAsync(
        Guid deviceId, string bootSessionId, long sequence, CancellationToken ct = default);

    Task<IReadOnlyList<DeviceEvent>> ListBySessionAsync(
        Guid measurementSessionId, CancellationToken ct = default);

    Task AddAsync(DeviceEvent deviceEvent, CancellationToken ct = default);
}

public interface IReconciliationRepository
{
    Task<Reconciliation?> GetEffectiveAsync(Guid tripId, CancellationToken ct = default);
    Task AddAsync(Reconciliation reconciliation, CancellationToken ct = default);
}

public interface IAllocationRepository
{
    Task<IReadOnlyList<Allocation>> ListByTripAsync(Guid tripId, CancellationToken ct = default);
    Task ReplaceForTripAsync(Guid tripId, IEnumerable<Allocation> allocations, CancellationToken ct = default);
}

public interface IIntegrationMessageRepository
{
    Task<IntegrationMessage?> FindByBusinessKeyAsync(string businessKey, CancellationToken ct = default);

    Task<IReadOnlyList<IntegrationMessage>> ListByTripAsync(Guid tripId, CancellationToken ct = default);

    Task<IReadOnlyList<IntegrationMessage>> ListRequiringAttentionAsync(
        Guid siteId, CancellationToken ct = default);

    Task AddAsync(IntegrationMessage message, CancellationToken ct = default);
}

public interface IGateRepository
{
    Task<GateCommand?> GetActiveCommandAsync(Guid tripId, CancellationToken ct = default);

    Task<IReadOnlyList<GateCommand>> ListUnknownCommandsAsync(Guid siteId, CancellationToken ct = default);

    Task AddDecisionAsync(GateDecision decision, CancellationToken ct = default);
    Task AddCommandAsync(GateCommand command, CancellationToken ct = default);
}

public interface IExternalDocumentRepository
{
    Task<ExternalDocument?> GetByReferenceAsync(
        string sourceSystem, string referenceNo, CancellationToken ct = default);

    Task<IReadOnlyList<TripDocumentLine>> ListLinesByTripAsync(Guid tripId, CancellationToken ct = default);

    Task AddAsync(ExternalDocument document, CancellationToken ct = default);
    Task AddLineAsync(TripDocumentLine line, CancellationToken ct = default);
}

public interface IBomSnapshotRepository
{
    Task<BomSnapshot?> GetEffectiveAsync(
        string sourceSystem, string itemCode, DateTimeOffset at, CancellationToken ct = default);

    Task AddAsync(BomSnapshot snapshot, CancellationToken ct = default);
}

public interface IApprovalRepository
{
    Task<Approval?> GetByIdAsync(Guid approvalId, CancellationToken ct = default);

    Task<IReadOnlyList<Approval>> ListPendingAsync(Guid siteId, CancellationToken ct = default);

    Task AddAsync(Approval approval, CancellationToken ct = default);
}

public interface IIncidentRepository
{
    Task<Incident?> GetOpenByTripAsync(Guid tripId, CancellationToken ct = default);
    Task<IReadOnlyList<Incident>> ListOpenAsync(Guid siteId, CancellationToken ct = default);
    Task AddAsync(Incident incident, CancellationToken ct = default);
}

public interface IWeighTicketRepository
{
    Task<WeighTicket?> GetByTripAsync(Guid tripId, CancellationToken ct = default);
    Task AddAsync(WeighTicket ticket, CancellationToken ct = default);
    Task IncrementPrintCountAsync(Guid ticketId, CancellationToken ct = default);
}

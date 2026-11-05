using TTC.Weighing.Domain.Entities;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Application.Abstractions;

public interface IUnitOfWork
{
    /// <summary>Lưu cùng transaction với outbox message (03_Du_lieu mục 7).</summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default);
}

/// <summary>
/// Transaction phải được commit tường minh. Dispose mà chưa commit thì rollback,
/// để một lỗi giữa luồng không để lại dữ liệu ghi dở.
/// </summary>
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
}

public interface ITripRepository
{
    Task<Trip?> GetByIdAsync(Guid tripId, CancellationToken ct = default);

    /// <summary>Tra cứu chuyến theo số đăng tài (TripNo). Dùng khi quét barcode TripNo thay vì QR token.</summary>
    Task<Trip?> GetByTripNoAsync(string tripNo, Guid siteId, CancellationToken ct = default);

    /// <summary>
    /// Tra cứu chuyến đang trong nhà máy theo biển số xe (mới nhất, chưa hoàn tất).
    /// Dùng khi nhập biển số tại trạm đo tạp chất / chữ đường thay vì quét QR.
    /// </summary>
    Task<Trip?> GetActiveByPlateAsync(string plate, Guid siteId, CancellationToken ct = default);

    /// <summary>Kiểm tra version mong đợi để chống ghi đè đồng thời.</summary>
    Task<Trip?> GetForUpdateAsync(Guid tripId, int expectedVersion, CancellationToken ct = default);

    Task AddAsync(Trip trip, CancellationToken ct = default);

    /// <summary>Cập nhật thông tin chuyến (biển số, shipment, khách hàng, loại xe...) khi chỉnh sửa từ UI đăng tài.</summary>
    Task UpdateAsync(Trip trip, CancellationToken ct = default);

    /// <summary>
    /// Đếm số chuyến chờ cân (Lifecycle ∈ {Registered, InProgress}) được gán cho mỗi bàn cân tại nhà máy.
    /// Dùng cho load balancing khi xếp hàng (Req 9.2).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> CountPendingByWeighbridgeAsync(
        Guid siteId, CancellationToken ct = default);

    /// <summary>
    /// Tìm chuyến đã dùng lệnh đốn GUID — mọi trạng thái trừ Cancelled, kể cả đã cân xong (Closed).
    /// Chuyến đã xóa không tính. Trả TripNo nếu tìm thấy.
    /// </summary>
    Task<string?> FindActiveByLenhDonIdAsync(string lenhDonId, CancellationToken ct = default);

    /// <summary>
    /// Tìm chuyến đã dùng chứng từ nguồn (ví dụ Shipment/TRIP ERP của vật tư) — mọi trạng thái trừ Cancelled,
    /// kể cả đã cân xong (Closed). Trả TripNo nếu tìm thấy; chỉ phiếu đã hủy / đã xóa mới cho đăng tài lại.
    /// </summary>
    Task<string?> FindActiveByReferenceAsync(string sourceSystem, string referenceTripNo, CancellationToken ct = default);

    /// <summary>
    /// Phiếu đăng tài mía đã HỦY (chưa xóa) của lệnh đốn — theo ID lệnh đốn hoặc mã lệnh đốn.
    /// Lệnh đốn có phiếu đã hủy không được đăng tài lại (phải lập lệnh đốn mới); phiếu đã XÓA thì được.
    /// Trả TripNo nếu tìm thấy.
    /// </summary>
    Task<string?> FindCancelledByLenhDonAsync(string? lenhDonId, string? maLenhDon, CancellationToken ct = default);

    /// <summary>
    /// Chuyến đã có lượt cân nào chưa (mọi WeighEvent, kể cả bản đã bị thay thế khi cân lại).
    /// Dùng để chặn hủy / xóa đăng tài khi xe đã cân.
    /// </summary>
    Task<bool> HasWeighEventsAsync(Guid tripId, CancellationToken ct = default);

    /// <summary>
    /// Nạp lại chuyến từ DB (bỏ giá trị cũ đang giữ trong bộ nhớ). Dùng trước khi sửa + lưu ở màn hình mở lâu,
    /// tránh ghi đè trạng thái mới do màn khác cập nhật (ví dụ đã cân xong → Closed).
    /// </summary>
    Task ReloadAsync(TTC.Weighing.Domain.Entities.Trip trip, CancellationToken ct = default);

    /// <summary>
    /// Số đăng tài của chuyến cùng biển số mà xe CHƯA CÂN RA (chưa hủy / đóng / xóa, chưa có lượt cân ra hiệu lực).
    /// Null = xe được đăng tài lệnh đốn mới.
    /// </summary>
    Task<string?> FindNotWeighedOutByPlateAsync(string plate, Guid siteId, CancellationToken ct = default);
}

public interface IQrTokenRepository
{
    Task<QrToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);
    Task AddAsync(QrToken token, CancellationToken ct = default);
}

public interface IWeighSessionRepository
{
    Task<WeighSession?> GetByIdAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Phiên đang sở hữu bàn cân, nếu có. Dùng để chặn hai phiên cùng bàn (B-003).</summary>
    Task<WeighSession?> GetOwningSessionAsync(Guid weighbridgeId, CancellationToken ct = default);

    /// <summary>Bản cân đang hiệu lực cho vai trò trong phân đoạn, để kiểm tra lượt cân trước.</summary>
    Task<WeighEvent?> GetEffectiveEventAsync(
        Guid tripId, WeightRole role, int segmentIndex, CancellationToken ct = default);

    Task AddAsync(WeighSession session, CancellationToken ct = default);
    Task AddEventAsync(WeighEvent weighEvent, CancellationToken ct = default);
}

public interface IWorkflowProfileRepository
{
    Task<WorkflowProfile?> GetEffectiveAsync(Guid profileId, int version, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowProfile>> ListApprovedAsync(Guid siteId, CancellationToken ct = default);
}

public interface IWeighbridgeRepository
{
    Task<Weighbridge?> GetByIdAsync(Guid weighbridgeId, CancellationToken ct = default);
    Task<IReadOnlyList<Weighbridge>> ListBySiteAsync(Guid siteId, CancellationToken ct = default);
}

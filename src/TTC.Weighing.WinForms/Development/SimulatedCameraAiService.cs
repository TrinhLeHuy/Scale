using TTC.Weighing.Application.Abstractions;

namespace TTC.Weighing.WinForms.Development;

/// <summary>
/// Giả lập AI camera tại bàn cân. Cho phép thay đổi kết quả phân tích qua code
/// hoặc nút bấm trên UI để demo cho các sếp.
///
/// Khi có API camera thật, thay bằng implementation gọi API.
/// Đăng ký Singleton để các form dùng chung trạng thái.
/// </summary>
public sealed class SimulatedCameraAiService : ICameraAiService
{
    private bool _personDetected;
    private decimal _personConfidence = 0.95m;
    private VehicleAlignmentStatus _alignment = VehicleAlignmentStatus.Aligned;
    private decimal _alignmentConfidence = 0.92m;
    private CargoStatus _cargoStatus = CargoStatus.Open;
    private decimal _cargoConfidence = 0.88m;

    /// <summary>Sự kiện khi kết quả AI thay đổi — form đăng ký để cập nhật UI.</summary>
    public event EventHandler? ResultChanged;

    // ── Setters cho giả lập (gọi từ UI) ──

    public void SetPersonDetected(bool detected, decimal confidence = 0.95m)
    {
        _personDetected = detected;
        _personConfidence = confidence;
        ResultChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetAlignment(VehicleAlignmentStatus alignment, decimal confidence = 0.92m)
    {
        _alignment = alignment;
        _alignmentConfidence = confidence;
        ResultChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetCargoStatus(CargoStatus status, decimal confidence = 0.88m)
    {
        _cargoStatus = status;
        _cargoConfidence = confidence;
        ResultChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reset về trạng thái bình thường (không cảnh báo).</summary>
    public void ResetToNormal()
    {
        _personDetected = false;
        _personConfidence = 0.95m;
        _alignment = VehicleAlignmentStatus.Aligned;
        _alignmentConfidence = 0.92m;
        _cargoStatus = CargoStatus.Open;
        _cargoConfidence = 0.88m;
        ResultChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Interface implementation ──

    public Task<CameraAiAnalysisResult> AnalyzeAsync(
        Guid weighbridgeId, CancellationToken ct = default)
    {
        var hasWarnings = _personDetected
            || _alignment is VehicleAlignmentStatus.Misaligned or VehicleAlignmentStatus.NoVehicle
            || _cargoStatus is CargoStatus.Closed;

        var result = new CameraAiAnalysisResult(
            PersonDetected: _personDetected,
            PersonConfidence: _personConfidence,
            Alignment: _alignment,
            AlignmentConfidence: _alignmentConfidence,
            CargoStatus: _cargoStatus,
            CargoConfidence: _cargoConfidence,
            AnalyzedAt: DateTimeOffset.UtcNow,
            ModelVersion: "sim-1.0",
            SnapshotRef: null,
            HasWarnings: hasWarnings);

        return Task.FromResult(result);
    }
}

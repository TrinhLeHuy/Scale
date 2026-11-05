using TTC.Weighing.Infrastructure.Adapters;
using TTC.Weighing.Domain.Common;

namespace TTC.Weighing.WinForms.Services;

/// <summary>
/// Quy tắc cân (ngưỡng tạp chất, CCS, Fibre, khối lượng tối đa, cân lần 1 tối thiểu) lấy từ Portal DigiScale
/// theo đơn vị của tài khoản đăng nhập. Không lấy được → các ngưỡng = null, màn hình cảnh báo
/// "chưa cài ngưỡng" — KHÔNG dùng giá trị hardcode.
/// Mỗi lần quét xe ở trạm tạp chất/CCS gọi <see cref="RefreshAsync"/> để nhận thay đổi mới trên Portal;
/// lỗi mạng thì giữ bản đã lấy thành công gần nhất (vẫn là dữ liệu Portal).
/// </summary>
public static class PortalScaleRules
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static Func<CancellationToken, Task<ScaleRulesFetchResult>>? _loader;

    /// <summary>Bản quy tắc lấy thành công gần nhất. Null = chưa lấy được từ Portal.</summary>
    public static ScaleRulesDto? Rules { get; private set; }

    /// <summary>Thời điểm lấy thành công gần nhất (giờ máy).</summary>
    public static DateTime? LoadedAt { get; private set; }

    /// <summary>Lỗi của lần gọi gần nhất (null nếu thành công).</summary>
    public static string? LastError { get; private set; }

    public static bool IsLoaded => Rules is not null;

    /// <summary>Gắn hàm tải quy tắc cho phiên đăng nhập hiện tại (đơn vị + token). Xoá bản cũ của phiên trước.</summary>
    public static void Configure(Func<CancellationToken, Task<ScaleRulesFetchResult>>? loader)
    {
        _loader = loader;
        Rules = null;
        LoadedAt = null;
        LastError = loader is null ? "NO_ORGANIZATION_UNIT" : null;
    }

    /// <summary>Kéo lại quy tắc từ Portal. Trả true nếu đang có quy tắc (mới hoặc bản gần nhất).</summary>
    public static async Task<bool> RefreshAsync(CancellationToken ct = default)
    {
        var loader = _loader;
        if (loader is null) return IsLoaded;

        await Gate.WaitAsync(ct);
        try
        {
            var result = await loader(ct);
            if (result.IsSuccess && result.Rules is not null)
            {
                Rules = result.Rules;
                LoadedAt = VnTime.Now;
                LastError = null;
            }
            else
            {
                LastError = result.Error ?? "UNKNOWN";
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            Gate.Release();
        }

        return IsLoaded;
    }
}

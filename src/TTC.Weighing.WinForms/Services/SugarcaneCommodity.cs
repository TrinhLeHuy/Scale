using TTC.Weighing.Infrastructure.Adapters;
using TTC.Weighing.WinForms.Localization;

namespace TTC.Weighing.WinForms.Services;

/// <summary>
/// Tên hàng hóa của chuyến mía theo lệnh đốn FRM:
/// Mía cháy = FRM Miachay; Mía khúc = thu hoạch máy; Mía cây = thu hoạch thủ công.
/// Mía cháy thu hoạch máy → "Mía cháy + Mía khúc". Dùng chung cho lưới, ô Hàng hóa và lúc đăng tài.
/// </summary>
public static class SugarcaneCommodity
{
    /// <summary>Null khi không có lệnh đốn hoặc FRM chưa trả cờ cháy / thu hoạch (dữ liệu cũ chưa làm mới).</summary>
    public static string? Label(FrmLenhDonInfo? ld) => Label(ld, Loc.T);

    /// <summary>Như <see cref="Label(FrmLenhDonInfo?)"/> nhưng dịch bằng hàm chỉ định (phiếu in song ngữ).</summary>
    public static string? Label(FrmLenhDonInfo? ld, Func<string, string> t)
    {
        if (ld is null || (ld.MiaChay is null && ld.ThuHoachMay is null)) return null;

        var (isChay, isCay, isKhuc) = ld.SugarcaneForm;
        var parts = new[]
        {
            isChay ? t("reg.burnt_cane") : null,
            isKhuc ? t("reg.cane_cut") : null,
            isCay ? t("reg.cane_whole") : null
        }.Where(p => p is not null);

        return string.Join(" + ", parts);
    }

    /// <summary>Tên hàng hóa theo lệnh đốn của chuyến (ưu tiên ID, sau đó mã lệnh đốn); null nếu không xác định.</summary>
    public static string? ForTrip(FrmMasterDataCache cache, string? lenhDonId, string? maLenhDon)
        => Label(cache.FindLenhDonBySourceKey(lenhDonId) ?? cache.FindLenhDonByCode(maLenhDon));
}

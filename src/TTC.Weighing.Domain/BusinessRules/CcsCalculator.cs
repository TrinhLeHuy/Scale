using TTC.Weighing.Domain.Common;

namespace TTC.Weighing.Domain.BusinessRules;

/// <summary>
/// Tính CCS (Commercial Cane Sugar) theo QCVN 01-98:2012/BNNPTNT.
/// Công thức từ hệ thống cũ (DigiScale):
///   PolTinh = (PolDoc × 26 × 110) / (99.718 × g × 100)
///   CCS = 1.5 × PolTinh × (95 - F) / 100 - 0.5 × Bx × (97 - F) / 100
/// Trong đó: g = tỷ trọng biểu kiến tra bảng theo Brix, F = tỉ lệ xơ (Fibre %).
/// </summary>
public static class CcsCalculator
{
    /// <summary>
    /// Tính CCS từ Pol đọc, Brix đọc và tỷ lệ xơ (Fibre).
    /// </summary>
    /// <param name="polDoc">Giá trị Pol đọc từ máy Polarimeter.</param>
    /// <param name="brixDoc">Giá trị Brix đọc từ máy đo.</param>
    /// <param name="fibre">Tỉ lệ xơ trong mía (%), xác định hàng tuần.</param>
    /// <returns>Kết quả tính CCS.</returns>
    public static CcsResult Calculate(decimal polDoc, decimal brixDoc, decimal fibre)
    {
        if (polDoc <= 0)
            return CcsResult.Invalid(DomainText.T("rule.ccs_pol_positive", "Pol phải lớn hơn 0."));
        if (brixDoc <= 0)
            return CcsResult.Invalid(DomainText.T("rule.ccs_brix_positive", "Brix phải lớn hơn 0."));
        if (fibre < 0)
            return CcsResult.Invalid(DomainText.T("rule.ccs_fibre_negative", "Fibre không được âm."));

        // Bước 1: Tra tỷ trọng biểu kiến theo Brix (bảng ICUMSA, 20°C/20°C)
        var bxRounded = Math.Round(brixDoc, 1, MidpointRounding.AwayFromZero);
        var g = LookupApparentDensity((double)bxRounded);
        if (g <= 0)
            return CcsResult.Invalid(DomainText.T("rule.ccs_density_not_found", "Không tra được tỷ trọng cho Brix = {0}.", bxRounded));

        // Bước 2: Tính Pol cải chính (PolTinh)
        // PolTinh = (PolDoc × 26 × 110) / (99.718 × g × 100)
        var polTinh = ((double)polDoc * 26.0 * 110.0) / (99.718 * g * 100.0);

        if (polTinh <= 0)
            return CcsResult.Invalid(DomainText.T("rule.ccs_pol_corrected_invalid", "Pol cải chính ≤ 0, kiểm tra lại giá trị Pol và Brix."));

        // Bước 3: Tính CCS
        // CCS = 1.5 × PolTinh × (95 - F) / 100 - 0.5 × BxDoc × (97 - F) / 100
        var f = (double)fibre;
        var bx = (double)brixDoc;
        var ccs = 1.5 * polTinh * (95.0 - f) / 100.0
                - 0.5 * bx * (97.0 - f) / 100.0;

        ccs = Math.Round(ccs, 2, MidpointRounding.AwayFromZero);

        return new CcsResult(
            IsValid: true,
            InvalidReason: null,
            PolCaiChinh: Math.Round((decimal)polTinh, 4),
            CcsValue: (decimal)ccs,
            ApparentDensity: (decimal)g);
    }

    /// <summary>
    /// Tra tỷ trọng biểu kiến (apparent density) của dung dịch đường ở 20°C/20°C
    /// theo bảng ICUMSA. Brix làm tròn 1 chữ số thập phân.
    /// Bảng rút gọn cho khoảng Brix 0–30 (mía thường 14–24).
    /// </summary>
    private static double LookupApparentDensity(double brix)
    {
        // Bảng tỷ trọng ICUMSA (Brix → density tại 20°C/20°C)
        // Nguồn: ICUMSA Methods Book, Sugar Analysis
        // Key = Brix × 10 (integer), Value = density
        // Chỉ lưu phần nguyên (0–30), nội suy tuyến tính cho phần thập phân
        double[] densityTable =
        [
            // Brix:  0      1      2      3      4      5      6      7      8      9
            /* 0 */ 0.99823, 1.00222, 1.00625, 1.01029, 1.01437, 1.01848, 1.02261, 1.02678, 1.03098, 1.03521,
            /*10 */ 1.03947, 1.04377, 1.04809, 1.05245, 1.05685, 1.06127, 1.06574, 1.07023, 1.07476, 1.07933,
            /*20 */ 1.08393, 1.08857, 1.09324, 1.09796, 1.10271, 1.10750, 1.11233, 1.11720, 1.12211, 1.12706,
            /*30 */ 1.13205
        ];

        if (brix < 0 || brix > 30)
            return 0; // Ngoài khoảng bảng

        var intPart = (int)brix;
        var fracPart = brix - intPart;

        if (intPart >= densityTable.Length - 1)
            return densityTable[^1];

        // Nội suy tuyến tính
        var d0 = densityTable[intPart];
        var d1 = densityTable[intPart + 1];
        return d0 + (d1 - d0) * fracPart;
    }
}

/// <summary>Kết quả tính CCS.</summary>
public sealed record CcsResult(
    bool IsValid,
    string? InvalidReason,
    decimal PolCaiChinh = 0,
    decimal CcsValue = 0,
    decimal ApparentDensity = 0)
{
    public static CcsResult Invalid(string reason)
        => new(false, reason);
}

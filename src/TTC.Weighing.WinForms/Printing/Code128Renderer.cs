namespace TTC.Weighing.WinForms.Printing;

/// <summary>
/// Pure GDI+ Code 128 (Code Set B) barcode renderer.
/// Không phụ thuộc NuGet ngoài — chỉ dùng System.Drawing.
/// Hỗ trợ ASCII 32–126 (đủ cho mã lệnh đốn, biển số, QR token).
/// Encoding: bar/space widths per ISO/IEC 15417.
/// </summary>
public static class Code128Renderer
{
    // Mỗi chuỗi = 6 ký số, mỗi ký số là độ rộng (1–4 modules) cho bar-space-bar-space-bar-space
    // 106 symbols + Stop
    private static readonly string[] Encodings =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213", // 0-9
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132", // 10-19
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211", // 20-29
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313", // 30-39
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331", // 40-49
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111", // 50-59
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214", // 60-69
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111", // 70-79
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141", // 80-89
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141", // 90-99
        "114131", "311141", "411131",                                                                        // 100-102
        "211412", "211214", "211232",                                                                        // 103-105 (START A/B/C)
    ];

    private const string StopEncoding = "2331112"; // 7 elements, 13 modules

    private const int START_B = 104;

    /// <summary>
    /// Vẽ barcode Code 128B trực tiếp lên Graphics context.
    /// </summary>
    /// <param name="g">GDI+ graphics.</param>
    /// <param name="data">Chuỗi cần mã hóa (ASCII 32–126).</param>
    /// <param name="x">Tọa độ X góc trái.</param>
    /// <param name="y">Tọa độ Y góc trên.</param>
    /// <param name="width">Chiều rộng tổng (pixel).</param>
    /// <param name="height">Chiều cao barcode (pixel).</param>
    /// <returns>true nếu vẽ thành công.</returns>
    public static bool Draw(Graphics g, string data, int x, int y, int width, int height)
    {
        if (string.IsNullOrEmpty(data))
            return false;

        // Encode: START B + data characters + checksum + STOP
        var values = new List<int> { START_B };

        foreach (var ch in data)
        {
            var val = ch - 32; // Code 128B: value = ASCII - 32
            if (val < 0 || val > 94) return false; // ngoài phạm vi Code 128B
            values.Add(val);
        }

        // Tính checksum (mod 103)
        var checksum = values[0]; // START B value = 104
        for (var i = 1; i < values.Count; i++)
            checksum += values[i] * i;
        checksum %= 103;
        values.Add(checksum);

        // Tính tổng số modules: mỗi symbol = 11 modules, STOP = 13 modules
        var totalModules = values.Count * 11 + 13;

        var moduleWidth = (float)width / totalModules;
        if (moduleWidth < 0.5f) moduleWidth = 0.5f;

        using var brush = new SolidBrush(Color.Black);
        var curX = (float)x;

        // Vẽ từng symbol
        foreach (var val in values)
        {
            if (val < 0 || val >= Encodings.Length) return false;
            curX = DrawPattern(g, brush, Encodings[val], curX, y, height, moduleWidth);
        }

        // Vẽ stop pattern (7 elements)
        DrawPattern(g, brush, StopEncoding, curX, y, height, moduleWidth);

        return true;
    }

    /// <summary>Vẽ một pattern (chuỗi widths). Chẵn = bar (đen), lẻ = space (trắng).</summary>
    private static float DrawPattern(
        Graphics g, Brush brush, string pattern, float curX, int y, int height, float moduleWidth)
    {
        for (var i = 0; i < pattern.Length; i++)
        {
            var w = (pattern[i] - '0') * moduleWidth;
            if (i % 2 == 0) // bar
                g.FillRectangle(brush, curX, y, w, height);
            curX += w;
        }
        return curX;
    }
}

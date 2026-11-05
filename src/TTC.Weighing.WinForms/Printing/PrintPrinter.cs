using System.Drawing.Printing;

namespace TTC.Weighing.WinForms.Printing;

/// <summary>
/// Máy in phiếu dùng chung cho mọi phiếu (đăng tài, cân, tạp chất, CCS). Thứ tự chọn:
/// <list type="number">
/// <item>Máy in người dùng chọn lần in trước trên máy này (%LocalAppData%\TTC.Weighing\print-printer.txt).</item>
/// <item><c>Printing:DefaultPrinter</c> trong appsettings (nếu máy in đó có cài trên máy).</item>
/// <item>Máy in mặc định của Windows, nếu không phải máy in ảo (PDF / XPS / OneNote / Fax).</item>
/// <item>Máy in thật đầu tiên đã cài; không có thì máy in mặc định của Windows.</item>
/// </list>
/// </summary>
public static class PrintPrinter
{
    private static readonly string PrefFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TTC.Weighing", "print-printer.txt");

    private static readonly string[] VirtualMarkers = ["PDF", "XPS", "OneNote", "Fax", "Send To", "Snagit"];

    private static string? _session;

    /// <summary>Máy in ảo (xuất file), không phải máy in phiếu thật.</summary>
    public static bool IsVirtual(string name)
        => VirtualMarkers.Any(m => name.Contains(m, StringComparison.OrdinalIgnoreCase));

    private static List<string> Installed()
    {
        try { return PrinterSettings.InstalledPrinters.Cast<string>().ToList(); }
        catch { return []; }
    }

    private static string? Match(List<string> installed, string? name)
        => string.IsNullOrWhiteSpace(name) ? null
            : installed.FirstOrDefault(p => string.Equals(p, name.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string? Saved()
    {
        if (_session is not null) return _session;
        try { return File.Exists(PrefFile) ? File.ReadAllText(PrefFile).Trim() : null; }
        catch { return null; }
    }

    /// <summary>Máy in sẽ dùng để in phiếu; null nếu máy chưa cài máy in nào.</summary>
    public static string? Current
    {
        get
        {
            var installed = Installed();
            if (installed.Count == 0) return null;

            var chosen = Match(installed, Saved()) ?? Match(installed, Program.AppConfiguration?["Printing:DefaultPrinter"]);
            if (chosen is not null) return chosen;

            string? windowsDefault = null;
            try { windowsDefault = new PrinterSettings().PrinterName; } catch { }
            if (windowsDefault is not null && !IsVirtual(windowsDefault) && Match(installed, windowsDefault) is { } d) return d;

            return installed.FirstOrDefault(p => !IsVirtual(p)) ?? Match(installed, windowsDefault) ?? installed[0];
        }
    }

    /// <summary>Nhớ máy in người dùng vừa in để lần sau tự chọn lại (theo máy).</summary>
    public static void Remember(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        _session = name.Trim();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefFile)!);
            File.WriteAllText(PrefFile, _session);
        }
        catch { /* không lưu được → chỉ nhớ trong phiên này */ }
    }

    /// <summary>Đặt máy in phiếu cho tài liệu. Gọi TRƯỚC <see cref="PrintPaper.Apply"/> (khổ giấy tra theo máy in).</summary>
    public static void Apply(PrintDocument doc)
    {
        if (Current is { } name) doc.PrinterSettings.PrinterName = name;
    }

    /// <summary>
    /// Hộp chọn máy in của Windows, chọn sẵn máy in phiếu. Bấm In → nhớ máy in đã chọn rồi in.
    /// Trả false nếu người dùng hủy.
    /// </summary>
    public static bool ShowDialogAndPrint(PrintDocument doc, IWin32Window owner)
    {
        Apply(doc);
        PrintPaper.Apply(doc);
        using var dialog = new PrintDialog { Document = doc, UseEXDialog = true };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return false;
        Remember(doc.PrinterSettings.PrinterName);
        PrintPaper.Apply(doc); // người dùng có thể đổi máy in → tra lại khổ giấy theo máy mới
        doc.Print();
        return true;
    }
}

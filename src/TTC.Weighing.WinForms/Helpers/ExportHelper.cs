using System.Diagnostics;
using System.Reflection;
using TTC.Weighing.WinForms.Localization;

namespace TTC.Weighing.WinForms.Helpers;

/// <summary>
/// Xuất lưới ra Excel (.xlsx). Lấy TOÀN BỘ danh sách đang lọc (không chỉ trang hiện tại),
/// đúng các cột đang hiển thị theo thứ tự người dùng đã kéo, tiêu đề cột theo ngôn ngữ hiện hành.
/// Chỉ đọc dữ liệu đã có trên client — không gọi DB/API, không thay đổi trạng thái nghiệp vụ.
/// </summary>
public static class ExportHelper
{
    /// <summary>Thông tin mô tả file xuất.</summary>
    public sealed record ExportRequest(
        string Title,
        string FileBaseName,
        IReadOnlyList<string>? Filters = null,
        string? SheetName = null);

    /// <summary>
    /// Đổi giá trị thô sang giá trị xuất cho một cột (ví dụ enum → chữ thân thiện).
    /// Trả <paramref name="raw"/> nếu không cần đổi.
    /// </summary>
    public delegate object? ValueMapper(string propertyName, object item, object? raw);

    /// <summary>
    /// Hỏi nơi lưu, ghi file trên thread nền, báo kết quả và đề nghị mở file.
    /// Trả true nếu đã ghi xong.
    /// </summary>
    public static async Task<bool> ExportGridAsync<T>(
        IWin32Window owner,
        DataGridView grid,
        IReadOnlyList<T> rows,
        ExportRequest request,
        ValueMapper? mapValue = null,
        Control? busyControl = null) where T : notnull
    {
        var caption = Loc.T("export.title");

        if (rows.Count == 0)
        {
            MessageBox.Show(owner, Loc.T("export.no_data"), caption,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        using var dialog = new SaveFileDialog
        {
            Title = caption,
            Filter = Loc.T("export.file_filter") + " (*.xlsx)|*.xlsx",
            DefaultExt = "xlsx",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"{request.FileBaseName}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
        };

        if (dialog.ShowDialog(owner) != DialogResult.OK) return false;

        var sheet = BuildSheet(grid, rows, request, mapValue);
        var path = dialog.FileName;

        var previousCursor = Cursor.Current;
        if (busyControl is not null) busyControl.Enabled = false;
        Cursor.Current = Cursors.WaitCursor;

        try
        {
            await Task.Run(() => XlsxWriter.Write(path, [sheet]));
        }
        catch (IOException ex)
        {
            // Thường gặp nhất: file cùng tên đang mở trong Excel.
            MessageBox.Show(owner, Loc.T("export.file_locked", path) + Environment.NewLine + ex.Message,
                caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            MessageBox.Show(owner, Loc.T("export.access_denied", path) + Environment.NewLine + ex.Message,
                caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        finally
        {
            Cursor.Current = previousCursor;
            if (busyControl is not null) busyControl.Enabled = true;
        }

        var open = MessageBox.Show(owner,
            Loc.T("export.success", rows.Count.ToString("N0"), path),
            caption, MessageBoxButtons.YesNo, MessageBoxIcon.Information);

        if (open == DialogResult.Yes)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, Loc.T("export.open_failed") + Environment.NewLine + ex.Message,
                    caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        return true;
    }

    /// <summary>Dựng sheet từ cột đang hiển thị của lưới + toàn bộ dòng dữ liệu.</summary>
    public static XlsxSheet BuildSheet<T>(
        DataGridView grid, IReadOnlyList<T> rows, ExportRequest request, ValueMapper? mapValue = null)
        where T : notnull
    {
        var columns = grid.Columns
            .Cast<DataGridViewColumn>()
            .Where(c => c.Visible && !string.IsNullOrEmpty(c.DataPropertyName))
            .OrderBy(c => c.DisplayIndex)
            .ToList();

        var properties = columns
            .Select(c => typeof(T).GetProperty(c.DataPropertyName, BindingFlags.Public | BindingFlags.Instance))
            .ToList();

        var xlsxColumns = columns
            .Select((c, i) => new XlsxColumn(
                c.HeaderText.Replace("\n", " ").Trim(),
                KindFor(properties[i]?.PropertyType, c.DefaultCellStyle.Format)))
            .ToList();

        var data = new List<object?[]>(rows.Count);
        foreach (var item in rows)
        {
            var values = new object?[columns.Count];
            for (var i = 0; i < columns.Count; i++)
            {
                var raw = properties[i]?.GetValue(item);
                var mapped = mapValue is null ? raw : mapValue(columns[i].DataPropertyName, item, raw);
                values[i] = Normalize(mapped);
            }
            data.Add(values);
        }

        var subtitles = new List<string>();
        if (request.Filters is { Count: > 0 } filters)
            subtitles.AddRange(filters);
        subtitles.Add(Loc.T("export.generated_at", DateTime.Now.ToString("dd/MM/yyyy HH:mm"), rows.Count.ToString("N0")));

        return new XlsxSheet
        {
            Name = request.SheetName ?? request.Title,
            Title = request.Title,
            Subtitles = subtitles,
            Columns = xlsxColumns,
            Rows = data
        };
    }

    /// <summary>Giá trị xuất: số/ngày giữ nguyên kiểu để Excel tính toán được, còn lại thành chuỗi.</summary>
    private static object? Normalize(object? value) => value switch
    {
        null => null,
        string s => string.IsNullOrWhiteSpace(s) ? null : s,
        decimal or int or long or short or double or float => value,
        DateTime or DateTimeOffset => value,
        bool b => Loc.YesNo(b),
        Enum e => Loc.Enum(e),
        _ => value.ToString()
    };

    /// <summary>
    /// Suy kiểu cột Excel từ kiểu thuộc tính + định dạng của cột lưới.
    /// Số thập phân không có định dạng riêng coi là khối lượng (N3) theo quy ước dự án.
    /// </summary>
    private static XlsxCellKind KindFor(Type? type, string? gridFormat)
    {
        if (type is null) return XlsxCellKind.Text;
        var t = Nullable.GetUnderlyingType(type) ?? type;

        if (t == typeof(DateTime) || t == typeof(DateTimeOffset)) return XlsxCellKind.DateTime;
        if (t == typeof(int) || t == typeof(long) || t == typeof(short)) return XlsxCellKind.Integer;

        if (t == typeof(decimal) || t == typeof(double) || t == typeof(float))
        {
            return gridFormat switch
            {
                "N0" => XlsxCellKind.Integer,
                "N2" => XlsxCellKind.Decimal2,
                "N4" => XlsxCellKind.Decimal4,
                _ => XlsxCellKind.Weight
            };
        }

        return XlsxCellKind.Text;
    }
}

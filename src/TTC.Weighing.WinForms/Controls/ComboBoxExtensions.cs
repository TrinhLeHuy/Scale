namespace TTC.Weighing.WinForms.Controls;

/// <summary>
/// Gán DataSource cho ComboBox mà không phát SelectedIndexChanged giữa chừng.
/// WinForms phát event ngay khi gán DataSource, làm handler async chạy song song
/// với code đang gán, gây lỗi "A second operation was started on this context instance".
/// </summary>
public static class ComboBoxExtensions
{
    /// <summary>
    /// Gán DataSource, chặn event handler trong lúc gán. Sau khi gán xong mới gọi callback.
    /// </summary>
    public static void SetDataSourceSafe(
        this ComboBox combo,
        object? dataSource,
        EventHandler? selectedIndexChangedHandler)
    {
        if (selectedIndexChangedHandler is not null)
            combo.SelectedIndexChanged -= selectedIndexChangedHandler;

        combo.DataSource = dataSource;

        if (selectedIndexChangedHandler is not null)
            combo.SelectedIndexChanged += selectedIndexChangedHandler;
    }
}

using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using TTC.Weighing.Domain.Common;

namespace TTC.Weighing.WinForms.Helpers;

/// <summary>Kiểu dữ liệu ô Excel — quyết định style/định dạng số.</summary>
public enum XlsxCellKind
{
    Text,
    /// <summary>Số cân/khối lượng — luôn #,##0.000 (thống nhất N3 theo quy ước dự án).</summary>
    Weight,
    Integer,
    Decimal2,
    Decimal4,
    DateTime
}

public sealed record XlsxColumn(string Header, XlsxCellKind Kind, double? Width = null);

/// <summary>Một sheet: tiêu đề, các dòng mô tả (bộ lọc, thời điểm xuất), bảng dữ liệu.</summary>
public sealed class XlsxSheet
{
    public required string Name { get; init; }
    public required string Title { get; init; }
    public IReadOnlyList<string> Subtitles { get; init; } = [];
    public required IReadOnlyList<XlsxColumn> Columns { get; init; }
    public required IReadOnlyList<object?[]> Rows { get; init; }
}

/// <summary>
/// Ghi tệp .xlsx tối giản theo chuẩn OpenXML (SpreadsheetML) — không cần thư viện ngoài.
/// Có: tiêu đề gộp ô, header tô màu, cố định dòng header, AutoFilter, định dạng số/ngày, độ rộng cột.
/// Chuỗi ghi inline (không dùng sharedStrings) để đơn giản; đủ nhanh cho vài chục nghìn dòng.
/// </summary>
public static class XlsxWriter
{
    private const string NS_MAIN = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string NS_REL = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string NS_PKG_REL = "http://schemas.openxmlformats.org/package/2006/relationships";

    // Chỉ số cellXfs trong styles.xml (giữ đúng thứ tự khi sửa BuildStyles).
    private const int STYLE_TITLE = 1;
    private const int STYLE_SUBTITLE = 2;
    private const int STYLE_HEADER = 3;
    private const int STYLE_TEXT = 4;
    private const int STYLE_WEIGHT = 5;
    private const int STYLE_INTEGER = 6;
    private const int STYLE_DECIMAL2 = 7;
    private const int STYLE_DECIMAL4 = 8;
    private const int STYLE_DATETIME = 9;

    private static readonly DateTime ExcelEpoch = new(1899, 12, 30, 0, 0, 0, DateTimeKind.Unspecified);

    public static void Write(string path, IReadOnlyList<XlsxSheet> sheets)
    {
        if (sheets.Count == 0) throw new ArgumentException("Cần ít nhất một sheet.", nameof(sheets));

        // Ghi ra tệp tạm rồi thay thế — không để lại tệp hỏng nếu lỗi giữa chừng.
        var temp = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var names = UniqueSheetNames(sheets);

                AddEntry(zip, "[Content_Types].xml", w => WriteContentTypes(w, sheets.Count));
                AddEntry(zip, "_rels/.rels", WriteRootRels);
                AddEntry(zip, "xl/workbook.xml", w => WriteWorkbook(w, sheets, names));
                AddEntry(zip, "xl/_rels/workbook.xml.rels", w => WriteWorkbookRels(w, sheets.Count));
                AddEntry(zip, "xl/styles.xml", WriteStyles);

                for (var i = 0; i < sheets.Count; i++)
                {
                    var sheet = sheets[i];
                    AddEntry(zip, $"xl/worksheets/sheet{i + 1}.xml", w => WriteSheet(w, sheet));
                }
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    // ───────────────────────── Package parts ─────────────────────────

    private static void AddEntry(ZipArchive zip, string name, Action<XmlWriter> write)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false
        });

        writer.WriteStartDocument(true);
        write(writer);
        writer.WriteEndDocument();
    }

    private static void WriteContentTypes(XmlWriter w, int sheetCount)
    {
        const string ns = "http://schemas.openxmlformats.org/package/2006/content-types";
        w.WriteStartElement("Types", ns);

        Default(w, ns, "rels", "application/vnd.openxmlformats-package.relationships+xml");
        Default(w, ns, "xml", "application/xml");

        Override(w, ns, "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
        Override(w, ns, "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
        for (var i = 1; i <= sheetCount; i++)
            Override(w, ns, $"/xl/worksheets/sheet{i}.xml",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");

        w.WriteEndElement();

        static void Default(XmlWriter w, string ns, string ext, string type)
        {
            w.WriteStartElement("Default", ns);
            w.WriteAttributeString("Extension", ext);
            w.WriteAttributeString("ContentType", type);
            w.WriteEndElement();
        }

        static void Override(XmlWriter w, string ns, string part, string type)
        {
            w.WriteStartElement("Override", ns);
            w.WriteAttributeString("PartName", part);
            w.WriteAttributeString("ContentType", type);
            w.WriteEndElement();
        }
    }

    private static void WriteRootRels(XmlWriter w)
    {
        w.WriteStartElement("Relationships", NS_PKG_REL);
        w.WriteStartElement("Relationship", NS_PKG_REL);
        w.WriteAttributeString("Id", "rId1");
        w.WriteAttributeString("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument");
        w.WriteAttributeString("Target", "xl/workbook.xml");
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void WriteWorkbook(XmlWriter w, IReadOnlyList<XlsxSheet> sheets, IReadOnlyList<string> names)
    {
        w.WriteStartElement("workbook", NS_MAIN);
        w.WriteAttributeString("xmlns", "r", null, NS_REL);

        w.WriteStartElement("sheets", NS_MAIN);
        for (var i = 0; i < sheets.Count; i++)
        {
            w.WriteStartElement("sheet", NS_MAIN);
            w.WriteAttributeString("name", names[i]);
            w.WriteAttributeString("sheetId", (i + 1).ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("id", NS_REL, $"rId{i + 1}");
            w.WriteEndElement();
        }
        w.WriteEndElement();

        // Vùng AutoFilter cần defined name ẩn để Excel nhận đúng.
        w.WriteStartElement("definedNames", NS_MAIN);
        for (var i = 0; i < sheets.Count; i++)
        {
            var layout = Layout.For(sheets[i]);
            w.WriteStartElement("definedName", NS_MAIN);
            w.WriteAttributeString("name", "_xlnm._FilterDatabase");
            w.WriteAttributeString("localSheetId", i.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("hidden", "1");
            var quoted = "'" + names[i].Replace("'", "''") + "'";
            w.WriteString($"{quoted}!${ColumnLetter(0)}${layout.HeaderRow}:${ColumnLetter(layout.ColumnCount - 1)}${layout.LastRow}");
            w.WriteEndElement();
        }
        w.WriteEndElement();

        w.WriteEndElement();
    }

    private static void WriteWorkbookRels(XmlWriter w, int sheetCount)
    {
        w.WriteStartElement("Relationships", NS_PKG_REL);
        for (var i = 1; i <= sheetCount; i++)
        {
            w.WriteStartElement("Relationship", NS_PKG_REL);
            w.WriteAttributeString("Id", $"rId{i}");
            w.WriteAttributeString("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet");
            w.WriteAttributeString("Target", $"worksheets/sheet{i}.xml");
            w.WriteEndElement();
        }

        w.WriteStartElement("Relationship", NS_PKG_REL);
        w.WriteAttributeString("Id", $"rId{sheetCount + 1}");
        w.WriteAttributeString("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles");
        w.WriteAttributeString("Target", "styles.xml");
        w.WriteEndElement();

        w.WriteEndElement();
    }

    /// <summary>Bảng style: font, fill, border, numFmt và cellXfs (thứ tự = hằng STYLE_*).</summary>
    private static void WriteStyles(XmlWriter w)
    {
        w.WriteStartElement("styleSheet", NS_MAIN);

        w.WriteStartElement("numFmts", NS_MAIN);
        w.WriteAttributeString("count", "3");
        NumFmt(w, 164, "#,##0.000");
        NumFmt(w, 165, "dd/mm/yyyy hh:mm");
        NumFmt(w, 166, "#,##0.0000");
        w.WriteEndElement();

        // Fonts: 0 thường, 1 tiêu đề, 2 phụ đề (nghiêng xám), 3 header (đậm trắng)
        w.WriteStartElement("fonts", NS_MAIN);
        w.WriteAttributeString("count", "4");
        Font(w, 11, bold: false, italic: false, color: null);
        Font(w, 15, bold: true, italic: false, color: "FF00853E");
        Font(w, 10, bold: false, italic: true, color: "FF64748B");
        Font(w, 11, bold: true, italic: false, color: "FFFFFFFF");
        w.WriteEndElement();

        // Fills: 0/1 bắt buộc theo chuẩn, 2 = xanh thương hiệu cho header
        w.WriteStartElement("fills", NS_MAIN);
        w.WriteAttributeString("count", "3");
        PatternFill(w, "none", null);
        PatternFill(w, "gray125", null);
        PatternFill(w, "solid", "FF00853E");
        w.WriteEndElement();

        // Borders: 0 không viền, 1 viền mảnh xám
        w.WriteStartElement("borders", NS_MAIN);
        w.WriteAttributeString("count", "2");
        Border(w, null);
        Border(w, "FFCBD5E1");
        w.WriteEndElement();

        w.WriteStartElement("cellStyleXfs", NS_MAIN);
        w.WriteAttributeString("count", "1");
        Xf(w, 0, 0, 0, 0, applyAlign: false, wrap: false, hAlign: null, includeXfId: false);
        w.WriteEndElement();

        w.WriteStartElement("cellXfs", NS_MAIN);
        w.WriteAttributeString("count", "10");
        Xf(w, 0, 0, 0, 0);                                            // 0 mặc định
        Xf(w, 0, 1, 0, 0);                                            // 1 tiêu đề
        Xf(w, 0, 2, 0, 0);                                            // 2 phụ đề
        Xf(w, 0, 3, 2, 1, applyAlign: true, wrap: true, hAlign: "center"); // 3 header
        Xf(w, 0, 0, 0, 1, applyAlign: true, wrap: false, hAlign: null);    // 4 text
        Xf(w, 164, 0, 0, 1);                                          // 5 weight
        Xf(w, 3, 0, 0, 1);                                            // 6 integer (#,##0)
        Xf(w, 4, 0, 0, 1);                                            // 7 #,##0.00
        Xf(w, 166, 0, 0, 1);                                          // 8 #,##0.0000
        Xf(w, 165, 0, 0, 1, applyAlign: true, wrap: false, hAlign: "center"); // 9 datetime
        w.WriteEndElement();

        w.WriteStartElement("cellStyles", NS_MAIN);
        w.WriteAttributeString("count", "1");
        w.WriteStartElement("cellStyle", NS_MAIN);
        w.WriteAttributeString("name", "Normal");
        w.WriteAttributeString("xfId", "0");
        w.WriteAttributeString("builtinId", "0");
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteEndElement();

        static void NumFmt(XmlWriter w, int id, string code)
        {
            w.WriteStartElement("numFmt", NS_MAIN);
            w.WriteAttributeString("numFmtId", id.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("formatCode", code);
            w.WriteEndElement();
        }

        static void Font(XmlWriter w, int size, bool bold, bool italic, string? color)
        {
            w.WriteStartElement("font", NS_MAIN);
            if (bold) w.WriteElementString("b", NS_MAIN, null);
            if (italic) w.WriteElementString("i", NS_MAIN, null);
            w.WriteStartElement("sz", NS_MAIN);
            w.WriteAttributeString("val", size.ToString(CultureInfo.InvariantCulture));
            w.WriteEndElement();
            if (color is not null)
            {
                w.WriteStartElement("color", NS_MAIN);
                w.WriteAttributeString("rgb", color);
                w.WriteEndElement();
            }
            w.WriteStartElement("name", NS_MAIN);
            w.WriteAttributeString("val", "Calibri");
            w.WriteEndElement();
            w.WriteEndElement();
        }

        static void PatternFill(XmlWriter w, string pattern, string? rgb)
        {
            w.WriteStartElement("fill", NS_MAIN);
            w.WriteStartElement("patternFill", NS_MAIN);
            w.WriteAttributeString("patternType", pattern);
            if (rgb is not null)
            {
                w.WriteStartElement("fgColor", NS_MAIN);
                w.WriteAttributeString("rgb", rgb);
                w.WriteEndElement();
                w.WriteStartElement("bgColor", NS_MAIN);
                w.WriteAttributeString("indexed", "64");
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndElement();
        }

        static void Border(XmlWriter w, string? rgb)
        {
            w.WriteStartElement("border", NS_MAIN);
            foreach (var side in new[] { "left", "right", "top", "bottom" })
            {
                w.WriteStartElement(side, NS_MAIN);
                if (rgb is not null)
                {
                    w.WriteAttributeString("style", "thin");
                    w.WriteStartElement("color", NS_MAIN);
                    w.WriteAttributeString("rgb", rgb);
                    w.WriteEndElement();
                }
                w.WriteEndElement();
            }
            w.WriteElementString("diagonal", NS_MAIN, null);
            w.WriteEndElement();
        }

        static void Xf(XmlWriter w, int numFmtId, int fontId, int fillId, int borderId,
            bool applyAlign = false, bool wrap = false, string? hAlign = null, bool includeXfId = true)
        {
            w.WriteStartElement("xf", NS_MAIN);
            w.WriteAttributeString("numFmtId", numFmtId.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("fontId", fontId.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("fillId", fillId.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("borderId", borderId.ToString(CultureInfo.InvariantCulture));
            if (includeXfId) w.WriteAttributeString("xfId", "0");
            if (numFmtId != 0) w.WriteAttributeString("applyNumberFormat", "1");
            if (fontId != 0) w.WriteAttributeString("applyFont", "1");
            if (fillId != 0) w.WriteAttributeString("applyFill", "1");
            if (borderId != 0) w.WriteAttributeString("applyBorder", "1");
            if (applyAlign)
            {
                w.WriteAttributeString("applyAlignment", "1");
                w.WriteStartElement("alignment", NS_MAIN);
                if (hAlign is not null) w.WriteAttributeString("horizontal", hAlign);
                w.WriteAttributeString("vertical", "center");
                if (wrap) w.WriteAttributeString("wrapText", "1");
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }
    }

    // ───────────────────────── Worksheet ─────────────────────────

    /// <summary>Vị trí các khối trong sheet (1-based row index).</summary>
    private readonly record struct Layout(int HeaderRow, int LastRow, int ColumnCount)
    {
        public static Layout For(XlsxSheet sheet)
        {
            // Dòng 1 tiêu đề, kế đến các phụ đề, một dòng trống, rồi header.
            var header = 1 + sheet.Subtitles.Count + 2;
            return new Layout(header, header + Math.Max(sheet.Rows.Count, 1), Math.Max(sheet.Columns.Count, 1));
        }
    }

    private static void WriteSheet(XmlWriter w, XlsxSheet sheet)
    {
        var layout = Layout.For(sheet);
        var lastCol = ColumnLetter(layout.ColumnCount - 1);

        w.WriteStartElement("worksheet", NS_MAIN);
        w.WriteAttributeString("xmlns", "r", null, NS_REL);

        // Cố định vùng tiêu đề + header khi cuộn.
        w.WriteStartElement("sheetViews", NS_MAIN);
        w.WriteStartElement("sheetView", NS_MAIN);
        w.WriteAttributeString("workbookViewId", "0");
        w.WriteAttributeString("showGridLines", "0");
        w.WriteStartElement("pane", NS_MAIN);
        w.WriteAttributeString("ySplit", layout.HeaderRow.ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("topLeftCell", $"A{layout.HeaderRow + 1}");
        w.WriteAttributeString("activePane", "bottomLeft");
        w.WriteAttributeString("state", "frozen");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteStartElement("cols", NS_MAIN);
        for (var c = 0; c < sheet.Columns.Count; c++)
        {
            w.WriteStartElement("col", NS_MAIN);
            w.WriteAttributeString("min", (c + 1).ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("max", (c + 1).ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("width", EstimateWidth(sheet, c).ToString("0.##", CultureInfo.InvariantCulture));
            w.WriteAttributeString("customWidth", "1");
            w.WriteEndElement();
        }
        w.WriteEndElement();

        w.WriteStartElement("sheetData", NS_MAIN);

        var rowIndex = 1;
        WriteRow(w, rowIndex++, [(sheet.Title, STYLE_TITLE)], height: 24);
        foreach (var subtitle in sheet.Subtitles)
            WriteRow(w, rowIndex++, [(subtitle, STYLE_SUBTITLE)]);
        rowIndex++; // dòng trống

        WriteRow(w, rowIndex++, sheet.Columns.Select(c => ((object?)c.Header, STYLE_HEADER)).ToArray(), height: 32);

        foreach (var values in sheet.Rows)
        {
            var cells = new (object?, int)[sheet.Columns.Count];
            for (var c = 0; c < sheet.Columns.Count; c++)
            {
                var value = c < values.Length ? values[c] : null;
                cells[c] = (value, StyleFor(sheet.Columns[c].Kind, value));
            }
            WriteRow(w, rowIndex++, cells);
        }

        w.WriteEndElement(); // sheetData

        w.WriteStartElement("autoFilter", NS_MAIN);
        w.WriteAttributeString("ref", $"A{layout.HeaderRow}:{lastCol}{layout.LastRow}");
        w.WriteEndElement();

        if (layout.ColumnCount > 1)
        {
            w.WriteStartElement("mergeCells", NS_MAIN);
            w.WriteAttributeString("count", (1 + sheet.Subtitles.Count).ToString(CultureInfo.InvariantCulture));
            for (var r = 1; r <= 1 + sheet.Subtitles.Count; r++)
            {
                w.WriteStartElement("mergeCell", NS_MAIN);
                w.WriteAttributeString("ref", $"A{r}:{lastCol}{r}");
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }

        w.WriteStartElement("pageMargins", NS_MAIN);
        w.WriteAttributeString("left", "0.4");
        w.WriteAttributeString("right", "0.4");
        w.WriteAttributeString("top", "0.5");
        w.WriteAttributeString("bottom", "0.5");
        w.WriteAttributeString("header", "0.3");
        w.WriteAttributeString("footer", "0.3");
        w.WriteEndElement();

        // In ngang, vừa chiều rộng một trang.
        w.WriteStartElement("pageSetup", NS_MAIN);
        w.WriteAttributeString("orientation", "landscape");
        w.WriteAttributeString("paperSize", "9");
        w.WriteAttributeString("fitToWidth", "1");
        w.WriteAttributeString("fitToHeight", "0");
        w.WriteEndElement();

        w.WriteEndElement(); // worksheet
    }

    private static int StyleFor(XlsxCellKind kind, object? value)
    {
        if (value is null || value is string) return STYLE_TEXT;

        return kind switch
        {
            XlsxCellKind.Weight => STYLE_WEIGHT,
            XlsxCellKind.Integer => STYLE_INTEGER,
            XlsxCellKind.Decimal2 => STYLE_DECIMAL2,
            XlsxCellKind.Decimal4 => STYLE_DECIMAL4,
            XlsxCellKind.DateTime => STYLE_DATETIME,
            _ => STYLE_TEXT
        };
    }

    private static void WriteRow(XmlWriter w, int rowIndex, (object? Value, int Style)[] cells, double? height = null)
    {
        w.WriteStartElement("row", NS_MAIN);
        w.WriteAttributeString("r", rowIndex.ToString(CultureInfo.InvariantCulture));
        if (height is { } h)
        {
            w.WriteAttributeString("ht", h.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("customHeight", "1");
        }

        for (var c = 0; c < cells.Length; c++)
        {
            var (value, style) = cells[c];
            var reference = $"{ColumnLetter(c)}{rowIndex}";

            w.WriteStartElement("c", NS_MAIN);
            w.WriteAttributeString("r", reference);
            w.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));

            switch (value)
            {
                case null:
                    break;

                case decimal d:
                    w.WriteElementString("v", NS_MAIN, d.ToString(CultureInfo.InvariantCulture));
                    break;

                case int or long or short:
                    w.WriteElementString("v", NS_MAIN, Convert.ToInt64(value, CultureInfo.InvariantCulture)
                        .ToString(CultureInfo.InvariantCulture));
                    break;

                case double or float:
                    w.WriteElementString("v", NS_MAIN, Convert.ToDouble(value, CultureInfo.InvariantCulture)
                        .ToString("R", CultureInfo.InvariantCulture));
                    break;

                case DateTime dt:
                    w.WriteElementString("v", NS_MAIN, ToSerial(dt).ToString("0.########", CultureInfo.InvariantCulture));
                    break;

                case DateTimeOffset dto:
                    // Giờ hiển thị theo múi giờ máy trạm (giờ nhà máy), giống lưới trên màn hình.
                    w.WriteElementString("v", NS_MAIN, ToSerial(dto.ToVnTime().DateTime)
                        .ToString("0.########", CultureInfo.InvariantCulture));
                    break;

                default:
                    w.WriteAttributeString("t", "inlineStr");
                    w.WriteStartElement("is", NS_MAIN);
                    w.WriteStartElement("t", NS_MAIN);
                    w.WriteAttributeString("xml", "space", null, "preserve");
                    w.WriteString(Sanitize(Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty));
                    w.WriteEndElement();
                    w.WriteEndElement();
                    break;
            }

            w.WriteEndElement();
        }

        w.WriteEndElement();
    }

    private static double ToSerial(DateTime dt)
        => (DateTime.SpecifyKind(dt, DateTimeKind.Unspecified) - ExcelEpoch).TotalDays;

    /// <summary>Độ rộng cột theo nội dung (mẫu tối đa 500 dòng), giới hạn 10–60 ký tự.</summary>
    private static double EstimateWidth(XlsxSheet sheet, int column)
    {
        if (sheet.Columns[column].Width is { } fixedWidth) return fixedWidth;

        var kind = sheet.Columns[column].Kind;
        var longest = Math.Min(sheet.Columns[column].Header.Length, 24);

        foreach (var row in sheet.Rows.Take(500))
        {
            if (column >= row.Length || row[column] is not { } value) continue;

            var length = value switch
            {
                DateTime or DateTimeOffset => 16,
                decimal d when kind == XlsxCellKind.Weight => d.ToString("N3", CultureInfo.InvariantCulture).Length,
                _ => Convert.ToString(value, CultureInfo.CurrentCulture)?.Length ?? 0
            };

            longest = Math.Max(longest, length);
        }

        return Math.Clamp(longest + 3, 10, 60);
    }

    /// <summary>0 → A, 25 → Z, 26 → AA.</summary>
    private static string ColumnLetter(int index)
    {
        var sb = new StringBuilder();
        index++;
        while (index > 0)
        {
            var rem = (index - 1) % 26;
            sb.Insert(0, (char)('A' + rem));
            index = (index - 1) / 26;
        }
        return sb.ToString();
    }

    /// <summary>Bỏ ký tự XML không hợp lệ (giữ nguyên emoji/cặp surrogate).</summary>
    private static string Sanitize(string text)
    {
        StringBuilder? sb = null;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (char.IsHighSurrogate(ch) && i + 1 < text.Length && XmlConvert.IsXmlSurrogatePair(text[i + 1], ch))
            {
                sb?.Append(ch).Append(text[i + 1]);
                i++;
                continue;
            }

            if (XmlConvert.IsXmlChar(ch))
            {
                sb?.Append(ch);
                continue;
            }

            sb ??= new StringBuilder(text, 0, i, text.Length);
        }

        return sb?.ToString() ?? text;
    }

    /// <summary>Tên sheet: tối đa 31 ký tự, không chứa []:*?/\ và không trùng nhau.</summary>
    private static List<string> UniqueSheetNames(IReadOnlyList<XlsxSheet> sheets)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(sheets.Count);

        foreach (var sheet in sheets)
        {
            var name = new string(sheet.Name.Where(c => "[]:*?/\\".IndexOf(c) < 0).ToArray()).Trim('\'', ' ');
            if (name.Length == 0) name = "Sheet";
            if (name.Length > 31) name = name[..31];

            var candidate = name;
            for (var n = 2; !used.Add(candidate); n++)
            {
                var suffix = $" ({n})";
                candidate = (name.Length + suffix.Length > 31 ? name[..(31 - suffix.Length)] : name) + suffix;
            }

            result.Add(candidate);
        }

        return result;
    }
}

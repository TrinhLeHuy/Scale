using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

Console.OutputEncoding = Encoding.UTF8;
var root = @"D:\Works\Scale";
var res = Path.Combine(root, "src", "TTC.Weighing.WinForms", "Localization", "Resources");
string[] langs = ["vi", "en", "lo", "km"];
var tables = langs.ToDictionary(l => l, l =>
    JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(res, $"strings.{l}.json")))!);

var mode = args.FirstOrDefault() ?? "summary";
var viChars = new Regex("[àáạảãâầấậẩẫăằắặẳẵèéẹẻẽêềếệểễìíịỉĩòóọỏõôồốộổỗơờớợởỡùúụủũưừứựửữỳýỵỷỹđÀÁẠẢÃÂẦẤẬẨẪĂẰẮẶẲẴÈÉẸẺẼÊỀẾỆỂỄÌÍỊỈĨÒÓỌỎÕÔỒỐỘỔỖƠỜỚỢỞỠÙÚỤỦŨƯỪỨỰỬỮỲÝỴỶỸĐ]");
var literal = new Regex("\\$?@?\"((?:[^\"\\\\\\r\\n]|\\\\.)*)\"");
var all = tables.Values.SelectMany(t => t.Keys).Distinct().OrderBy(k => k).ToList();

if (mode == "summary")
{
    Console.WriteLine($"TOTAL KEYS (union): {all.Count}");
    foreach (var l in langs)
    {
        var t = tables[l];
        var missing = all.Count(k => !t.ContainsKey(k));
        var todo = t.Values.Count(v => v.Contains("[TODO"));
        var same = l == "vi" ? 0 : t.Count(kv => tables["vi"].TryGetValue(kv.Key, out var vi) && vi == kv.Value && viChars.IsMatch(vi));
        var viInside = l == "vi" ? 0 : t.Values.Count(v => viChars.IsMatch(v));
        Console.WriteLine($"{l}: keys={t.Count} missing={missing} todo={todo} sameAsVi={same} containsVietnamese={viInside}");
    }
}

// Khóa dùng trong code
var csFiles = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
    .Where(f => !Regex.IsMatch(f, @"\\(bin|obj|Migrations)\\")).ToList();
var keyUse = new Regex("Loc\\.T\\(\\s*\"([^\"]+)\"");
var used = csFiles.SelectMany(f => keyUse.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value)).Distinct().ToList();
var undefinedKeys = used.Where(k => !tables["vi"].ContainsKey(k)).OrderBy(k => k).ToList();

if (mode == "summary")
{
    Console.WriteLine($"USED KEYS: {used.Count}  UNDEFINED IN vi: {undefinedKeys.Count}");
    foreach (var k in undefinedKeys.Take(60)) Console.WriteLine($"  undefined: {k}");
}

// Literal tiếng Việt hardcode
var skip = new Regex(@"Debug\.WriteLine|Log(Information|Warning|Error|Debug|Trace)\(|LogAsync\(|nameof\(|\[Description|Console\.Write");
var hits = new List<(string Project, string File, int Line, string Text)>();
foreach (var f in csFiles)
{
    var project = f.Substring(Path.Combine(root, "src").Length + 1).Split('\\')[0];
    var lines = File.ReadAllLines(f);
    var inBlock = false;
    for (var i = 0; i < lines.Length; i++)
    {
        var line = lines[i];
        var trimmed = line.TrimStart();
        if (inBlock) { if (trimmed.Contains("*/")) inBlock = false; continue; }
        if (trimmed.StartsWith("/*")) { if (!trimmed.Contains("*/")) inBlock = true; continue; }
        if (trimmed.StartsWith("//")) continue;
        var code = line;
        var idx = IndexOfLineComment(code);
        if (idx >= 0) code = code[..idx];
        if (skip.IsMatch(code)) continue;
        foreach (Match m in literal.Matches(code))
        {
            if (viChars.IsMatch(m.Groups[1].Value))
                hits.Add((project, Path.GetRelativePath(root, f), i + 1, m.Groups[1].Value));
        }
    }
}

if (mode == "summary")
{
    Console.WriteLine($"HARDCODED VI LITERALS: {hits.Count}");
    foreach (var g in hits.GroupBy(h => h.File).OrderByDescending(g => g.Count()))
        Console.WriteLine($"  {g.Count(),5}  {g.Key}");
}
else if (mode == "list")
{
    var filter = args.Length > 1 ? args[1] : "";
    foreach (var h in hits.Where(h => h.File.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        Console.WriteLine($"{h.File}:{h.Line}: {h.Text}");
}
else if (mode == "todo")
{
    var l = args[1];
    foreach (var k in all)
    {
        tables["vi"].TryGetValue(k, out var vi);
        tables["en"].TryGetValue(k, out var en);
        tables[l].TryGetValue(k, out var v);
        if (v is null || v.Contains("[TODO") || viChars.IsMatch(v))
            Console.WriteLine($"{k}\t{en ?? vi}");
    }
}

if (mode == "merge")
{
    // fragments/*.json : { "key": { "vi": "...", "en": "...", "lo": "...", "km": "..." } }
    var fragDir = Path.Combine(root, ".tmp-i18n", "fragments");
    var incoming = new Dictionary<string, Dictionary<string, string>>();
    var conflicts = new List<string>();
    foreach (var file in Directory.EnumerateFiles(fragDir, "*.json").OrderBy(f => f))
    {
        Dictionary<string, Dictionary<string, string>>? frag;
        try { frag = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(file)); }
        catch (Exception ex) { Console.WriteLine($"INVALID FRAGMENT {Path.GetFileName(file)}: {ex.Message}"); return; }
        foreach (var (key, values) in frag!)
        {
            if (incoming.TryGetValue(key, out var prev) && prev.TryGetValue("vi", out var pv) && values.TryGetValue("vi", out var nv) && pv != nv && tables["vi"].ContainsKey(key) == false)
                conflicts.Add($"{key} ({Path.GetFileName(file)}): '{pv}' vs '{nv}'");
            if (!incoming.TryGetValue(key, out var target)) incoming[key] = target = new();
            foreach (var (l, v) in values) if (!string.IsNullOrEmpty(v)) target[l] = v;
        }
    }
    foreach (var c in conflicts) Console.WriteLine("CONFLICT " + c);

    var opts = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    foreach (var l in langs)
    {
        // Giữ thứ tự cũ; khóa mới chèn sau khóa cuối cùng cùng tiền tố.
        var ordered = File.ReadAllText(Path.Combine(res, $"strings.{l}.json"));
        var list = JsonSerializer.Deserialize<List<KeyValuePair<string, string>>>(
            "[" + string.Join(",", JsonDocument.Parse(ordered).RootElement.EnumerateObject()
                .Select(p => JsonSerializer.Serialize(new { Key = p.Name, Value = p.Value.GetString() }))) + "]")!;
        var index = list.Select((kv, i) => (kv.Key, i)).ToDictionary(x => x.Key, x => x.i);
        var updated = 0; var added = 0;
        foreach (var (key, values) in incoming)
        {
            if (!values.TryGetValue(l, out var value)) continue;
            if (index.TryGetValue(key, out var pos))
            {
                if (list[pos].Value != value) { list[pos] = new(key, value); updated++; }
                continue;
            }
            var prefix = key.Contains('.') ? key[..key.IndexOf('.')] + "." : key;
            var insertAt = list.FindLastIndex(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal));
            insertAt = insertAt < 0 ? list.Count : insertAt + 1;
            list.Insert(insertAt, new(key, value));
            index = list.Select((kv, i) => (kv.Key, i)).ToDictionary(x => x.Key, x => x.i);
            added++;
        }
        var sb = new StringBuilder("{\n");
        for (var i = 0; i < list.Count; i++)
        {
            var prevPrefix = i > 0 ? list[i - 1].Key.Split('.')[0] : null;
            if (prevPrefix is not null && prevPrefix != list[i].Key.Split('.')[0]) sb.Append('\n');
            sb.Append("  ").Append(JsonSerializer.Serialize(list[i].Key, opts)).Append(": ")
              .Append(JsonSerializer.Serialize(list[i].Value, opts)).Append(i < list.Count - 1 ? ",\n" : "\n");
        }
        sb.Append("}\n");
        File.WriteAllText(Path.Combine(res, $"strings.{l}.json"), sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"{l}: updated={updated} added={added} total={list.Count}");
    }
}

static int IndexOfLineComment(string s)
{
    var inStr = false;
    for (var i = 0; i < s.Length - 1; i++)
    {
        if (s[i] == '"' && (i == 0 || s[i - 1] != '\\')) inStr = !inStr;
        if (!inStr && s[i] == '/' && s[i + 1] == '/') return i;
    }
    return -1;
}

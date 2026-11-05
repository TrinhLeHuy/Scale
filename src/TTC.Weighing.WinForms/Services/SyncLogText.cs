using TTC.Weighing.Infrastructure.Adapters;
using TTC.Weighing.WinForms.Localization;

namespace TTC.Weighing.WinForms.Services;

/// <summary>
/// Câu nhật ký đồng bộ ngắn gọn: "kéo được gì — cập nhật gì". Chỉ ghi khi có thay đổi (kéo liên tục mỗi vài giây).
/// Mỗi nhóm liệt kê tối đa <see cref="MAX_CODES"/> mã, còn lại "+N".
/// </summary>
public static class SyncLogText
{
    private const int MAX_CODES = 5;

    public static IEnumerable<string> Frm(FrmPullSummary s)
    {
        if (s.IsFirstPull)
        {
            yield return "✓ " + Loc.T("synclog.frm_first",
                Count(s, "LenhDon"), Count(s, "the-xe"), Count(s, "xe-van-chuyen"), Count(s, "hop-dong-van-chuyen"));
            yield break;
        }

        foreach (var g in s.Changes.GroupBy(c => (c.Entity, c.Kind)).OrderBy(g => g.Key.Entity).ThenBy(g => g.Key.Kind))
        {
            var kind = g.Key.Kind switch
            {
                FrmChangeKind.Added => Loc.T("synclog.added"),
                FrmChangeKind.Updated => Loc.T("synclog.updated"),
                _ => Loc.T("synclog.removed")
            };
            yield return $"{Icon(g.Key.Kind)} {kind} {g.Count()} {EntityName(g.Key.Entity)}: {Codes(g.Select(c => c.Code))}";
        }
    }

    public static IEnumerable<string> Erp(BackgroundDataSync.ErpPullSummary s)
    {
        if (s.IsFirstPull)
        {
            yield return "✓ " + Loc.T("synclog.erp_first", s.Total);
            yield break;
        }

        var added = s.Changes.Where(c => c.IsNew).Select(c => c.Code).ToList();
        var updated = s.Changes.Where(c => !c.IsNew).Select(c => c.Code).ToList();
        if (added.Count > 0) yield return $"＋ {Loc.T("synclog.added")} {added.Count} {Loc.T("synclog.e_erp")}: {Codes(added)}";
        if (updated.Count > 0) yield return $"✎ {Loc.T("synclog.updated")} {updated.Count} {Loc.T("synclog.e_erp")}: {Codes(updated)}";
    }

    private static int Count(FrmPullSummary s, string entity) => s.Counts.TryGetValue(entity, out var n) ? n : 0;

    private static string Icon(FrmChangeKind k) => k switch { FrmChangeKind.Added => "＋", FrmChangeKind.Updated => "✎", _ => "－" };

    private static string EntityName(string entity) => entity switch
    {
        "LenhDon" => Loc.T("synclog.e_lenhdon"),
        "the-xe" => Loc.T("synclog.e_card"),
        "xe-van-chuyen" => Loc.T("synclog.e_vehicle"),
        "hop-dong-van-chuyen" => Loc.T("synclog.e_contract"),
        _ => entity
    };

    private static string Codes(IEnumerable<string> codes)
    {
        var list = codes.Distinct().ToList();
        var shown = string.Join(", ", list.Take(MAX_CODES));
        return list.Count > MAX_CODES ? $"{shown} +{list.Count - MAX_CODES}" : shown;
    }
}

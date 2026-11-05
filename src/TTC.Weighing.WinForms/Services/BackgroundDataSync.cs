using Microsoft.Extensions.Configuration;
using TTC.Weighing.Infrastructure.Adapters;
using TTC.Weighing.Infrastructure.Integration;
using TTC.Weighing.Domain.Common;

namespace TTC.Weighing.WinForms.Services;

/// <summary>
/// Kéo dữ liệu FRM + ERP từ DigiScale liên tục sau khi đăng nhập: kéo xong → chờ
/// <c>BackgroundSync:IntervalSeconds</c> (mặc định 3s) → kéo tiếp. Không chạy chồng (vòng lặp tuần tự).
/// - FRM: <see cref="FrmMasterDataCache.RefreshAsync"/> — không đổi thì không ghi DB; đổi thì phát <c>Refreshed</c>.
/// - ERP: order-lines theo khung ngày + không lọc ngày (như màn Dữ liệu ERP) — không đổi thì không ghi DB;
///   đổi thì lưu DB và phát <see cref="ErpRefreshed"/>.
/// Lượt gọi thành công của luồng nền không ghi Nhật ký API (lỗi vẫn ghi) — tránh nhật ký phình mỗi vài giây.
/// </summary>
public sealed class BackgroundDataSync : IDisposable
{
    public static BackgroundDataSync? Current { get; private set; }

    // ── Nhật ký kéo nền: giữ từ lúc khởi động app (static — qua cả đăng xuất / đăng nhập lại) ──
    public enum SyncSource { Frm, Erp }

    public sealed record SyncLogEntry(DateTime At, SyncSource Source, string Text);

    private const int MAX_LOG_ENTRIES = 2000;
    private static readonly object LogLock = new();
    private static readonly List<SyncLogEntry> LogEntries = new();
    private static readonly List<(SyncSource Source, Action<SyncLogEntry> Handler)> LogListeners = new();
    private static bool _frmFirstLogged;

    /// <summary>
    /// Màn Dữ liệu FRM / ERP mở ra: nhận lịch sử kéo từ lúc khởi động app của nguồn tương ứng,
    /// và đăng ký nhận dòng mới (gọi từ luồng nền). Dispose kết quả khi đóng màn.
    /// </summary>
    public static IDisposable AttachLog(SyncSource source, Action<SyncLogEntry> onEntry, out IReadOnlyList<SyncLogEntry> history)
    {
        var listener = (source, onEntry);
        lock (LogLock)
        {
            history = LogEntries.Where(e => e.Source == source).ToList();
            LogListeners.Add(listener);
        }
        return new Detach(() => { lock (LogLock) LogListeners.Remove(listener); });
    }

    private sealed class Detach(Action action) : IDisposable
    {
        private Action? _action = action;
        public void Dispose() { _action?.Invoke(); _action = null; }
    }

    private static void Log(SyncSource source, IEnumerable<string> lines)
    {
        foreach (var line in lines) Log(source, line);
    }

    private static void Log(SyncSource source, string text)
    {
        var entry = new SyncLogEntry(VnTime.Now, source, text);
        List<Action<SyncLogEntry>> handlers;
        lock (LogLock)
        {
            LogEntries.Add(entry);
            if (LogEntries.Count > MAX_LOG_ENTRIES) LogEntries.RemoveRange(0, LogEntries.Count - MAX_LOG_ENTRIES);
            handlers = LogListeners.Where(l => l.Source == source).Select(l => l.Handler).ToList();
        }
        foreach (var h in handlers)
            try { h(entry); } catch { /* lỗi của màn nghe không dừng vòng kéo */ }
    }

    private string? _lastFrmError;
    private string? _lastErpError;

    /// <summary>Ghi lỗi một lần khi xuất hiện / đổi nội dung, và một dòng khi hết lỗi.</summary>
    private static void LogErrorTransition(SyncSource source, ref string? last, string? current)
    {
        if (current == last) return;
        Log(source, current is null ? "✓ " + TTC.Weighing.WinForms.Localization.Loc.T("synclog.recovered") : "⚠ " + current);
        last = current;
    }

    private readonly FrmMasterDataCache _frm;
    private readonly ErpOrderLineAdapter _erp;
    private readonly string _businessUnit;
    private readonly TimeSpan _interval;
    private readonly int _erpDaysBack;
    private readonly CancellationTokenSource _cts = new();
    private string? _erpFingerprint;
    private Dictionary<string, string>? _erpHashes;

    /// <summary>Lần kéo ERP có thay đổi gần nhất: tổng số dòng + đơn mới / đổi.</summary>
    public ErpPullSummary? LastErpPull { get; private set; }

    public sealed record ErpPullSummary(DateTime At, int Total, IReadOnlyList<(bool IsNew, string Code)> Changes, bool IsFirstPull);

    /// <summary>Dữ liệu ERP (order-lines) trong DB vừa đổi. Gọi từ luồng nền.</summary>
    public event Action? ErpRefreshed;

    /// <summary>Thời gian chờ giữa hai lượt kéo.</summary>
    public TimeSpan Interval => _interval;

    // ── Tắt / bật kéo tự động theo từng nguồn (nút ở màn Dữ liệu FRM / ERP). Giữ trong phiên chạy app. ──
    private static volatile bool _frmPaused, _erpPaused;

    /// <summary>Kéo tự động của nguồn đang tắt?</summary>
    public static bool IsPaused(SyncSource source) => source == SyncSource.Frm ? _frmPaused : _erpPaused;

    /// <summary>Tắt / bật kéo tự động của một nguồn. Nguồn còn lại vẫn chạy bình thường.</summary>
    public static void SetPaused(SyncSource source, bool paused)
    {
        if (IsPaused(source) == paused) return;
        if (source == SyncSource.Frm) _frmPaused = paused; else _erpPaused = paused;
        Log(source, (paused ? "⏸ " : "▶ ") + TTC.Weighing.WinForms.Localization.Loc.T(paused ? "synclog.paused" : "synclog.resumed"));
    }

    /// <summary>Đang chạy (đã đăng nhập, chưa dừng).</summary>
    public bool IsRunning => !_cts.IsCancellationRequested;

    /// <summary>
    /// Dòng trạng thái tự động cho màn Dữ liệu FRM / ERP: "Tự động kéo: ĐANG BẬT · mỗi 3 giây · lần cuối 09:38:16".
    /// <paramref name="lastAt"/> = lần kéo xong gần nhất của nguồn tương ứng.
    /// </summary>
    public static (string Text, bool Ok) StatusLine(Func<BackgroundDataSync, DateTime?> lastAt, SyncSource? source = null)
    {
        var s = Current;
        if (s is null || !s.IsRunning)
            return (TTC.Weighing.WinForms.Localization.Loc.T("sync.auto_off"), false);
        if (source is { } src && IsPaused(src))
            return (TTC.Weighing.WinForms.Localization.Loc.T("sync.auto_paused", lastAt(s)?.ToString("HH:mm:ss") ?? "—"), false);

        var at = lastAt(s)?.ToString("HH:mm:ss") ?? "—";
        var text = TTC.Weighing.WinForms.Localization.Loc.T("sync.auto_on", s.Interval.TotalSeconds.ToString("0"), at);
        if (s.LastError is { } err) text += " · " + TTC.Weighing.WinForms.Localization.Loc.T("sync.auto_error", err);
        return (text, s.LastError is null);
    }

    public DateTime? LastFrmAt { get; private set; }
    public DateTime? LastErpAt { get; private set; }
    public string? LastError { get; private set; }

    private BackgroundDataSync(FrmMasterDataCache frm, ErpOrderLineAdapter erp, string businessUnit, IConfiguration? cfg)
    {
        _frm = frm;
        _erp = erp;
        _businessUnit = businessUnit;
        var seconds = cfg?.GetValue<int?>("BackgroundSync:IntervalSeconds") ?? 3;
        _interval = TimeSpan.FromSeconds(Math.Max(1, seconds));
        _erpDaysBack = Math.Max(1, cfg?.GetValue<int?>("BackgroundSync:ErpDaysBack") ?? 7);
    }

    /// <summary>Bắt đầu (thay vòng lặp cũ nếu có — ví dụ đăng xuất rồi đăng nhập lại).</summary>
    public static BackgroundDataSync Start(FrmMasterDataCache frm, ErpOrderLineAdapter erp, string businessUnit,
        IConfiguration? cfg)
    {
        Current?.Dispose();
        var sync = new BackgroundDataSync(frm, erp, businessUnit, cfg);
        Current = sync;
        _ = Task.Run(() => sync.LoopAsync(sync._cts.Token));
        return sync;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        ApiCallLogContext.QuietSuccess = true; // chỉ luồng nền này (AsyncLocal)

        var started = TTC.Weighing.WinForms.Localization.Loc.T("synclog.started", _interval.TotalSeconds.ToString("0"));
        Log(SyncSource.Frm, "⟳ " + started);
        Log(SyncSource.Erp, "⟳ " + started);

        while (!ct.IsCancellationRequested)
        {
            string? frmError = null, erpError = null;

            if (!_frmPaused)
            try
            {
                var before = _frm.LastPull;
                await _frm.RefreshAsync(ct);
                LastFrmAt = VnTime.Now;
                if (_frm.LastRefreshErrors.Count > 0) frmError = string.Join("; ", _frm.LastRefreshErrors);

                // Nhật ký: chỉ ghi khi kéo được / có thay đổi. Dòng tổng lần đầu chỉ ghi một lần.
                if (_frm.LastPull is { } pull && !ReferenceEquals(pull, before) && (!pull.IsFirstPull || !_frmFirstLogged))
                {
                    if (pull.IsFirstPull) _frmFirstLogged = true;
                    Log(SyncSource.Frm, SyncLogText.Frm(pull));
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { frmError = ex.Message; }

            if (!_erpPaused)
            try
            {
                await PullErpAsync(ct);
                LastErpAt = VnTime.Now;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { erpError = ex.Message; }

            LogErrorTransition(SyncSource.Frm, ref _lastFrmError, frmError);
            LogErrorTransition(SyncSource.Erp, ref _lastErpError, erpError);

            LastError = (frmError, erpError) switch
            {
                (null, null) => null,
                (not null, null) => "FRM: " + frmError,
                (null, not null) => "ERP: " + erpError,
                _ => "FRM: " + frmError + " | ERP: " + erpError
            };

            try { await Task.Delay(_interval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task PullErpAsync(CancellationToken ct)
    {
        var from = VnTime.Today.AddDays(-_erpDaysBack);
        var to = VnTime.Today.AddDays(1);

        var all = new List<ErpOrderLine>();
        var seen = new HashSet<string>();
        foreach (var item in await _erp.GetAllOrderLinesAsync(_businessUnit, from, to, null, ct))
            if (seen.Add(item.StorageKey)) all.Add(item);
        // Không lọc ngày: PO / dòng chưa có transactionDate.
        foreach (var item in await _erp.GetAllOrderLinesAsync(_businessUnit, null, null, null, ct))
            if (seen.Add(item.StorageKey)) all.Add(item);

        if (all.Count == 0) return;

        var fingerprint = Fingerprint(all);
        if (fingerprint == _erpFingerprint) return; // không đổi → không ghi DB, không báo

        // Tóm tắt cho nhật ký: đơn mới / đổi so với lần kéo trước (theo ID bản ghi DigiScale).
        var current = all.ToDictionary(i => i.StorageKey,
            i => (Hash: System.Text.Json.JsonSerializer.Serialize(i), Code: i.TripNumber ?? i.ShipmentNumber ?? i.StorageKey));
        var changes = new List<(bool IsNew, string Code)>();
        if (_erpHashes is { } prev)
            foreach (var (key, v) in current)
            {
                if (!prev.TryGetValue(key, out var old)) changes.Add((true, v.Code));
                else if (old != v.Hash) changes.Add((false, v.Code));
            }
        LastErpPull = new ErpPullSummary(VnTime.Now, all.Count, changes, IsFirstPull: _erpHashes is null);
        _erpHashes = current.ToDictionary(kv => kv.Key, kv => kv.Value.Hash);

        await _erp.SaveToDbAsync(all);
        Log(SyncSource.Erp, SyncLogText.Erp(LastErpPull));
        _erpFingerprint = fingerprint;
        try { ErpRefreshed?.Invoke(); } catch { /* lỗi của màn nghe sự kiện không dừng vòng kéo */ }
    }

    private static string Fingerprint(IEnumerable<ErpOrderLine> items)
    {
        using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (var it in items.OrderBy(i => i.StorageKey, StringComparer.Ordinal))
            sha.AppendData(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(it) + "\n"));
        return Convert.ToHexString(sha.GetHashAndReset());
    }

    public void Dispose()
    {
        if (!_cts.IsCancellationRequested) _cts.Cancel();
        _cts.Dispose();
        if (ReferenceEquals(Current, this)) Current = null;
    }
}

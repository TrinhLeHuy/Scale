namespace TTC.Weighing.WinForms.Controls;

/// <summary>
/// Gom ký tự bàn phím thành MỘT mã quét. Logic thuần (không phụ thuộc WinForms) để kiểm chứng được bằng thời gian giả lập.
/// <para>
/// Mọi thời gian là thời điểm PHÍM ĐƯỢC GÕ (ms, cùng gốc <c>GetTickCount</c>), không phải lúc app xử lý.
/// </para>
/// <list type="bullet">
/// <item>Máy quét: ≥ <see cref="MinFastRun"/> ký tự liên tiếp, mỗi ký tự cách nhau ≤ <see cref="MaxGapMs"/> ms.</item>
/// <item>Máy quét gửi nhiều đợt (ngắt ≤ <see cref="JoinChunkMs"/> ms) hoặc có Enter/Tab giữa chừng → vẫn là MỘT mã
///   (mọi mã QR của hệ thống cân là một dòng: số đăng tài, biển số, mã lệnh đốn).</item>
/// <item>Quét xong khi: đã có Enter/Tab và im ≥ <see cref="SettleAfterTerminatorMs"/> ms, hoặc
///   không có Enter và im ≥ <see cref="IdleCompleteMs"/> ms.</item>
/// </list>
/// </summary>
internal sealed class ScanAccumulator
{
    public int MaxGapMs { get; init; } = 50;
    public int MinFastRun { get; init; } = 4;
    public int MinLength { get; init; } = 4;
    public int JoinChunkMs { get; init; } = 500;
    public int IdleCompleteMs { get; init; } = 500;
    public int SettleAfterTerminatorMs { get; init; } = 150;

    private readonly System.Text.StringBuilder _buf = new();
    private int? _last;
    private int _fastRun;
    private bool _terminated;

    /// <summary>Đang gom dở (có ký tự chưa kết luận).</summary>
    public bool Active => _buf.Length > 0;

    /// <summary>Chuỗi đang gom đã xác định là của máy quét.</summary>
    public bool ScannerLike { get; private set; }

    /// <summary>Thông tin chẩn đoán của chuỗi đang gom.</summary>
    public int Chunks { get; private set; }
    public int MaxObservedGapMs { get; private set; }
    public bool SawTerminator => _terminated;

    /// <summary>Nhận một ký tự in được. Trả true nếu ký tự này bắt đầu một chuỗi MỚI (chuỗi cũ bị bỏ).</summary>
    public bool OnChar(char ch, int time)
    {
        var gap = _last is { } last ? unchecked(time - last) : int.MaxValue;
        var startedNew = false;

        if (_last is not null && gap <= MaxGapMs && !_terminated)
        {
            _fastRun++;                                       // cùng đợt gõ nhanh
        }
        else if (ScannerLike && gap <= JoinChunkMs)
        {
            _fastRun = 1;                                     // đợt tiếp theo của cùng mã → nối
            _terminated = false;                              // Enter/Tab giữa chừng chỉ là ngăn cách
            Chunks++;
        }
        else
        {
            Reset();                                          // người gõ tay / chuỗi mới
            _fastRun = 1;
            Chunks = 1;
            startedNew = true;
        }

        if (_last is not null && gap != int.MaxValue && !startedNew)
            MaxObservedGapMs = Math.Max(MaxObservedGapMs, gap);

        _buf.Append(ch);
        _last = time;
        if (_fastRun >= MinFastRun) ScannerLike = true;
        return startedNew;
    }

    /// <summary>
    /// Nhận Enter/Tab. Trả true nếu là của máy quét (chặn phím, không để bấm nút / nhảy ô);
    /// false = người dùng bấm, để phím đi tiếp như bình thường.
    /// </summary>
    public bool OnTerminator(int time)
    {
        if (!ScannerLike || _last is not { } last || unchecked(time - last) > JoinChunkMs) return false;
        _terminated = true;
        _last = time;
        return true;
    }

    /// <summary>
    /// Gọi định kỳ khi KHÔNG còn phím chờ xử lý. Trả mã khi quét xong; null nếu chưa xong
    /// hoặc chuỗi là người gõ tay (khi đó bỏ chuỗi, ký tự đã vào ô như thường).
    /// </summary>
    public string? TryComplete(int now, out bool finished)
    {
        finished = false;
        if (_last is not { } last) return null;

        var need = _terminated ? SettleAfterTerminatorMs : IdleCompleteMs;
        if (unchecked(now - last) < need) return null;

        finished = true;
        var code = _buf.ToString().Trim();
        var isScan = ScannerLike && code.Length >= MinLength;
        Reset();
        return isScan ? code : null;
    }

    public void Reset()
    {
        _buf.Clear();
        _last = null;
        _fastRun = 0;
        _terminated = false;
        ScannerLike = false;
        Chunks = 0;
        MaxObservedGapMs = 0;
    }
}

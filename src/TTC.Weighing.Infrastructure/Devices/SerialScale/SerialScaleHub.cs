using System.IO.Ports;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Domain.Enums;

namespace TTC.Weighing.Infrastructure.Devices.SerialScale;

/// <summary>
/// Đọc đầu cân qua cổng COM (RS-232C) cho máy trạm.
/// <list type="bullet">
/// <item>Mỗi cổng COM mở một lần; nhiều thiết bị dùng chung cổng (ví dụ Xe vào / Xe ra cùng một đầu cân) đọc cùng dòng.</item>
/// <item>Cổng lỗi / rút cáp → tự mở lại sau <see cref="RetryInterval"/>.</item>
/// <item>Cấu hình lưu theo máy: <c>%LocalAppData%\TTC.Weighing\scale-connections.json</c>.</item>
/// </list>
/// Không tự bịa số: chưa đọc được thì <see cref="ScaleLiveReading.Kg"/> = null.
/// </summary>
public sealed class SerialScaleHub : IScaleConnectionService, IDisposable
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);

    /// <summary>Số không đổi liên tục bao lâu thì coi là ổn định khi đầu cân không gửi cờ ổn định.</summary>
    private static readonly TimeSpan GenericStableAfter = TimeSpan.FromSeconds(1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _storePath;
    private readonly IClock _clock;
    private readonly ILogger<SerialScaleHub> _logger;
    private readonly object _lock = new();
    private readonly Dictionary<string, PortReader> _ports = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DeviceState> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _retryTimer;
    private List<ScaleConnectionSettings> _settings = [];
    private bool _disposed;

    public SerialScaleHub(string storePath, IClock clock, ILogger<SerialScaleHub> logger)
    {
        _storePath = storePath;
        _clock = clock;
        _logger = logger;
        _settings = Load();
        _retryTimer = new Timer(_ => RetryClosedPorts(), null, RetryInterval, RetryInterval);
        // Mở cổng ngoài luồng gọi (giao diện) — cổng USB-COM có thể mất vài trăm ms.
        ThreadPool.QueueUserWorkItem(_ => Apply());
    }

    public IReadOnlyList<ScaleConnectionSettings> GetSettings()
    {
        lock (_lock) return [.. _settings];
    }

    public void Save(IReadOnlyList<ScaleConnectionSettings> settings)
    {
        lock (_lock) _settings = [.. settings];

        Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
        File.WriteAllText(_storePath, JsonSerializer.Serialize(settings, JsonOptions), new UTF8Encoding(false));
        _logger.LogInformation("Scale connections saved: {Settings}",
            string.Join("; ", settings.Select(s => $"{s.Key}={(s.Enabled ? s.PortName : "off")}/{s.BaudRate}/{s.DataBits}{s.Parity[0]}{s.StopBits}/x{s.Factor}")));

        Apply();
    }

    public void Reconnect() => ReconnectAll();

    public ScaleConnectionSettings? FindEnabled(Guid weighbridgeId)
    {
        lock (_lock)
            return _settings.FirstOrDefault(s => s.Enabled && s.WeighbridgeId == weighbridgeId && !string.IsNullOrEmpty(s.PortName));
    }

    public ScaleConnectionSettings? FindEnabled(string key)
    {
        lock (_lock)
            return _settings.FirstOrDefault(s => s.Enabled && string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(s.PortName));
    }

    public IReadOnlyList<string> GetPortNames()
    {
        try
        {
            return SerialPort.GetPortNames().Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p.Length).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cannot list COM ports");
            return [];
        }
    }

    public ScaleLiveReading GetLive(string key)
    {
        lock (_lock)
        {
            var settings = _settings.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
            if (settings is null || string.IsNullOrEmpty(settings.PortName))
                return new ScaleLiveReading { Error = "NOT_CONFIGURED" };
            if (!settings.Enabled)
                return new ScaleLiveReading { Error = "DISABLED" };

            _ports.TryGetValue(settings.PortName, out var port);
            _devices.TryGetValue(key, out var device);
            var now = _clock.UtcNow;

            var fresh = device?.ReceivedAt is { } at && now - at <= TimeSpan.FromMilliseconds(Math.Max(500, settings.StaleAfterMs));
            var stableDuration = fresh && device!.StableSince is { } since && device.StableFlag != false ? now - since : TimeSpan.Zero;

            return new ScaleLiveReading
            {
                PortOpen = port?.IsOpen == true,
                Kg = device?.Kg,
                RawValue = device?.RawValue,
                RawUnit = device?.RawUnit,
                StableFlag = device?.StableFlag,
                StableDuration = stableDuration,
                IsStable = fresh && (device!.StableFlag ?? stableDuration >= GenericStableAfter),
                ObservedDeltaKg = device?.LastDeltaKg,
                Status = !fresh ? ScaleDeviceStatus.Unknown
                    : device!.Overload ? ScaleDeviceStatus.Overload
                    : ScaleDeviceStatus.Ok,
                LastLine = port?.LastLine,
                ReceivedAt = device?.ReceivedAt ?? port?.LastLineAt,
                Fresh = fresh,
                Error = port?.Error ?? device?.Error,
                ErrorDetail = port?.ErrorDetail
            };
        }
    }

    // ── Tự dò ──

    /// <summary>
    /// Thứ tự thử: 9600 / 8N1 trước (cấu hình phần mềm cân cũ của nhà máy), rồi các tốc độ khác; mỗi tốc độ thử 8N1, 7E1 (A&amp;D), 7O1.
    /// </summary>
    private static readonly int[] ProbeBauds = [9600, 2400, 4800, 19200, 1200];
    private static readonly (int DataBits, string Parity, int StopBits)[] ProbeFrames =
        [(8, "None", 1), (7, "Even", 1), (7, "Odd", 1)];
    private static readonly TimeSpan ProbeListen = TimeSpan.FromMilliseconds(1300);

    public async Task<ScaleProbeResult?> ProbeAsync(
        IReadOnlyList<string> portNames, IProgress<string>? progress, CancellationToken ct)
    {
        // Cổng hub đang mở (đã cấu hình cho thiết bị khác) thì không đụng tới.
        HashSet<string> busy;
        lock (_lock)
            busy = _ports.Where(p => p.Value.IsOpen).Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var portName in portNames.Where(p => !busy.Contains(p)))
        {
            foreach (var baud in ProbeBauds)
            {
                foreach (var (dataBits, parity, stopBits) in ProbeFrames)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report($"{portName} · {baud} · {dataBits}{parity[0]}{stopBits}");

                    var text = await ListenAsync(portName, baud, dataBits, parity, stopBits, ct);
                    if (text is null) goto nextPort; // không mở được cổng → bỏ cả cổng
                    if (Evaluate(text) is { } hit)
                    {
                        _logger.LogInformation("Scale probe found {Port} {Baud}/{Data}{Parity}{Stop} {Format}: {Line}",
                            portName, baud, dataBits, parity, stopBits, hit.Format, hit.Line);
                        return new ScaleProbeResult(portName, baud, dataBits, parity, stopBits,
                            hit.Format, hit.Line, hit.Frame.Value, hit.Frame.Unit);
                    }
                }
            }
            nextPort:;
        }

        return null;
    }

    /// <summary>Mở cổng, chỉ nghe trong <see cref="ProbeListen"/>. Null khi không mở được cổng.</summary>
    private async Task<string?> ListenAsync(string portName, int baud, int dataBits, string parity, int stopBits, CancellationToken ct)
    {
        SerialPort port;
        try
        {
            port = new SerialPort(portName, baud, Enum.Parse<Parity>(parity), dataBits,
                stopBits == 2 ? StopBits.Two : StopBits.One)
            { Encoding = Encoding.ASCII, DtrEnable = true, RtsEnable = true, ReadTimeout = 200 };
            port.Open();
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Probe: cannot open {Port}: {Error}", portName, ex.Message);
            return null;
        }

        var buffer = new StringBuilder();
        try
        {
            var until = DateTime.UtcNow + ProbeListen;
            while (DateTime.UtcNow < until && buffer.Length < 2048)
            {
                await Task.Delay(100, ct);
                if (port.BytesToRead > 0) buffer.Append(ReadMasked(port));
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogInformation("Probe: read error {Port}: {Error}", portName, ex.Message);
        }
        finally
        {
            try { port.Close(); } catch { /* bỏ qua */ }
            port.Dispose();
        }

        return buffer.ToString();
    }

    /// <summary>Đạt khi có ít nhất 2 dòng đọc ra số và chuỗi gần như toàn ký tự in được (sai tốc độ → ký tự rác).</summary>
    private static (ScaleFrameFormat Format, string Line, ScaleFrame Frame)? Evaluate(string text)
    {
        if (text.Length < 6) return null;
        var printable = text.Count(c => c is '\r' or '\n' or '\u0002' or '\u0003' || (c >= ' ' && c <= '~'));
        if (printable < text.Length * 0.95) return null;

        var lines = text.Split(['\r', '\n', '\u0003'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Trim('\u0002', ' ')).Where(l => l.Length >= 3).ToList();
        // Bỏ dòng đầu: có thể bị cắt giữa chừng lúc mở cổng.
        if (lines.Count > 2) lines = lines.Skip(1).ToList();

        var and = lines.Select(l => (Line: l, Frame: ScaleFrameParser.Parse(l, ScaleFrameFormat.AndStandard)))
            .Where(x => x.Frame is { Stable: not null } || x.Frame is { Overload: true }).ToList();
        if (and.Count >= 2) return (ScaleFrameFormat.AndStandard, and[^1].Line, and[^1].Frame!);

        // Kiểu phần mềm cân cũ: cụm số có dấu "+0000140" (Toledo, Keli, A12...).
        var signed = lines.Where(l => l.Contains('+') || l.Contains('-'))
            .Select(l => (Line: l, Frame: ScaleFrameParser.Parse(l, ScaleFrameFormat.SignedDigits)))
            .Where(x => x.Frame is not null).ToList();
        if (signed.Count >= 2) return (ScaleFrameFormat.SignedDigits, signed[^1].Line, signed[^1].Frame!);

        var generic = lines.Select(l => (Line: l, Frame: ScaleFrameParser.Parse(l, ScaleFrameFormat.Generic)))
            .Where(x => x.Frame is not null).ToList();
        if (generic.Count >= 2) return (ScaleFrameFormat.Generic, generic[^1].Line, generic[^1].Frame!);

        return null;
    }

    /// <summary>
    /// Đọc byte thô và bỏ bit thứ 8 (giống phần mềm cân cũ CanDienTu): đầu cân 7 bit + parity đọc ở 8N1
    /// gửi '1' (0x31) thành 0xB1. Encoding.ASCII sẽ biến các byte đó thành '?' → không tách được số.
    /// </summary>
    private static string ReadMasked(SerialPort port)
    {
        var n = port.BytesToRead;
        if (n <= 0) return string.Empty;
        var bytes = new byte[n];
        var read = port.Read(bytes, 0, n);
        var chars = new char[read];
        for (var i = 0; i < read; i++) chars[i] = (char)(bytes[i] & 0x7F);
        return new string(chars);
    }

    /// <summary>Định danh thiết bị cố định theo mã (ghi vào weigh_events.device_id).</summary>
    public static Guid DeviceIdFor(string key)
        => new(MD5.HashData(Encoding.UTF8.GetBytes("serial-scale:" + key.ToUpperInvariant())));

    // ── Kết nối ──

    /// <summary>
    /// Áp cấu hình: chỉ đóng / mở lại cổng có thay đổi (đổi cổng hoặc thông số truyền).
    /// Cổng không đổi giữ nguyên kết nối và số đang đọc — kênh cân còn lại không bị ngắt.
    /// </summary>
    private void Apply()
    {
        var toDispose = new List<PortReader>();
        var toOpen = new List<PortReader>();
        lock (_lock)
        {
            if (_disposed) return;

            var enabled = _settings.Where(s => s.Enabled && !string.IsNullOrWhiteSpace(s.PortName)).ToList();
            var groups = enabled.GroupBy(s => s.PortName!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            // Cùng một cổng phải cùng thông số truyền — khác nhau thì báo xung đột, không mở.
            static bool Conflict(List<ScaleConnectionSettings> g) => g.Any(s => s.BaudRate != g[0].BaudRate
                || s.DataBits != g[0].DataBits || s.Parity != g[0].Parity || s.StopBits != g[0].StopBits);

            foreach (var (name, reader) in _ports.ToList())
            {
                if (groups.TryGetValue(name, out var g) && !Conflict(g) && reader.Error != "PORT_CONFLICT" && reader.Matches(g[0]))
                    continue;
                _ports.Remove(name);
                toDispose.Add(reader);
            }

            foreach (var (name, g) in groups)
            {
                if (_ports.ContainsKey(name)) continue;
                var reader = new PortReader(name, g[0], OnLine, _logger);
                if (Conflict(g)) reader.SetError("PORT_CONFLICT", name);
                else toOpen.Add(reader);
                _ports[name] = reader;
            }

            // Giữ số đang đọc của thiết bị có cổng không đổi; thiết bị mở lại cổng thì bắt đầu lại.
            var reopened = toOpen.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var devices = new Dictionary<string, DeviceState>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in enabled)
                devices[s.Key] = !reopened.Contains(s.PortName!) && _devices.TryGetValue(s.Key, out var d) ? d : new DeviceState();
            _devices.Clear();
            foreach (var (k, d) in devices) _devices[k] = d;
        }

        // Đóng / mở NGOÀI khóa: SerialPort.Close chờ luồng DataReceived, mà luồng đó cần khóa → kẹt.
        // Đóng trước rồi mới mở: đổi thông số trên cùng một cổng.
        foreach (var port in toDispose) port.Dispose();
        foreach (var port in toOpen) port.TryOpen();
    }

    /// <summary>Đóng hết rồi mở lại mọi cổng (nút "Kết nối lại").</summary>
    private void ReconnectAll()
    {
        List<PortReader> old;
        lock (_lock)
        {
            old = [.. _ports.Values];
            _ports.Clear();
            _devices.Clear();
        }
        foreach (var port in old) port.Dispose();
        Apply();
    }

    private void RetryClosedPorts()
    {
        List<PortReader> closed;
        lock (_lock)
        {
            if (_disposed) return;
            closed = _ports.Values.Where(p => !p.IsOpen && p.Error != "PORT_CONFLICT").ToList();
        }

        foreach (var port in closed)
            port.TryOpen();
    }

    private void OnLine(string portName, string line)
    {
        lock (_lock)
        {
            var now = _clock.UtcNow;
            foreach (var s in _settings.Where(s => s.Enabled && string.Equals(s.PortName, portName, StringComparison.OrdinalIgnoreCase)))
            {
                if (!_devices.TryGetValue(s.Key, out var device)) continue;

                var frame = ScaleFrameParser.Parse(line, s.Format);
                if (frame is null)
                {
                    device.Error = "PARSE_ERROR";
                    continue;
                }

                device.Error = null;
                var kg = decimal.Round(ScaleFrameParser.ToKg(frame, s), 3);

                device.LastDeltaKg = device.Kg is { } prev ? Math.Abs(kg - prev) : null;
                if (device.Kg != kg || frame.Stable == false || device.StableSince is null)
                    device.StableSince = now;

                device.Kg = kg;
                device.RawValue = frame.Value;
                device.RawUnit = frame.Unit;
                device.StableFlag = frame.Stable;
                device.Overload = frame.Overload;
                device.ReceivedAt = now;
            }
        }
    }

    private List<ScaleConnectionSettings> Load()
    {
        try
        {
            if (!File.Exists(_storePath)) return [];
            return JsonSerializer.Deserialize<List<ScaleConnectionSettings>>(File.ReadAllText(_storePath), JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cannot read scale connection settings {Path}", _storePath);
            return [];
        }
    }

    public void Dispose()
    {
        List<PortReader> old;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            old = [.. _ports.Values];
            _ports.Clear();
        }
        _retryTimer.Dispose();
        foreach (var port in old) port.Dispose();
    }

    private sealed class DeviceState
    {
        public decimal? Kg;
        public decimal? RawValue;
        public string? RawUnit;
        public bool? StableFlag;
        public bool Overload;
        public DateTimeOffset? StableSince;
        public DateTimeOffset? ReceivedAt;
        public decimal? LastDeltaKg;
        public string? Error;
    }

    /// <summary>Một cổng COM: mở, nhận byte, cắt thành dòng theo CR / LF.</summary>
    private sealed class PortReader(
        string portName, ScaleConnectionSettings settings, Action<string, string> onLine, ILogger logger) : IDisposable
    {
        private readonly StringBuilder _buffer = new();
        private SerialPort? _port;

        public string? LastLine { get; private set; }
        public DateTimeOffset? LastLineAt { get; private set; }
        public string? Error { get; private set; }
        public string? ErrorDetail { get; private set; }
        public bool IsOpen => _port?.IsOpen == true;
        public string Name => portName;

        /// <summary>Cùng thông số truyền → giữ kết nối khi lưu lại cấu hình.</summary>
        public bool Matches(ScaleConnectionSettings s) => s.BaudRate == settings.BaudRate && s.DataBits == settings.DataBits
            && string.Equals(s.Parity, settings.Parity, StringComparison.OrdinalIgnoreCase) && s.StopBits == settings.StopBits;

        public void SetError(string? code, string? detail)
        {
            Error = code;
            ErrorDetail = detail;
        }

        public void TryOpen()
        {
            try
            {
                _port?.Dispose();
                _port = new SerialPort(portName, settings.BaudRate,
                    Enum.TryParse<Parity>(settings.Parity, true, out var parity) ? parity : Parity.None,
                    settings.DataBits,
                    settings.StopBits == 2 ? StopBits.Two : StopBits.One)
                {
                    Encoding = Encoding.ASCII,
                    ReadTimeout = 500,
                    // Nhiều đầu cân cần DTR/RTS bật mới gửi dữ liệu.
                    DtrEnable = true,
                    RtsEnable = true
                };
                _port.DataReceived += (_, _) => ReadAvailable();
                _port.ErrorReceived += (_, e) => logger.LogWarning("COM {Port} error {Error}", portName, e.EventType);
                _port.Open();
                SetError(null, null);
                logger.LogInformation("COM {Port} opened {Baud}/{Data}{Parity}{Stop}", portName,
                    settings.BaudRate, settings.DataBits, settings.Parity, settings.StopBits);
            }
            catch (UnauthorizedAccessException ex)
            {
                SetError("PORT_BUSY", ex.Message);
            }
            catch (IOException ex) when (!SerialPort.GetPortNames().Contains(portName, StringComparer.OrdinalIgnoreCase))
            {
                SetError("PORT_NOT_FOUND", ex.Message);
            }
            catch (Exception ex)
            {
                SetError("PORT_ERROR", ex.Message);
                logger.LogWarning(ex, "Cannot open {Port}", portName);
            }
        }

        private void ReadAvailable()
        {
            try
            {
                var port = _port;
                if (port is null || !port.IsOpen) return;
                var text = ReadMasked(port);

                foreach (var ch in text)
                {
                    if (ch is '\r' or '\n' or '\u0003')
                    {
                        if (_buffer.Length > 0)
                        {
                            var line = _buffer.ToString().Trim('\u0002', ' ');
                            _buffer.Clear();
                            if (line.Length == 0) continue;
                            LastLine = line.Length > 80 ? line[..80] : line;
                            LastLineAt = DateTimeOffset.UtcNow;
                            onLine(portName, line);
                        }
                    }
                    else if (_buffer.Length < 256)
                    {
                        _buffer.Append(ch);
                    }
                    else
                    {
                        _buffer.Clear(); // rác / sai tốc độ → bỏ
                    }
                }
            }
            catch (Exception ex)
            {
                SetError("PORT_ERROR", ex.Message);
                try { _port?.Close(); } catch { /* cổng đã mất */ }
            }
        }

        public void Dispose()
        {
            try { _port?.Close(); } catch { /* bỏ qua */ }
            _port?.Dispose();
            _port = null;
        }
    }
}

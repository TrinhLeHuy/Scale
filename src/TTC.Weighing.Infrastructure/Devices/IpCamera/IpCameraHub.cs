using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TTC.Weighing.Application.Abstractions;

namespace TTC.Weighing.Infrastructure.Devices.IpCamera;

/// <summary>
/// Kết nối camera IP qua TCP/IP: kiểm tra cổng HTTP, chụp ảnh qua HTTP (Basic/Digest), dựng URL RTSP.
/// Cấu hình lưu theo máy trạm (JSON), mật khẩu mã hóa DPAPI theo tài khoản Windows.
/// Mỗi camera = màn hình (Bàn cân / Đăng tài) + vị trí (Vào / Ra).
/// Chưa có cấu hình đã lưu cho một camera thì dùng giá trị mặc định từ appsettings (mục Cameras).
/// </summary>
public sealed class IpCameraHub : ICameraConnectionService, IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _storePath;
    private readonly ILogger<IpCameraHub> _logger;
    private readonly object _lock = new();
    private readonly Dictionary<string, CameraLiveState> _live = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HttpClient> _clients = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (CameraBrand Brand, string Path)> _resolvedPaths = new(StringComparer.Ordinal);
    private readonly Timer _checkTimer;
    private List<CameraConnectionSettings> _settings;
    private int _checking;
    private bool _disposed;

    public IpCameraHub(string storePath, IReadOnlyList<CameraConnectionSettings> defaults, ILogger<IpCameraHub> logger)
    {
        _storePath = storePath;
        _logger = logger;

        var saved = Load();
        _settings = [.. saved];
        foreach (var d in defaults)
            if (!_settings.Any(s => s.Is(d.Station, d.Position)))
                _settings.Add(d);

        _checkTimer = new Timer(_ => _ = CheckAllAsync(), null, TimeSpan.FromSeconds(1), CheckInterval);
    }

    private static string LiveKey(string station, string position) => $"{station}.{position}".ToUpperInvariant();

    public IReadOnlyList<CameraConnectionSettings> GetSettings()
    {
        lock (_lock) return [.. _settings];
    }

    public CameraConnectionSettings? FindEnabled(string station, string position)
    {
        lock (_lock)
            return _settings.FirstOrDefault(s => s.Enabled && !string.IsNullOrWhiteSpace(s.Host) && s.Is(station, position));
    }

    public void Save(IReadOnlyList<CameraConnectionSettings> settings)
    {
        List<HttpClient> oldClients;
        lock (_lock)
        {
            _settings = [.. settings];
            _live.Clear();
            oldClients = [.. _clients.Values];
            _clients.Clear();
        }
        foreach (var c in oldClients) c.Dispose();

        Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
        var stored = settings.Select(s => new StoredCamera(s with { Password = null }, Protect(s.Password))).ToList();
        File.WriteAllText(_storePath, JsonSerializer.Serialize(stored, JsonOptions), new UTF8Encoding(false));

        // Không ghi mật khẩu vào log.
        _logger.LogInformation("Camera connections saved: {Settings}", string.Join("; ", settings.Select(s =>
            $"{s.Station}.{s.Position}={(s.Enabled ? $"{s.Host}:{s.HttpPort}/{s.Brand}/ch{s.Channel}/user={s.Username}" : "off")}")));

        _ = CheckAllAsync();
    }

    public CameraLiveState GetLive(string station, string position)
    {
        lock (_lock)
        {
            var s = _settings.FirstOrDefault(x => x.Is(station, position));
            if (s is null || string.IsNullOrWhiteSpace(s.Host)) return new CameraLiveState { Error = "NOT_CONFIGURED" };
            if (!s.Enabled) return new CameraLiveState { Error = "DISABLED" };
            return _live.TryGetValue(LiveKey(station, position), out var state) ? state : new CameraLiveState();
        }
    }

    public async Task<byte[]?> CaptureAsync(string station, string position, CancellationToken ct)
    {
        var settings = FindEnabled(station, position);
        if (settings is null) return null;
        // Kiểm tra nền vừa báo mất kết nối → trả ngay, không bắt luồng gọi (đăng tài / xem trực tiếp) chờ hết timeout.
        if (GetLive(station, position).Error == "HOST_UNREACHABLE") return null;
        var result = await SnapshotAsync(settings, latency: null, rtspOk: null, ct);
        return result.Success ? result.Image : null;
    }

    // ── Kiểm tra / chụp ảnh ──

    public async Task<CameraTestResult> TestAsync(CameraConnectionSettings settings, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.Host))
            return new CameraTestResult { Error = "NOT_CONFIGURED" };

        var host = settings.Host.Trim();
        var timeout = TimeSpan.FromMilliseconds(Math.Clamp(settings.TimeoutMs, 500, 30000));

        var (tcpOk, latency, tcpError) = await TcpCheckAsync(host, settings.HttpPort, timeout, ct);
        if (!tcpOk)
            return new CameraTestResult
            {
                Error = "HOST_UNREACHABLE", ErrorDetail = $"{host}:{settings.HttpPort} — {tcpError}",
                RtspUrl = BuildRtspUrl(settings, settings.Brand)
            };

        var (rtspOk, _, _) = await TcpCheckAsync(host, settings.RtspPort, timeout, ct);
        return await SnapshotAsync(settings, latency, rtspOk, ct);
    }

    /// <summary>Chụp một ảnh qua HTTP (đường dẫn đã dò được, hoặc thử lần lượt theo hãng).</summary>
    private async Task<CameraTestResult> SnapshotAsync(
        CameraConnectionSettings settings, int? latency, bool? rtspOk, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.Host))
            return new CameraTestResult { Error = "NOT_CONFIGURED" };

        var host = settings.Host.Trim();
        var timeout = TimeSpan.FromMilliseconds(Math.Clamp(settings.TimeoutMs, 500, 30000));
        var rtspUrl = BuildRtspUrl(settings, settings.Brand);

        var key = PathKey(settings);
        List<(CameraBrand Brand, string Path)> candidates;
        lock (_lock)
            candidates = _resolvedPaths.TryGetValue(key, out var known) ? [known] : SnapshotCandidates(settings);

        var client = GetClient(settings);
        int? lastStatus = null;
        string? lastDetail = null;
        var notImage = false;
        var otherFailure = false; // lỗi khác 404 (timeout, 5xx...) → HTTP_ERROR thay vì "sai đường dẫn"

        foreach (var (brand, path) in candidates)
        {
            var url = $"http://{FormatHost(host)}:{settings.HttpPort}{path}";
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeout);
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseContentRead, cts.Token);
                lastStatus = (int)response.StatusCode;

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    return Fail("AUTH_FAILED", $"HTTP {lastStatus} — {url}");

                if (!response.IsSuccessStatusCode)
                {
                    lastDetail = $"HTTP {lastStatus} — {url}";
                    if (response.StatusCode != HttpStatusCode.NotFound) otherFailure = true;
                    continue;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
                if (!IsImage(bytes))
                {
                    notImage = true;
                    lastDetail = $"{response.Content.Headers.ContentType?.MediaType ?? "?"} ({bytes.Length} bytes) — {url}";
                    continue;
                }

                lock (_lock) _resolvedPaths[key] = (brand, path);
                return new CameraTestResult
                {
                    TcpOk = true, LatencyMs = latency, RtspPortOpen = rtspOk, HttpStatus = lastStatus,
                    DetectedBrand = brand, SnapshotUrl = url, RtspUrl = BuildRtspUrl(settings, brand), Image = bytes
                };
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                lastDetail = $"Timeout {timeout.TotalSeconds:0.#}s — {url}";
                otherFailure = true;
            }
            catch (HttpRequestException ex)
            {
                lastDetail = $"{ex.Message} — {url}";
                otherFailure = true;
            }
        }

        // Đường dẫn đã nhớ không còn dùng được → lần sau dò lại.
        lock (_lock) _resolvedPaths.Remove(key);

        if (notImage) return Fail("NOT_IMAGE", lastDetail);
        return otherFailure ? Fail("HTTP_ERROR", lastDetail) : Fail("SNAPSHOT_NOT_FOUND", lastDetail);

        CameraTestResult Fail(string code, string? detail) => new()
        {
            TcpOk = true, LatencyMs = latency, RtspPortOpen = rtspOk, HttpStatus = lastStatus,
            RtspUrl = rtspUrl, Error = code, ErrorDetail = detail
        };
    }

    private static List<(CameraBrand, string)> SnapshotCandidates(CameraConnectionSettings s)
    {
        var ch = Math.Max(1, s.Channel);
        var hik = new List<(CameraBrand, string)>
        {
            (CameraBrand.Hikvision, $"/ISAPI/Streaming/channels/{ch}01/picture"),
            (CameraBrand.Hikvision, $"/Streaming/channels/{ch}01/picture")
        };
        var dahua = new List<(CameraBrand, string)> { (CameraBrand.Dahua, $"/cgi-bin/snapshot.cgi?channel={ch}") };

        return s.Brand switch
        {
            CameraBrand.Hikvision => hik,
            CameraBrand.Dahua => dahua,
            CameraBrand.Custom when !string.IsNullOrWhiteSpace(s.SnapshotPath) =>
                [(CameraBrand.Custom, s.SnapshotPath.Trim().StartsWith('/') ? s.SnapshotPath.Trim() : "/" + s.SnapshotPath.Trim())],
            CameraBrand.Custom => [],
            _ => [.. hik, .. dahua]
        };
    }

    /// <summary>URL RTSP không kèm mật khẩu (chỉ để hiển thị / mở bằng trình xem khác).</summary>
    private static string? BuildRtspUrl(CameraConnectionSettings s, CameraBrand brand)
    {
        if (string.IsNullOrWhiteSpace(s.Host)) return null;
        var ch = Math.Max(1, s.Channel);
        var path = brand switch
        {
            CameraBrand.Hikvision => $"/Streaming/Channels/{ch}01",
            CameraBrand.Dahua => $"/cam/realmonitor?channel={ch}&subtype=0",
            _ => null
        };
        return path is null ? null : $"rtsp://{FormatHost(s.Host.Trim())}:{s.RtspPort}{path}";
    }

    private static async Task<(bool Ok, int? LatencyMs, string? Error)> TcpCheckAsync(
        string host, int port, TimeSpan timeout, CancellationToken ct)
    {
        using var client = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var sw = Stopwatch.StartNew();
        try
        {
            await client.ConnectAsync(host, port, cts.Token);
            return (true, (int)sw.ElapsedMilliseconds, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (false, null, $"Timeout {timeout.TotalSeconds:0.#}s");
        }
        catch (SocketException ex)
        {
            return (false, null, ex.SocketErrorCode.ToString());
        }
    }

    private static bool IsImage(byte[] b) =>
        b.Length > 4 && (
            (b[0] == 0xFF && b[1] == 0xD8) ||                          // JPEG
            (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)); // PNG

    private static string FormatHost(string host)
        => IPAddress.TryParse(host, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{host}]" : host;

    private static string PathKey(CameraConnectionSettings s)
        => $"{s.Host?.Trim()}|{s.HttpPort}|{s.Brand}|{s.Channel}|{s.SnapshotPath}";

    /// <summary>HttpClient theo camera + tài khoản; hỗ trợ Basic và Digest (Hikvision / Dahua dùng Digest).</summary>
    private HttpClient GetClient(CameraConnectionSettings s)
    {
        var key = $"{s.Host?.Trim()}|{s.HttpPort}|{s.Username}|{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s.Password ?? "")))}";
        lock (_lock)
        {
            if (_clients.TryGetValue(key, out var existing)) return existing;
            var handler = new SocketsHttpHandler
            {
                Credentials = string.IsNullOrEmpty(s.Username) ? null : new NetworkCredential(s.Username, s.Password ?? ""),
                PreAuthenticate = false,
                ConnectTimeout = TimeSpan.FromMilliseconds(Math.Clamp(s.TimeoutMs, 500, 30000)),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            };
            var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            _clients[key] = client;
            return client;
        }
    }

    // ── Kiểm tra nền ──

    private async Task CheckAllAsync()
    {
        if (Interlocked.Exchange(ref _checking, 1) == 1) return;
        try
        {
            List<CameraConnectionSettings> enabled;
            lock (_lock)
            {
                if (_disposed) return;
                enabled = _settings.Where(s => s.Enabled && !string.IsNullOrWhiteSpace(s.Host)).ToList();
            }

            foreach (var s in enabled)
            {
                var timeout = TimeSpan.FromMilliseconds(Math.Clamp(s.TimeoutMs, 500, 30000));
                var (ok, latency, error) = await TcpCheckAsync(s.Host!.Trim(), s.HttpPort, timeout, CancellationToken.None);
                var state = new CameraLiveState
                {
                    Online = ok, LatencyMs = latency, CheckedAt = DateTimeOffset.UtcNow,
                    Error = ok ? null : "HOST_UNREACHABLE", ErrorDetail = ok ? null : $"{s.Host}:{s.HttpPort} — {error}"
                };
                lock (_lock)
                {
                    var liveKey = LiveKey(s.Station, s.Position);
                    if (_live.TryGetValue(liveKey, out var prev) && prev.Online != ok)
                        _logger.LogInformation("Camera {Station}.{Position} {Host} {State}", s.Station, s.Position, s.Host,
                            ok ? "online" : "offline: " + error);
                    _live[liveKey] = state;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Camera status check failed");
        }
        finally
        {
            Interlocked.Exchange(ref _checking, 0);
        }
    }

    // ── Lưu trữ ──

    private sealed record StoredCamera(CameraConnectionSettings Settings, string? ProtectedPassword);

    private List<CameraConnectionSettings> Load()
    {
        try
        {
            if (!File.Exists(_storePath)) return [];
            var stored = JsonSerializer.Deserialize<List<StoredCamera>>(File.ReadAllText(_storePath), JsonOptions) ?? [];
            return stored.Select(s => s.Settings with { Password = Unprotect(s.ProtectedPassword) }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cannot read camera connection settings {Path}", _storePath);
            return [];
        }
    }

    private static readonly byte[] Entropy = "TTC.Weighing.Camera"u8.ToArray();

    private static string? Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return null;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI requires Windows.");
        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser));
    }

    private string? Unprotect(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher) || !OperatingSystem.IsWindows()) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(cipher), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex)
        {
            // Tệp chép từ tài khoản / máy khác → không giải mã được, người dùng nhập lại mật khẩu.
            _logger.LogWarning("Cannot decrypt camera password: {Error}", ex.Message);
            return null;
        }
    }

    public void Dispose()
    {
        List<HttpClient> clients;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            clients = [.. _clients.Values];
            _clients.Clear();
        }
        _checkTimer.Dispose();
        foreach (var c in clients) c.Dispose();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.Application.UseCases.Trips;
using TTC.Weighing.Infrastructure.Adapters;
using TTC.Weighing.Infrastructure.Integration;
using TTC.Weighing.Infrastructure.Persistence;
using TTC.Weighing.Infrastructure.Queries;
using TTC.Weighing.Infrastructure.Repositories;

namespace TTC.Weighing.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Đăng ký lớp lưu trữ. Connection string đọc từ cấu hình bên ngoài,
    /// không hard-code và không commit vào repository (R-035).
    /// </summary>
    public static IServiceCollection AddWeighingInfrastructure(
        this IServiceCollection services, string connectionString,
        IConfiguration? configuration = null)
    {
        services.AddDbContextFactory<WeighingDbContext>(options =>
            options
                .UseNpgsql(connectionString)
                .UseSnakeCaseNamingConvention()
                .EnableDetailedErrors()
                .EnableSensitiveDataLogging());

        // Scoped DbContext lấy từ factory — mỗi scope tạo instance riêng, tránh "command already in progress"
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<WeighingDbContext>>().CreateDbContext());

        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        // Lõi chuyến và cân
        services.AddScoped<ITripRepository, TripRepository>();
        services.AddScoped<IQrTokenRepository, QrTokenRepository>();
        services.AddScoped<IWeighSessionRepository, WeighSessionRepository>();
        services.AddScoped<IWorkflowProfileRepository, WorkflowProfileRepository>();
        services.AddScoped<IWeighbridgeRepository, WeighbridgeRepository>();

        // Chất lượng, phiên đo, đối soát
        services.AddScoped<IQualitySampleRepository, QualitySampleRepository>();
        services.AddScoped<IMeasurementSessionRepository, MeasurementSessionRepository>();
        services.AddScoped<IDeviceEventRepository, DeviceEventRepository>();
        services.AddScoped<IReconciliationRepository, ReconciliationRepository>();
        services.AddScoped<IAllocationRepository, AllocationRepository>();

        // Tích hợp, cổng, ngoại lệ
        services.AddScoped<IIntegrationMessageRepository, IntegrationMessageRepository>();
        services.AddScoped<IGateRepository, GateRepository>();
        services.AddScoped<IExternalDocumentRepository, ExternalDocumentRepository>();
        services.AddScoped<IBomSnapshotRepository, BomSnapshotRepository>();
        services.AddScoped<IApprovalRepository, ApprovalRepository>();
        services.AddScoped<IIncidentRepository, IncidentRepository>();

        // Hạ tầng chung
        services.AddScoped<IAuditLogger, EfAuditLogger>();
        services.AddScoped<IIntegrationStateQuery, EfIntegrationStateQuery>();
        services.AddScoped<ITripNumberGenerator, SequentialTripNumberGenerator>();

        // Read model
        services.AddScoped<IDashboardQueries, EfDashboardQueries>();
        services.AddScoped<ITripDetailQueries, EfTripDetailQueries>();
        services.AddScoped<IExceptionQueueQueries, EfExceptionQueueQueries>();
        services.AddScoped<IIntegrationMonitorQueries, EfIntegrationMonitorQueries>();
        services.AddScoped<IReportQueries, EfReportQueries>();
        services.AddScoped<ITripRegistrationQueries, EfTripRegistrationQueries>();

        // Trip registration redesign — camera, queue sequence, handlers
        // Camera IP cổng Vào/Ra qua TCP/IP — cấu hình theo máy trạm (màn Kết nối camera),
        // giá trị mặc định lần đầu lấy từ mục "Cameras" trong appsettings.
        services.AddSingleton<ICameraConnectionService>(sp => new Devices.IpCamera.IpCameraHub(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TTC.Weighing", "camera-connections.json"),
            ReadCameraDefaults(configuration),
            sp.GetRequiredService<ILogger<Devices.IpCamera.IpCameraHub>>()));
        var evidenceRoot = configuration?.GetValue<string>("Evidence:RootPath")
            ?? Path.Combine(AppContext.BaseDirectory, "evidence");
        services.AddScoped<ICameraService>(sp => new CameraServiceAdapter(
            sp.GetRequiredService<ICameraConnectionService>(), Path.Combine(evidenceRoot, "camera")));
        services.AddScoped<IQueueSequenceGenerator, SequentialQueueSequenceGenerator>();
        services.AddScoped<CheckPlanHandler>();
        services.AddScoped<QueueTripHandler>();
        services.AddScoped<CancelTripHandler>();
        services.AddScoped<DeleteTripHandler>();
        services.AddScoped<RegisterTripHandler>();

        // Nhật ký API đẩy / kéo (DigiScale, FRM, ERP, Portal) — handler gắn vào mọi HttpClient tích hợp bên dưới
        services.AddSingleton<ApiCallLogStore>();
        services.AddSingleton<TripPolicyStore>();
        services.AddSingleton<IApiCallLogQueries>(sp => sp.GetRequiredService<ApiCallLogStore>());

        // FRM/ERP lookup — cache dữ liệu master FRM (the-xe, xe-van-chuyen) vào bộ nhớ
        var frmTimeoutSeconds = configuration?.GetValue<int?>("FrmPlanLookup:TimeoutSeconds") ?? 30;

        // HttpClient cho FrmMasterDataCache — kéo bulk data
        services.AddHttpClient<FrmMasterDataCache>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(120); // 120s per page — bulk load dataset lớn

            var baseUrl = configuration?.GetValue<string>("FrmPlanLookup:BaseUrl");
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                if (!baseUrl.EndsWith('/'))
                    baseUrl += '/';
                client.BaseAddress = new Uri(baseUrl);
            }
        }).AddHttpMessageHandler(sp => new ApiCallLoggingHandler(sp.GetRequiredService<ApiCallLogStore>(), "FrmMasterDataCache"));
        services.AddSingleton(sp =>
        {
            // Resolve từ HttpClientFactory
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var client = factory.CreateClient(nameof(FrmMasterDataCache));
            return new FrmMasterDataCache(
                client,
                configuration!,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()
                    .CreateLogger<FrmMasterDataCache>());
        });

        // FrmPlanLookupAdapter giờ dùng cache, không cần HttpClient riêng
        services.AddScoped<IFrmPlanLookup, FrmPlanLookupAdapter>();

        // Weighing workflow integration — adapters mới
        services.AddScoped<IQrScanHandler, QrScanHandlerAdapter>();
        services.AddScoped<IWeighTicketPrintService, WeighTicketPrintAdapter>();
        services.AddScoped<ISampleScaleGateway, SampleScaleGatewayAdapter>();
        services.AddScoped<ICcsInstrumentReader, UnconfiguredCcsInstrumentReader>();

        // Đầu cân qua cổng COM — cấu hình theo máy trạm (màn Kết nối cân).
        services.AddSingleton<IScaleConnectionService>(sp => new Devices.SerialScale.SerialScaleHub(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TTC.Weighing", "scale-connections.json"),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<ILogger<Devices.SerialScale.SerialScaleHub>>()));
        services.AddScoped<IDeviceStatusMonitor, DeviceStatusMonitorAdapter>();
        services.AddScoped<IWeighTicketRepository, EfWeighTicketRepository>();

        // Weighing workflow integration — queries mới
        services.AddScoped<IWeighOperationQueries, EfWeighOperationQueries>();
        services.AddScoped<IWarehouseQueries, EfWarehouseQueries>();

        // Weighing workflow integration — handlers mới
        services.AddScoped<UpdateSugarcaneTypeHandler>();

        // FRM sync — typed HttpClient với timeout từ appsettings (similar to FrmPlanLookup)
        var frmSyncTimeoutSeconds = configuration?.GetValue<int?>("FrmSync:TimeoutSeconds") ?? 30;
        services.AddHttpClient<IFrmSyncService, FrmSyncAdapter>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(frmSyncTimeoutSeconds);
            var baseUrl = configuration?.GetValue<string>("FrmSync:BaseUrl");
            if (!string.IsNullOrWhiteSpace(baseUrl))
                client.BaseAddress = new Uri(baseUrl);
        }).AddHttpMessageHandler(sp => new ApiCallLoggingHandler(sp.GetRequiredService<ApiCallLogStore>(), "FrmSyncAdapter"));

        // DigiScale ticket sync — đẩy phiếu cân/đăng tài/tạp chất/CCS lên DigiScale
        services.AddHttpClient<DigiScaleTicketAdapter>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(90); // 90s — API chính sách (TrQuetCS) cần thời gian xử lý
            var baseUrl = configuration?.GetValue<string>("FrmPlanLookup:BaseUrl");
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                if (!baseUrl.EndsWith('/')) baseUrl += '/';
                client.BaseAddress = new Uri(baseUrl);
            }
        }).AddHttpMessageHandler(sp => new ApiCallLoggingHandler(sp.GetRequiredService<ApiCallLogStore>(), "DigiScaleTicketAdapter"));

        // Quy tắc cân (ngưỡng tạp chất/CCS/Fibre...) — kéo từ Portal DigiScale theo đơn vị
        services.AddHttpClient<DigiScaleScaleRulesAdapter>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            var baseUrl = configuration?.GetValue<string>("FrmPlanLookup:BaseUrl");
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                if (!baseUrl.EndsWith('/')) baseUrl += '/';
                client.BaseAddress = new Uri(baseUrl);
            }
        }).AddHttpMessageHandler(sp => new ApiCallLoggingHandler(sp.GetRequiredService<ApiCallLogStore>(), "DigiScaleScaleRulesAdapter"));

        // Chính sách thu mua — gọi thẳng API PhieuCan của FRM (IsQuetCS = Yes)
        services.AddHttpClient<FrmPolicyAdapter>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(configuration?.GetValue<int?>("FrmPolicyApi:TimeoutSeconds") ?? 60);
            var baseUrl = configuration?.GetValue<string>("FrmPolicyApi:BaseUrl");
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                if (!baseUrl.EndsWith('/')) baseUrl += '/';
                client.BaseAddress = new Uri(baseUrl);
            }
        }).AddHttpMessageHandler(sp => new ApiCallLoggingHandler(sp.GetRequiredService<ApiCallLogStore>(), "FrmPolicyAdapter"));

        // ERP order-lines — kéo đơn hàng ERP từ DigiScale
        services.AddHttpClient<ErpOrderLineAdapter>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            var baseUrl = configuration?.GetValue<string>("FrmPlanLookup:BaseUrl");
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                if (!baseUrl.EndsWith('/')) baseUrl += '/';
                client.BaseAddress = new Uri(baseUrl);
            }
        }).AddHttpMessageHandler(sp => new ApiCallLoggingHandler(sp.GetRequiredService<ApiCallLogStore>(), "ErpOrderLineAdapter"));

        return services;
    }

    /// <summary>
    /// Cameras:{Weigh|Registration}:{In|Out} trong appsettings (mật khẩu đặt ở appsettings.Local.json, không commit).
    /// Khóa cũ Cameras:In / Cameras:Out (bản trước chưa chia màn hình) được hiểu là camera bàn cân.
    /// </summary>
    private static List<CameraConnectionSettings> ReadCameraDefaults(IConfiguration? configuration)
    {
        var result = new List<CameraConnectionSettings>();
        if (configuration is null) return result;

        foreach (var station in CameraConnectionSettings.Stations)
        foreach (var position in CameraConnectionSettings.Positions)
        {
            var section = configuration.GetSection($"Cameras:{station}:{position}");
            var legacy = station == CameraConnectionSettings.StationWeigh
                ? configuration.GetSection($"Cameras:{position}")
                : null;
            string? Get(string key) => section.GetValue<string>(key) is { Length: > 0 } v ? v : legacy?.GetValue<string>(key);
            int? Int(string key) => int.TryParse(Get(key), out var v) ? v : null;

            var host = Get("Host");
            if (string.IsNullOrWhiteSpace(host)) continue;

            result.Add(new CameraConnectionSettings
            {
                Station = station,
                Position = position,
                Enabled = !bool.TryParse(Get("Enabled"), out var enabled) || enabled,
                Host = host.Trim(),
                HttpPort = Int("HttpPort") ?? 80,
                RtspPort = Int("RtspPort") ?? 554,
                Username = Get("Username"),
                Password = Get("Password"),
                Brand = Enum.TryParse<CameraBrand>(Get("Brand"), true, out var b) ? b : CameraBrand.Auto,
                Channel = Int("Channel") ?? 1,
                SnapshotPath = Get("SnapshotPath"),
                TimeoutMs = Int("TimeoutMs") ?? 3000
            });
        }
        return result;
    }
}

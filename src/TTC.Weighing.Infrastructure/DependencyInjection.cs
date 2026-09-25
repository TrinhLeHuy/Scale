using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.Application.UseCases.Trips;
using TTC.Weighing.Infrastructure.Adapters;
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
        services.AddScoped<ICameraService, CameraServiceAdapter>();
        services.AddScoped<IQueueSequenceGenerator, SequentialQueueSequenceGenerator>();
        services.AddScoped<CheckPlanHandler>();
        services.AddScoped<QueueTripHandler>();
        services.AddScoped<CancelTripHandler>();
        services.AddScoped<RegisterTripHandler>();

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
        });
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
        });

        return services;
    }
}

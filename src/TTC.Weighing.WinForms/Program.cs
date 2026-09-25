using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TTC.Weighing.Application.Abstractions;
using TTC.Weighing.Application.Queries;
using TTC.Weighing.Application.UseCases.Gate;
using TTC.Weighing.Application.UseCases.Integration;
using TTC.Weighing.Application.UseCases.Measurement;
using TTC.Weighing.Application.UseCases.Quality;
using TTC.Weighing.Application.UseCases.Reconcile;
using TTC.Weighing.Application.UseCases.Trips;
using TTC.Weighing.Application.UseCases.Weighing;
using TTC.Weighing.Infrastructure;
using TTC.Weighing.Infrastructure.Devices;
using TTC.Weighing.Infrastructure.Evidence;
using TTC.Weighing.Infrastructure.Integration;
using TTC.Weighing.Infrastructure.Persistence;
using TTC.Weighing.Infrastructure.Queries;
using TTC.Weighing.Integration.Adapters;
using TTC.Weighing.WinForms.Development;
using TTC.Weighing.WinForms.Forms;
using TTC.Weighing.WinForms.Localization;
using TTC.Weighing.WinForms.Presenters;

// Tránh nhập nhằng giữa namespace TTC.Weighing.Application và System.Windows.Forms.Application.
using WinFormsApp = System.Windows.Forms.Application;

namespace TTC.Weighing.WinForms;

internal static class Program
{
    /// <summary>Mã BU của tài khoản đăng nhập. Null = admin/super → thấy tất cả dữ liệu.</summary>
    internal static string? ActiveOrganizationUnitCode { get; set; }

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables("TTC_WEIGHING_")
            .Build();

        // ── Localization: khởi tạo trước UI. Ngôn ngữ mặc định lấy từ cấu hình,
        //    sau khi đăng nhập sẽ đổi theo culture/ngôn ngữ của tài khoản (từ web portal). ──
        var defaultLanguage = AppLanguageInfo.FromCode(
            configuration.GetValue<string>("App:Language") ?? "vi");
        var localizer = new Localizer(defaultLanguage);
        Loc.Initialize(localizer);

        var services = new ServiceCollection();
        services.AddSingleton<ILocalizer>(localizer);
        ConfigureServices(services, configuration);

        using var provider = services.BuildServiceProvider();

        // Kiểm tra nhanh cấu hình DI mà không mở cửa sổ, dùng cho CI và chẩn đoán.
        if (args.Contains("--smoke", StringComparer.OrdinalIgnoreCase))
            return RunSmokeCheck(provider);

        // Kiểm tra bố cục: phát hiện control bị cắt hoặc chồng nhau.
        if (args.Contains("--layout", StringComparer.OrdinalIgnoreCase))
            return RunLayoutCheck(provider);

        WinFormsApp.ThreadException += (_, e) => ReportFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportFatal(e.ExceptionObject as Exception);

        // ── Kéo dữ liệu FRM master data (xe, thẻ xe) về cache khi khởi động ──
        _ = Task.Run(async () =>
        {
            try
            {
                var cache = provider.GetRequiredService<TTC.Weighing.Infrastructure.Adapters.FrmMasterDataCache>();
                await cache.RefreshAsync();
            }
            catch
            {
                // Cache load thất bại không chặn app khởi động — user sẽ thấy thông báo "đang tải" khi tra cứu
            }
        });

        // ── Login trước khi mở MainForm — vòng lặp để hỗ trợ đăng xuất quay lại login ──
        while (true)
        {
            var apiBaseUrl = configuration.GetValue<string>("DigiScaleApi:BaseUrl");
            var requiredTenantId = configuration.GetValue<string>("DigiScaleApi:TenantId");
            using var loginForm = new LoginForm(apiBaseUrl, requiredTenantId);
            if (loginForm.ShowDialog() != DialogResult.OK)
                return 0; // User hủy đăng nhập → thoát app

            // Đổi ngôn ngữ theo tài khoản: culture trong JWT (sau này web portal cấp API BU + ngôn ngữ).
            if (loginForm.LoginResult?.Culture is { Length: > 0 } culture)
                localizer.SetLanguage(AppLanguageInfo.FromCode(culture));

            // Giới hạn ngôn ngữ theo allowed_cultures từ API — chỉ hiển thị ngôn ngữ BU cho phép.
            if (loginForm.LoginResult?.AllowedCultures is { Length: > 0 } allowedCultures)
            {
                var allowedLanguages = allowedCultures
                    .Select(AppLanguageInfo.FromCode)
                    .Distinct()
                    .ToList();
                localizer.SetAllowedLanguages(allowedLanguages);
            }

            var mainForm = provider.GetRequiredService<MainForm>();
            mainForm.SetLoggedInUser(loginForm.AuthenticatedUser ?? "Admin");

            // Hiển thị tên BU (công ty) trên brand bar nếu có.
            if (loginForm.LoginResult is { OrganizationUnitCode.Length: > 0 } lr)
            {
                mainForm.SetOrganizationUnit(lr.OrganizationUnitCode, lr.OrganizationUnitName);
                // Set org unit vào SiteContext để filter dữ liệu theo BU
                ActiveOrganizationUnitCode = lr.OrganizationUnitCode;
                Infrastructure.Queries.OrganizationUnitFilter.CurrentOrgUnit = lr.OrganizationUnitCode;
            }
            else
            {
                ActiveOrganizationUnitCode = null;
                Infrastructure.Queries.OrganizationUnitFilter.CurrentOrgUnit = null;
            }

            WinFormsApp.Run(mainForm);

            // Nếu MainForm đóng do đăng xuất → quay lại login; ngược lại → thoát app.
            if (!mainForm.IsLogout)
                return 0;

            // Reset ngôn ngữ về mặc định trước khi hiện lại màn hình login.
            localizer.SetLanguage(defaultLanguage);
            localizer.SetAllowedLanguages(null);
        }
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IClock, SystemClock>();

        // Adapter mặc định trả HELD vì hợp đồng FRM/ERP chưa được xác minh (D-02).
        services.AddScoped<IErpAdapter, UnverifiedContractErpAdapter>();
        services.AddScoped<IFrmAdapter, UnverifiedContractFrmAdapter>();

        AddProductionServices(services, configuration);

        AddUseCases(services);
        AddForms(services);
    }

    private static void AddUseCases(IServiceCollection services)
    {
        services.AddScoped<RegisterTripHandler>();
        services.AddScoped<QueueTripHandler>();
        services.AddScoped<CheckPlanHandler>();
        services.AddScoped<CancelTripHandler>();
        services.AddScoped<ResolveQrHandler>();

        services.AddScoped<OpenWeighSessionHandler>();
        services.AddScoped<EvaluateAndCommitWeightHandler>();
        services.AddScoped<ApproveManualWeightHandler>();
        services.AddScoped<RequestReweighHandler>();
        services.AddScoped<PauseResumeHandler>();

        services.AddScoped<RecordImpuritySampleHandler>();
        services.AddScoped<ReceiveCcsResultHandler>();

        services.AddScoped<OpenMeasurementSessionHandler>();
        services.AddScoped<CloseMeasurementSessionHandler>();

        services.AddScoped<ReconcileTripHandler>();
        services.AddScoped<AllocateTripHandler>();

        services.AddScoped<EvaluateGateHandler>();
        services.AddScoped<ConfirmVehiclePassageHandler>();

        services.AddScoped<SubmitTripResultHandler>();
        services.AddScoped<ResolveUnknownMessageHandler>();
    }

    private static void AddForms(IServiceCollection services)
    {
        // Event bus chia sẻ giữa các form — realtime cross-tab updates
        services.AddSingleton<Services.TripEventBus>();

        services.AddScoped<WeighOperationPresenter>();
        services.AddScoped<TripRegistrationPresenter>();
        services.AddScoped<QualityPresenter>();

        services.AddTransient<MainForm>();
        services.AddTransient<DashboardForm>();
        services.AddTransient<TripRegistrationForm>();
        services.AddTransient<WeighOperationForm>();
        services.AddTransient<QualityForm>();
        services.AddTransient<TripDetailForm>();
        services.AddTransient<ExceptionQueueForm>();
        services.AddTransient<IntegrationMonitorForm>();
        services.AddTransient<ConfigurationForm>();
        services.AddTransient<ReportForm>();
        services.AddTransient<GuideForm>();
    }

    /// <summary>
    /// Chạy trên cơ sở dữ liệu thật. Lớp lưu trữ, read model, điều kiện nghiệp vụ và bằng chứng
    /// đều là bản vận hành. Thiết bị và danh tính người dùng còn phụ thuộc D-03, D-05 và D-08.
    /// </summary>
    private static void AddProductionServices(IServiceCollection services, IConfiguration configuration)
    {
        // Connection string đọc từ cấu hình/biến môi trường, không hard-code (R-035).
        var connectionString = configuration.GetConnectionString("WeighingDb")
            ?? throw new InvalidOperationException(
                "Thiếu ConnectionStrings:WeighingDb. Cấu hình qua appsettings hoặc biến môi trường TTC_WEIGHING_.");

        services.AddWeighingInfrastructure(connectionString, configuration);

        var siteCode = configuration.GetValue<string>("Site:Code")
            ?? throw new InvalidOperationException(
                "Thiếu Site:Code. Trạm phải được gán nhà máy trước khi vận hành (R-002).");

        services.AddScoped<ISiteContext>(sp =>
            new SiteCodeContext(sp.GetRequiredService<WeighingDbContext>(), siteCode));

        // Bản vận hành, đọc và ghi dữ liệu thật.
        services.AddScoped<IWeighPreconditionQuery, EfWeighPreconditionQuery>();
        services.AddScoped<IDeviceEventPayloadReader, NormalizedDeviceEventPayloadReader>();

        services.AddSingleton<IEvidenceStore>(new FileSystemEvidenceStore(
            configuration.GetValue<string>("Evidence:RootPath")
            ?? Path.Combine(AppContext.BaseDirectory, "evidence")));

        // D-06 chưa duyệt dung sai nên cung cấp policy rỗng; engine kết luận Pending khi cần.
        services.AddSingleton<ITolerancePolicyProvider>(new ApprovedTolerancePolicyProvider());

        services.AddScoped<IIntegrationPayloadBuilder>(sp =>
            new SnapshotIntegrationPayloadBuilder(
                sp.GetRequiredService<WeighingDbContext>(),
                Path.Combine(AppContext.BaseDirectory, "outbox")));

        services.AddScoped<IWeighWorkspaceProvider, DatabaseWeighWorkspaceProvider>();

        // Danh tính người dùng cục bộ chỉ dành cho máy dev (D-08 chưa chốt phân quyền).
        services.AddScoped<ICurrentUser>(sp => new LocalOperatorIdentity(
            sp.GetRequiredService<ISiteContext>().CurrentSiteId,
            configuration.GetValue<string>("Auth:LocalOperatorName") ?? "Người trực cân cục bộ"));

        // Thiết bị: chưa có adapter thật đã nghiệm thu (D-03, D-05).
        // Luôn dùng simulated cho đến khi có thiết bị thật kết nối.
        services.AddSingleton<IScaleSignalGateway, SimulatedScaleSignalGateway>();
        services.AddSingleton<IPositionEvidenceGateway, SimulatedPositionEvidenceGateway>();
        services.AddSingleton<IBarrierControllerGateway, SimulatedBarrierControllerGateway>();
        services.AddSingleton<ICameraAiService, SimulatedCameraAiService>();
        services.AddScoped<IVehicleIdentityVerifier, LaneConfirmationIdentityVerifier>();
    }

    private static int RunLayoutCheck(IServiceProvider provider)
    {
        // Kiểm tra ở cả màn hình rộng và màn hình nhỏ, vì vùng tab thấp hơn cửa sổ
        // khoảng 160px do dải thương hiệu, menu, dải tab, status bar và chân trang.
        Size[] viewports = [new(1920, 860), new(1400, 700), new(1024, 420)];

        var report = new System.Text.StringBuilder();
        var failed = false;

        foreach (var viewport in viewports)
        {
            report.AppendLine($"=== Vùng hiển thị {viewport.Width}x{viewport.Height} ===");

            foreach (var type in ScreenTypes())
            {
                using var scope = provider.CreateScope();

                try
                {
                    var form = (Form)scope.ServiceProvider.GetRequiredService(type);
                    var findings = LayoutInspector.Inspect(form, viewport, MainForm.ResolveMinimumSize);

                    if (findings.Count > 0) failed = true;

                    report.AppendLine(LayoutInspector.Format(type.Name, findings));

                    foreach (var line in LayoutInspector.DescribeBlocks(form))
                        report.AppendLine(line);

                    form.Close();
                    form.Dispose();
                }
                catch (Exception ex)
                {
                    failed = true;
                    report.AppendLine($"ERROR {type.Name}: {ex.Message}");
                }
            }

            report.AppendLine();
        }

        File.WriteAllText(
            Path.Combine(AppContext.BaseDirectory, "layout-report.txt"),
            report.ToString());

        return failed ? 1 : 0;
    }

    private static Type[] ScreenTypes() =>
    [
        typeof(DashboardForm), typeof(TripRegistrationForm), typeof(WeighOperationForm),
        typeof(QualityForm), typeof(TripDetailForm),
        typeof(ExceptionQueueForm), typeof(IntegrationMonitorForm), typeof(ConfigurationForm),
        typeof(ReportForm)
    ];

    private static int RunSmokeCheck(IServiceProvider provider)
    {
        var targets = new[]
        {
            typeof(MainForm), typeof(DashboardForm), typeof(TripRegistrationForm),
            typeof(WeighOperationForm), typeof(QualityForm),
            typeof(TripDetailForm), typeof(ExceptionQueueForm), typeof(IntegrationMonitorForm),
            typeof(ConfigurationForm), typeof(ReportForm)
        };

        var failures = new List<string>();

        foreach (var type in targets)
        {
            try
            {
                using var scope = provider.CreateScope();
                var instance = scope.ServiceProvider.GetRequiredService(type);
                (instance as IDisposable)?.Dispose();
            }
            catch (Exception ex)
            {
                failures.Add($"{type.Name}: {ex.Message}");
            }
        }

        if (failures.Count == 0)
            return 0;

        // WinExe không có console nên ghi ra tệp để CI đọc được.
        File.WriteAllText(
            Path.Combine(AppContext.BaseDirectory, "smoke-failures.txt"),
            string.Join(Environment.NewLine, failures));

        return 1;
    }

    private static void ReportFatal(Exception? ex)
        => MessageBox.Show(
            $"Lỗi không xử lý được. Ứng dụng tạm ngưng thao tác nghiệp vụ.{Environment.NewLine}{Environment.NewLine}{ex?.Message}",
            "Lỗi hệ thống", MessageBoxButtons.OK, MessageBoxIcon.Error);
}

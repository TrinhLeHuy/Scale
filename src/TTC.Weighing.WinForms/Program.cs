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
using TTC.Weighing.WinForms.Services;

// Tránh nhập nhằng giữa namespace TTC.Weighing.Application và System.Windows.Forms.Application.
using WinFormsApp = System.Windows.Forms.Application;

namespace TTC.Weighing.WinForms;

internal static class Program
{
    /// <summary>Mã BU của tài khoản đăng nhập. Null = admin/super → thấy tất cả dữ liệu.</summary>
    internal static string? ActiveOrganizationUnitCode { get; set; }

    /// <summary>Tên đơn vị/nhà máy của tài khoản đăng nhập (Portal) — in trên đầu phiếu.</summary>
    internal static string? ActiveOrganizationUnitName { get; set; }

    /// <summary>Tên người dùng đăng nhập — dùng cho NhanVienCanVao, NhanVienDoCCS, etc.</summary>
    internal static string LoggedInUserName { get; set; } = "Admin";

    /// <summary>Username (account login) — dùng cho UserNameNhanVienCanVao.</summary>
    internal static string LoggedInUserAccount { get; set; } = "admin";

    /// <summary>Access token DigiScale (Bearer) — set sau login, dùng cho ERP API.</summary>
    internal static string? DigiScaleAccessToken { get; set; }

    /// <summary>Vai trò DigiScale của tài khoản đăng nhập (claim role trong token).</summary>
    internal static string? LoggedInRole { get; set; }

    /// <summary>Quyền DigiScale của tài khoản đăng nhập (claim permission trong token).</summary>
    internal static IReadOnlyCollection<string> LoggedInPermissions { get; set; } = [];

    /// <summary>Cấu hình ứng dụng — dùng cho các form cần đọc tham số luồng mía / API phê duyệt.</summary>
    internal static IConfiguration? AppConfiguration { get; private set; }

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
        AppConfiguration = configuration;

        // ── Localization: khởi tạo trước UI. Ngôn ngữ mặc định lấy từ cấu hình,
        //    sau khi đăng nhập sẽ đổi theo culture/ngôn ngữ của tài khoản (từ web portal). ──
        var defaultLanguage = AppLanguageInfo.FromCode(
            configuration.GetValue<string>("App:Language") ?? "vi");
        var localizer = new Localizer(defaultLanguage);
        Loc.Initialize(localizer);

        // Thông báo nghiệp vụ từ Domain/Application/Infrastructure đi qua cùng bộ ngôn ngữ.
        TTC.Weighing.Domain.Common.DomainText.Resolver = localizer.TryT;

        var services = new ServiceCollection();
        services.AddSingleton<ILocalizer>(localizer);
        ConfigureServices(services, configuration);

        // Build DI sau — hiện LoginForm trước để không giật
        ServiceProvider? provider = null;

        // Kiểm tra nhanh cấu hình DI — chỉ khi có flag
        if (args.Contains("--smoke", StringComparer.OrdinalIgnoreCase))
        {
            provider = services.BuildServiceProvider();
            return RunSmokeCheck(provider);
        }

        if (args.Contains("--layout", StringComparer.OrdinalIgnoreCase))
        {
            provider ??= services.BuildServiceProvider();
            return RunLayoutCheck(provider);
        }

        WinFormsApp.ThreadException += (_, e) => ReportFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportFatal(e.ExceptionObject as Exception);

        // ── Splash screen — hiện logo trong khi khởi động ──
        using var splash = new SplashForm();
        splash.Show();
        WinFormsApp.DoEvents();

        // ── Login ──
        while (true)
        {
            var apiBaseUrl = configuration.GetValue<string>("DigiScaleApi:BaseUrl");
            var requiredTenantId = configuration.GetValue<string>("DigiScaleApi:TenantId");

            var loginForm = new LoginForm(apiBaseUrl, requiredTenantId);

            // Đóng splash SAU khi LoginForm đã hiện xong — không chớp
            loginForm.Shown += (_, _) =>
            {
                try { if (!splash.IsDisposed && splash.Visible) splash.Hide(); } catch { }
            };

            var loginResult = loginForm.ShowDialog();

            if (loginResult != DialogResult.OK)
            {
                loginForm.Dispose();
                provider?.Dispose();
                return 0;
            }

            if (loginForm.LoginResult?.Culture is { Length: > 0 } culture)
                localizer.SetLanguage(AppLanguageInfo.FromCode(culture));

            if (loginForm.LoginResult?.AllowedCultures is { Length: > 0 } allowedCultures)
            {
                var allowedLanguages = allowedCultures
                    .Select(AppLanguageInfo.FromCode)
                    .Distinct()
                    .ToList();
                localizer.SetAllowedLanguages(allowedLanguages);
            }

            // Build DI SAU login (chỉ 1 lần)
            provider ??= services.BuildServiceProvider();

            // FRM cache: set BU TRƯỚC khi load → load đúng BU từ DB
            {
                var cache = provider.GetRequiredService<TTC.Weighing.Infrastructure.Adapters.FrmMasterDataCache>();

                // Set BU từ login result TRƯỚC khi load DB
                if (loginForm.LoginResult is { OrganizationUnitCode.Length: > 0 } lrForCache)
                    cache.ActiveBusinessUnit = lrForCache.OrganizationUnitCode;
                else
                    cache.ActiveBusinessUnit = configuration?.GetValue<string>("FrmPlanLookup:DefaultBusinessUnit");

                try { cache.LoadFromDbAsync().GetAwaiter().GetResult(); }
                catch { /* DB chưa có data → sẽ kéo API sau */ }

                // Refresh API background — không block UI
                _ = Task.Run(async () =>
                {
                    try { await cache.RefreshAsync(); }
                    catch { }
                });
            }

            try
            {
                var mainForm = provider.GetRequiredService<MainForm>();
                mainForm.SetLoggedInUser(loginForm.AuthenticatedUser ?? "Admin");
                LoggedInUserName = loginForm.AuthenticatedUser ?? "Admin";
                LoggedInUserAccount = loginForm.LoginResult?.UserName ?? loginForm.AuthenticatedUser ?? "admin";
                ApiCallLogContext.Actor = LoggedInUserAccount;
                DigiScaleAccessToken = loginForm.LoginResult?.AccessToken;
                LoggedInRole = loginForm.LoginResult?.Role;
                LoggedInPermissions = loginForm.LoginResult?.Permissions ?? [];

                // Quy tắc cân (ngưỡng tạp chất/CCS/Fibre...) — kéo từ Portal theo đơn vị đăng nhập, không hardcode.
                if (loginForm.LoginResult?.OrganizationUnitId is { Length: > 0 } orgUnitId)
                {
                    var token = loginForm.LoginResult.AccessToken;
                    PortalScaleRules.Configure(ct => provider
                        .GetRequiredService<TTC.Weighing.Infrastructure.Adapters.DigiScaleScaleRulesAdapter>()
                        .GetAsync(orgUnitId, token, ct));
                    _ = Task.Run(() => PortalScaleRules.RefreshAsync());
                }
                else
                {
                    PortalScaleRules.Configure(null);
                }

            // Hiển thị tên BU (công ty) trên brand bar nếu có.
            if (loginForm.LoginResult is { OrganizationUnitCode.Length: > 0 } lr)
            {
                mainForm.SetOrganizationUnit(lr.OrganizationUnitCode, lr.OrganizationUnitName);
                // Set org unit vào SiteContext để filter dữ liệu theo BU
                ActiveOrganizationUnitCode = lr.OrganizationUnitCode;
                ActiveOrganizationUnitName = string.IsNullOrWhiteSpace(lr.OrganizationUnitName) ? null : lr.OrganizationUnitName.Trim();
                Infrastructure.Queries.OrganizationUnitFilter.CurrentOrgUnit = lr.OrganizationUnitCode;
                Infrastructure.Integration.ApiCallLogContext.OrgUnit = lr.OrganizationUnitCode;

                // Set BU cho FRM cache để kéo đúng data theo account
                var frmCache = provider.GetRequiredService<TTC.Weighing.Infrastructure.Adapters.FrmMasterDataCache>();
                frmCache.ActiveBusinessUnit = lr.OrganizationUnitCode;

                // Set BU cho DigiScale ticket adapter
                var digiScale = provider.GetRequiredService<TTC.Weighing.Infrastructure.Adapters.DigiScaleTicketAdapter>();
                digiScale.ActiveBusinessUnit = lr.OrganizationUnitCode;

                // Set Bearer token cho ERP order-lines adapter
                var erpAdapter = provider.GetRequiredService<TTC.Weighing.Infrastructure.Adapters.ErpOrderLineAdapter>();
                erpAdapter.ActiveAccessToken = loginForm.LoginResult?.AccessToken;
            }
            else
            {
                // Admin offline — fallback config DefaultBusinessUnit
                var defaultBu = configuration?.GetValue<string>("FrmPlanLookup:DefaultBusinessUnit");
                ActiveOrganizationUnitCode = defaultBu;
                ActiveOrganizationUnitName = null;
                Infrastructure.Queries.OrganizationUnitFilter.CurrentOrgUnit = defaultBu;
                Infrastructure.Integration.ApiCallLogContext.OrgUnit = defaultBu;

                var frmCache = provider.GetRequiredService<TTC.Weighing.Infrastructure.Adapters.FrmMasterDataCache>();
                frmCache.ActiveBusinessUnit = defaultBu;

                var digiScale = provider.GetRequiredService<TTC.Weighing.Infrastructure.Adapters.DigiScaleTicketAdapter>();
                digiScale.ActiveBusinessUnit = defaultBu;

                // Set Bearer token nếu có (admin online)
                if (loginForm.LoginResult?.AccessToken is { Length: > 0 } offlineToken)
                {
                    var erpAdapter = provider.GetRequiredService<TTC.Weighing.Infrastructure.Adapters.ErpOrderLineAdapter>();
                    erpAdapter.ActiveAccessToken = offlineToken;
                }
            }

            WinFormsApp.Run(mainForm);

            // Nếu MainForm đóng do đăng xuất → quay lại login; ngược lại → thoát app.
            if (!mainForm.IsLogout)
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khởi tạo ứng dụng:\n{ex.Message}", "Lỗi hệ thống",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                continue; // Quay lại login thay vì tắt app
            }

            // Reset ngôn ngữ về mặc định trước khi hiện lại màn hình login.
            localizer.SetLanguage(defaultLanguage);
            localizer.SetAllowedLanguages(null);
            loginForm.Dispose();
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

        AddUseCases(services, configuration);
        AddForms(services);
    }

    private static void AddUseCases(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<RegisterTripHandler>();
        services.AddScoped<QueueTripHandler>();
        services.AddScoped<CheckPlanHandler>();
        services.AddScoped<CancelTripHandler>();
        services.AddScoped<DeleteTripHandler>();
        services.AddScoped<ResolveQrHandler>();

        services.AddScoped<OpenWeighSessionHandler>();
        services.AddScoped<EvaluateAndCommitWeightHandler>();
        services.AddScoped<ApproveManualWeightHandler>();
        services.AddScoped<RequestReweighHandler>();
        services.AddScoped<PauseResumeHandler>();

        services.AddSingleton(new SugarcaneQualityOptions
        {
            MaxImpurityWeighings = configuration.GetValue<int?>("SugarcaneFlow:Quality:MaxImpurityWeighings") ?? 3,
            // Cân lần 1 tối thiểu lấy từ Quy tắc cân trên Portal (không hardcode).
            MinSampleKgProvider = () => SugarcaneFlowSettings.Current.MinSampleKg
        });
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
        services.AddTransient<CcsForm>();
        services.AddTransient<TripDetailForm>();
        services.AddTransient<ExceptionQueueForm>();
        services.AddTransient<IntegrationMonitorForm>();
        services.AddTransient<ConfigurationForm>();
        services.AddTransient<ReportForm>();
        services.AddTransient<GuideForm>();
        services.AddTransient<FrmSyncForm>();
        services.AddTransient<ErpSyncForm>();
        services.AddTransient<ApiLogForm>();
        services.AddTransient<WarehouseForm>();
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
        typeof(QualityForm), typeof(CcsForm), typeof(ApiLogForm), typeof(TripDetailForm),
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

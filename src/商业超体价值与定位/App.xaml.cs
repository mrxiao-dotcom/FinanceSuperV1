using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using 商业超体价值与定位.ViewModels;
using 商业超体价值与定位.Services;
using Serilog;

namespace 商业超体价值与定位;

public partial class App : Application
{
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 配置日志
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File("logs/商业超体-.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        Log.Information("商业超体 V1.0 启动中...");
        Log.Information("  命令行参数 : {Args}", e.Args != null ? string.Join(" ", e.Args) : "(none)");

        // ===== 在最早的时间点挂上全局异常钩子 =====
        // 这两行必须放在 DI 容器构建之前——避免后续 ServiceProvider 构建失败时
        // 没有任何地方捕获崩溃。
        RegisterGlobalExceptionHandlers();

        try
        {
            // 运行时环境诊断
            RuntimeDiagnostics.RunStartupDiagnostics();

            // 配置依赖注入
            var services = new ServiceCollection();
            ConfigureServices(services);
            ServiceProvider = services.BuildServiceProvider();

            // 自动恢复会话
            var sessionService = ServiceProvider.GetRequiredService<ISessionService>();
            Log.Information("会话恢复检查：HasSavedSession = {Flag}", sessionService.HasSavedSession());
            if (sessionService.HasSavedSession())
            {
                sessionService.RestoreSession();
                Log.Information("会话已自动恢复，当前会话ID: {Id}", sessionService.CurrentSessionId);
            }

            base.OnStartup(e);
            Log.Information("商业超体 V1.0 启动完成");
        }
        catch (Exception ex)
        {
            // DI 容器构建失败 / OnStartup 之前出错时唯一的兜底
            Log.Fatal(ex, "应用启动失败");
            var crashFile = RuntimeDiagnostics.WriteCrashLog(ex, "OnStartup");
            ShowFatalMessageBox("应用启动失败",
                $"启动时发生未捕获异常，应用即将退出。\n\n" +
                $"异常类型: {ex.GetType().FullName}\n" +
                $"异常消息: {ex.Message}\n\n" +
                $"崩溃日志: {crashFile}");
            Shutdown(-1);
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // 注册HttpClient
        services.AddHttpClient();

        // 注册服务
        services.AddSingleton<ISessionService, SessionService>();
        services.AddSingleton<IDiagnosticEngine, DiagnosticEngine>();
        services.AddSingleton<IConversationService, ConversationService>();
        services.AddSingleton<IBusinessCanvasService, BusinessCanvasService>();
        services.AddSingleton<ILlmService, LlmService>();
        services.AddSingleton<IContentExtractorService, ContentExtractorService>();
        services.AddSingleton<IExportService, ExportService>();
        services.AddSingleton<IPromptsService, PromptsService>();
        services.AddSingleton<IWeeklyPlanService, WeeklyPlanService>();

        // 注册视图模型
        services.AddTransient<MainViewModel>();
        services.AddTransient<ChatViewModel>();
        services.AddTransient<BusinessCanvasViewModel>();
        services.AddTransient<SessionListViewModel>();
        services.AddTransient<WeeklyPlanViewModel>();
        services.AddTransient<PromptsViewModel>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("商业超体 V1.0 关闭中...");
        
        // 退出前保存会话
        try
        {
            var sessionService = ServiceProvider?.GetService<ISessionService>();
            sessionService?.AutoSave();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "退出前保存会话时发生错误");
        }
        
        try
        {
            Log.CloseAndFlush();
        }
        catch
        {
            // 兜底：日志关闭失败不影响退出
        }

        base.OnExit(e);
    }

    // =====================================================================
    // 全局异常兜底
    // =====================================================================

    private void RegisterGlobalExceptionHandlers()
    {
        // 1. WPF UI 线程未捕获异常（拖到 UI 线程上但没人 await 的异常、绑定转换器抛异常等）
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        // 2. AppDomain 中非 UI 线程（如 Task.Run / Timer / 后台线程）的未捕获异常
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        // 3. Task 中未被 observe 的异常（async void、没继续的 Task 链）
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        Log.Information("全局异常钩子已注册（Dispatcher / AppDomain / TaskScheduler）");
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "[UI 线程异常] 类型: {Type}, 消息: {Msg}",
            e.Exception.GetType().FullName, e.Exception.Message);

        var crashFile = RuntimeDiagnostics.WriteCrashLog(e.Exception, "DispatcherUnhandledException",
            extra: $"UI 线程上的未捕获异常。\n" +
                   $"Handled = true (应用将继续运行)");

        // 把堆栈也写到调试器（VS 输出窗口能立刻看到）
        Debug.WriteLine("[DispatcherUnhandledException] " + e.Exception);

        try
        {
            MessageBox.Show(
                $"UI 线程出现异常，已记录到日志：\n{crashFile}\n\n" +
                $"类型: {e.Exception.GetType().FullName}\n" +
                $"消息: {e.Exception.Message}",
                "商业超体 - 异常",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch
        {
            // MessageBox 自身也可能抛——绝对不能让兜底代码再次触发兜底
        }

        // 标记为已处理，应用继续运行，不弹 VS 异常助手
        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        Log.Fatal(ex, "[AppDomain 未捕获异常] IsTerminating={Flag}, Message={Msg}",
            e.IsTerminating, ex?.Message);

        if (e.IsTerminating)
        {
            var crashFile = RuntimeDiagnostics.WriteCrashLog(ex!, "AppDomain.UnhandledException (Terminating)",
                extra: "进程即将终止。");

            // 进程要死了——尽量快地把堆栈写出去，不弹模态阻塞
            try
            {
                Debug.WriteLine("[AppDomain.UnhandledException - TERMINATING] " + ex);
                if (crashFile.Length > 0)
                    Debug.WriteLine("Crash log: " + crashFile);
            }
            catch
            {
                // ignore
            }
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "[Task 未观察异常] 类型: {Type}, 消息: {Msg}",
            e.Exception.GetType().FullName, e.Exception.Message);

        var crashFile = RuntimeDiagnostics.WriteCrashLog(e.Exception, "TaskScheduler.UnobservedTaskException");

        try
        {
            Debug.WriteLine("[UnobservedTaskException] " + e.Exception);
            if (crashFile.Length > 0)
                Debug.WriteLine("Crash log: " + crashFile);
        }
        catch
        {
            // ignore
        }

        // 标记为已观察，避免 GC 时再次抛出
        e.SetObserved();
    }

    private static void ShowFatalMessageBox(string title, string message)
    {
        try
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            // 兜底：连 MessageBox 都用不了
            Debug.WriteLine($"[{title}] {message}");
        }
    }
}
using System.Windows;
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

        // 配置依赖注入
        var services = new ServiceCollection();
        ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();

        // 自动恢复会话
        var sessionService = ServiceProvider.GetRequiredService<ISessionService>();
        if (sessionService.HasSavedSession())
        {
            sessionService.RestoreSession();
            Log.Information("会话已自动恢复");
        }

        base.OnStartup(e);
        Log.Information("商业超体 V1.0 启动完成");
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

        // 注册视图模型
        services.AddTransient<MainViewModel>();
        services.AddTransient<ChatViewModel>();
        services.AddTransient<BusinessCanvasViewModel>();
        services.AddTransient<SessionListViewModel>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("商业超体 V1.0 关闭中...");
        
        // 退出前保存会话
        var sessionService = ServiceProvider.GetService<ISessionService>();
        sessionService?.AutoSave();
        
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}

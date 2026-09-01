using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using 商业超体价值与定位.Services;
using 商业超体价值与定位.ViewModels;
using 商业超体价值与定位.Views;

namespace 商业超体价值与定位;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var viewModel = App.ServiceProvider.GetRequiredService<MainViewModel>();
        DataContext = viewModel;

        viewModel.ChatViewModel.ProgressUpdated += OnProgressUpdated;

        // 监听删除会话确认请求
        viewModel.SessionListViewModel.RequestDeleteConfirmation += OnDeleteConfirmationRequested;

        // 初始化并从会话恢复数据
        _ = InitializeFromSessionAsync(viewModel);
    }

    private void OnProgressUpdated(object? sender, DiagnosticProgressInfo e)
    {
        var viewModel = DataContext as MainViewModel;
        viewModel?.BusinessCanvasViewModel.UpdateDiagnosticProgress(e);
    }

    private void OnDeleteConfirmationRequested(object? sender, EventArgs e)
    {
        var viewModel = DataContext as MainViewModel;
        var sessionToDelete = viewModel?.SessionListViewModel.GetPendingDeleteSession();

        var message = string.IsNullOrEmpty(sessionToDelete)
            ? "确定要删除这个会话吗？\n此操作不可恢复。"
            : $"确定要删除会话「{sessionToDelete}」吗？\n此操作不可恢复。";

        var result = MessageBox.Show(
            message,
            "确认删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            Log.Information("用户确认删除");
            viewModel?.SessionListViewModel.ConfirmDelete();
        }
        else
        {
            Log.Information("用户取消删除");
            viewModel?.SessionListViewModel.CancelDelete();
        }
    }

    private async Task InitializeFromSessionAsync(MainViewModel viewModel)
    {
        var sessionService = App.ServiceProvider.GetRequiredService<ISessionService>();
        var session = sessionService.CurrentSession;

        // 恢复对话消息
        if (session.Messages.Count > 0)
        {
            foreach (var msg in session.Messages)
            {
                viewModel.ChatViewModel.Messages.Add(msg);
            }
            viewModel.ChatViewModel.CurrentStage = session.CurrentStage;
        }
        else
        {
            // 没有历史消息，发送欢迎消息
            await viewModel.ChatViewModel.StartWelcomeMessageAsync();
        }

        // 恢复商业画布
        await viewModel.BusinessCanvasViewModel.UpdateFromSessionAsync(session);

        // 刷新会话列表
        viewModel.SessionListViewModel.LoadSessions();
    }

    private void ExportBlueprint_Click(object sender, RoutedEventArgs e)
    {
        var canvasVm = DataContext as MainViewModel;
        if (canvasVm?.BusinessCanvasViewModel.BlueprintCard != null)
        {
            var exportWindow = new BlueprintExportWindow(canvasVm.BusinessCanvasViewModel.BlueprintCard);
            exportWindow.Show();
        }
    }

    private void OpenWeeklyPlan_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // autoGenerate=true：主入口的「生成四周大纲」按钮要一次点击 = 立即生成大纲，
            // 而不是只打开窗口让用户再点一次。
            var weeklyPlanWindow = new WeeklyPlanWindow(autoGenerate: true);
            weeklyPlanWindow.Owner = this;
            weeklyPlanWindow.Show();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "[OpenWeeklyPlan_Click] 打开周计划窗口失败");
            MessageBox.Show($"无法打开周计划窗口：\n{ex.Message}",
                "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow();
        settingsWindow.ShowDialog();
    }

    private void OpenPrompts_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var promptsWindow = new PromptsWindow
            {
                Owner = this
            };
            promptsWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "[OpenPrompts_Click] 打开提示词配置窗口失败");
            MessageBox.Show($"无法打开提示词配置窗口：\n{ex.Message}",
                "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "商业超体 V1.0\n\n" +
            "AI智能商业诊断与价值定位引擎\n\n" +
            "采用 Copilot 伴随式对话模式，\n" +
            "帮助您挖掘商业底牌、构建护城河、锁定精准定位。\n\n" +
            "© 2026 商业超体",
            "关于",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using 商业超体价值与定位.ViewModels;

namespace 商业超体价值与定位.Views;

public partial class WeeklyPlanWindow : Window
{
    private readonly WeeklyPlanViewModel _viewModel;
    private readonly bool _autoGenerate;

    /// <summary>
    /// 创建周计划窗口。
    /// </summary>
    /// <param name="autoGenerate">
    /// 若为 true 且当前会话尚未生成周计划，则窗口加载完成后立即触发 GenerateOutlineAsync。
    /// 用于主窗口「生成四周大纲」入口：一次点击 = 打开窗口 + 立即生成。
    /// </param>
    public WeeklyPlanWindow(bool autoGenerate = false)
    {
        InitializeComponent();

        _viewModel = App.ServiceProvider.GetRequiredService<WeeklyPlanViewModel>();
        _autoGenerate = autoGenerate;
        DataContext = _viewModel;

        Loaded += OnLoaded;
        Log.Information("[WeeklyPlanWindow] 实例已创建，autoGenerate={Auto}", autoGenerate);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.InitializeAsync();
            Log.Information("[WeeklyPlanWindow] 初始化完成");

            // 如果是「主入口」打开且没有现存计划，自动开始生成
            if (_autoGenerate && _viewModel.CurrentPlan == null && !_viewModel.IsGeneratingOutline)
            {
                Log.Information("[WeeklyPlanWindow] autoGenerate=true，自动触发 GenerateOutlineAsync");
                await _viewModel.GenerateOutlineCommand.ExecuteAsync(null);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[WeeklyPlanWindow] 初始化失败");
            MessageBox.Show($"加载周计划时发生错误：\n{ex.Message}",
                "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

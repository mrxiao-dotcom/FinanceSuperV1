using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using 商业超体价值与定位.ViewModels;

namespace 商业超体价值与定位.Views;

public partial class WeeklyPlanWindow : Window
{
    private readonly WeeklyPlanViewModel _viewModel;

    public WeeklyPlanWindow()
    {
        InitializeComponent();

        _viewModel = App.ServiceProvider.GetRequiredService<WeeklyPlanViewModel>();
        DataContext = _viewModel;

        Loaded += OnLoaded;
        Log.Information("[WeeklyPlanWindow] 实例已创建");
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.InitializeAsync();
            Log.Information("[WeeklyPlanWindow] 初始化完成");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[WeeklyPlanWindow] 初始化失败");
            MessageBox.Show($"加载周计划时发生错误：\n{ex.Message}",
                "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

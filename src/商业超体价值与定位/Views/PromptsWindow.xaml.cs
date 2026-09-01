using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using 商业超体价值与定位.ViewModels;

namespace 商业超体价值与定位.Views;

public partial class PromptsWindow : Window
{
    private readonly PromptsViewModel _viewModel;

    public PromptsWindow()
    {
        InitializeComponent();

        _viewModel = App.ServiceProvider.GetRequiredService<PromptsViewModel>();
        DataContext = _viewModel;

        Log.Information("[PromptsWindow] 实例已创建");
    }
}
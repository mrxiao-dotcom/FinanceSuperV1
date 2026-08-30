using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Serilog;
using 商业超体价值与定位.Models;
using 商业超体价值与定位.Services;

namespace 商业超体价值与定位.Views;

public partial class SettingsWindow : Window
{
    private readonly ILlmService _llmService;
    private readonly LlmConfig _deepseekConfig;
    private readonly LlmConfig _kimiConfig;

    public SettingsWindow()
    {
        InitializeComponent();

        // 从 LlmService 获取当前配置
        _llmService = App.ServiceProvider.GetRequiredService<ILlmService>();
        Log.Information("[Settings] LlmService 获取成功");

        // 通过反射获取私有字段（因为没有公开的获取方法）
        var dsField = typeof(LlmService).GetField("_deepseekConfig",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var kmField = typeof(LlmService).GetField("_kimiConfig",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        _deepseekConfig = dsField?.GetValue(_llmService) as LlmConfig ?? new LlmConfig();
        _kimiConfig = kmField?.GetValue(_llmService) as LlmConfig ?? new LlmConfig();

        Log.Information("[Settings] DeepSeek ApiKey长度: {Length}", _deepseekConfig.ApiKey?.Length ?? 0);
        Log.Information("[Settings] Kimi ApiKey长度: {Length}", _kimiConfig.ApiKey?.Length ?? 0);

        LoadCurrentConfig();
    }

    private void LoadCurrentConfig()
    {
        // DeepSeek配置
        DeepSeekApiKeyBox.Password = _deepseekConfig.ApiKey;
        DeepSeekBaseUrlBox.Text = _deepseekConfig.BaseUrl;
        DeepSeekModelBox.Text = _deepseekConfig.Model;
        DeepSeekTemperatureBox.Text = _deepseekConfig.Temperature.ToString();
        DeepSeekMaxTokensBox.Text = _deepseekConfig.MaxTokens.ToString();

        // Kimi配置
        KimiApiKeyBox.Password = _kimiConfig.ApiKey;
        KimiBaseUrlBox.Text = _kimiConfig.BaseUrl;
        KimiModelBox.Text = _kimiConfig.Model;
        KimiTemperatureBox.Text = _kimiConfig.Temperature.ToString();
        KimiMaxTokensBox.Text = _kimiConfig.MaxTokens.ToString();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        Log.Information("[Settings] 保存按钮点击");

        // 保存DeepSeek配置
        var deepseekConfig = new LlmConfig
        {
            Provider = "DeepSeek",
            ApiKey = DeepSeekApiKeyBox.Password,
            BaseUrl = DeepSeekBaseUrlBox.Text,
            Model = DeepSeekModelBox.Text,
            Temperature = double.TryParse(DeepSeekTemperatureBox.Text, out var dsTemp) ? dsTemp : 0.7,
            MaxTokens = int.TryParse(DeepSeekMaxTokensBox.Text, out var dsTokens) ? dsTokens : 2000
        };

        // 保存Kimi配置
        var kimiConfig = new LlmConfig
        {
            Provider = "Kimi",
            ApiKey = KimiApiKeyBox.Password,
            BaseUrl = KimiBaseUrlBox.Text,
            Model = KimiModelBox.Text,
            Temperature = double.TryParse(KimiTemperatureBox.Text, out var kmTemp) ? kmTemp : 0.7,
            MaxTokens = int.TryParse(KimiMaxTokensBox.Text, out var kmTokens) ? kmTokens : 8000
        };

        Log.Information("[Settings] 要保存的 DeepSeek ApiKey长度: {Length}", deepseekConfig.ApiKey?.Length ?? 0);

        // 立即更新 LlmService 的配置
        _llmService.Configure(deepseekConfig, kimiConfig);
        Log.Information("[Settings] Configure 调用完成");

        MessageBox.Show(
            "配置已保存并生效。",
            "保存成功",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

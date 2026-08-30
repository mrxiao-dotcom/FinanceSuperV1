using System.IO;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Serilog;
using 商业超体价值与定位.Models;

namespace 商业超体价值与定位.Services;

public interface ILlmService
{
    Task<string> ChatAsync(string systemPrompt, List<LlmMessage> conversationHistory, bool useLongContext = false);
    Task<string> ExtractTagsAsync(string conversationContent);
    void Configure(LlmConfig deepseekConfig, LlmConfig kimiConfig);
}

public class LlmApiKeyNotConfiguredException : Exception
{
    public LlmApiKeyNotConfiguredException(string message) : base(message) { }
}

public class LlmService : ILlmService
{
    private readonly HttpClient _httpClient;
    private LlmConfig _deepseekConfig;
    private LlmConfig _kimiConfig;
    private LlmConfig _currentConfig;

    private const string DefaultSystemPrompt = @"你是一个精确的信息提取助手。请从对话内容中提取关键商业信息，并以JSON格式返回。
提取以下类型的标签：
- exclusive_resources: 独占资源
- pain_points: 客户痛点
- emotional_drivers: 情感驱动因素
- risks: 风险因素
- delivery_mode: 交付模式

请仅返回JSON，不要包含任何其他文字。格式如下：
{
    ""exclusive_resources"": [],
    ""pain_points"": [],
    ""emotional_drivers"": [],
    ""risks"": [],
    ""delivery_mode"": """"
}";

    public LlmService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _deepseekConfig = LoadConfig("deepseek_config.json", new LlmConfig
        {
            Provider = "DeepSeek",
            BaseUrl = "https://api.deepseek.com/v1",
            Model = "deepseek-chat",
            MaxTokens = 2000,
            Temperature = 0.7
        });

        _kimiConfig = LoadConfig("kimi_config.json", new LlmConfig
        {
            Provider = "Kimi",
            BaseUrl = "https://api.moonshot.cn/v1",
            Model = "moonshot-v1-8k",
            MaxTokens = 8000,
            Temperature = 0.7
        });

        _currentConfig = _deepseekConfig;

        Log.Information("[LlmService 构造] DeepSeek ApiKey: {ApiKey}, 长度: {Length}", 
            _deepseekConfig.ApiKey ?? "NULL", _deepseekConfig.ApiKey?.Length ?? 0);
        Log.Information("[LlmService 构造] Kimi ApiKey: {ApiKey}, 长度: {Length}", 
            _kimiConfig.ApiKey ?? "NULL", _kimiConfig.ApiKey?.Length ?? 0);
    }

    private static LlmConfig LoadConfig(string fileName, LlmConfig defaultConfig)
    {
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "商业超体",
            fileName);

        if (File.Exists(configPath))
        {
            try
            {
                var json = File.ReadAllText(configPath);
                return JsonConvert.DeserializeObject<LlmConfig>(json) ?? defaultConfig;
            }
            catch
            {
                return defaultConfig;
            }
        }

        return defaultConfig;
    }

    public void Configure(LlmConfig deepseekConfig, LlmConfig kimiConfig)
    {
        _deepseekConfig = deepseekConfig;
        _kimiConfig = kimiConfig;
        _currentConfig = _deepseekConfig;

        Log.Information("[LlmService.Configure] DeepSeek ApiKey长度: {Length}", _deepseekConfig.ApiKey?.Length ?? 0);
        Log.Information("[LlmService.Configure] Kimi ApiKey长度: {Length}", _kimiConfig.ApiKey?.Length ?? 0);

        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "商业超体");
        Directory.CreateDirectory(configDir);

        var dsConfigPath = Path.Combine(configDir, "deepseek_config.json");
        File.WriteAllText(dsConfigPath, JsonConvert.SerializeObject(deepseekConfig, Formatting.Indented));

        var kmConfigPath = Path.Combine(configDir, "kimi_config.json");
        File.WriteAllText(kmConfigPath, JsonConvert.SerializeObject(kimiConfig, Formatting.Indented));
    }

    private LlmConfig GetConfig(bool useLongContext)
    {
        if (useLongContext)
        {
            // 优先使用 Kimi，如果未配置则回退到 DeepSeek
            if (!string.IsNullOrEmpty(_kimiConfig.ApiKey))
            {
                return _kimiConfig;
            }
            Log.Warning("[GetConfig] Kimi 未配置，回退到 DeepSeek");
            return _deepseekConfig;
        }
        return _deepseekConfig;
    }

    public async Task<string> ChatAsync(string systemPrompt, List<LlmMessage> conversationHistory, bool useLongContext = false)
    {
        var config = GetConfig(useLongContext);
        _currentConfig = config;

        Log.Information("[ChatAsync] useLongContext={UseLongContext}, ApiKey 长度: {Length}", 
            useLongContext, config.ApiKey?.Length ?? 0);

        var messages = new List<LlmMessage>
        {
            new() { Role = "system", Content = systemPrompt }
        };
        messages.AddRange(conversationHistory);

        if (string.IsNullOrEmpty(config.ApiKey))
        {
            Log.Warning("API Key 未配置，使用模拟回复");
            throw new LlmApiKeyNotConfiguredException("请先在「设置」中配置 LLM API Key");
        }

        return await SendRequestAsync(config, messages);
    }

    public async Task<string> ExtractTagsAsync(string conversationContent)
    {
        var config = _deepseekConfig;

        Log.Information("[ExtractTagsAsync] DeepSeek ApiKey 长度: {Length}", config.ApiKey?.Length ?? 0);

        if (string.IsNullOrEmpty(config.ApiKey))
        {
            Log.Warning("DeepSeek API Key 未配置，跳过标签提取");
            throw new LlmApiKeyNotConfiguredException("请先在「设置」中配置 DeepSeek API Key");
        }

        var request = new LlmRequest
        {
            Model = config.Model,
            Messages = new List<LlmMessage>
            {
                new() { Role = "system", Content = DefaultSystemPrompt },
                new() { Role = "user", Content = conversationContent }
            },
            MaxTokens = 1000,
            Temperature = 0.3
        };

        return await SendRequestAsync(config, request.Messages);
    }

    private async Task<string> SendRequestAsync(LlmConfig config, List<LlmMessage> messages)
    {
        var request = new LlmRequest
        {
            Model = config.Model,
            Messages = messages,
            MaxTokens = config.MaxTokens,
            Temperature = config.Temperature
        };

        try
        {
            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");

            var response = await _httpClient.PostAsync(config.BaseUrl + "/chat/completions", content);
            var responseJson = await response.Content.ReadAsStringAsync();

            Log.Information("[API响应] 状态码: {StatusCode}", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                Log.Error("LLM API调用失败: {StatusCode} - {Response}", response.StatusCode, responseJson);
                throw new Exception($"API调用失败: {response.StatusCode}");
            }

            var llmResponse = JsonConvert.DeserializeObject<LlmResponse>(responseJson);
            var responseContent = llmResponse?.Choices.FirstOrDefault()?.Message.Content ?? "";
            Log.Information("[API响应内容] {ResponseContent}", responseContent);
            return responseContent;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "调用LLM服务时发生错误");
            throw;
        }
    }

}

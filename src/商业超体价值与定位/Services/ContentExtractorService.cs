using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Serilog;
using 商业超体价值与定位.Models;

namespace 商业超体价值与定位.Services;

public interface IContentExtractorService
{
    Task ExtractAndUpdateAsync(
        IReadOnlyCollection<Message> messages,
        DiagnosticSession session);
}

public class ContentExtractorService : IContentExtractorService
{
    private readonly ILlmService _llmService;
    private readonly JsonSerializerSettings _jsonSettings;

    public ContentExtractorService(ILlmService llmService)
    {
        _llmService = llmService;
        _jsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new SnakeCaseNamingStrategy()
            }
        };
    }

    public async Task ExtractAndUpdateAsync(
        IReadOnlyCollection<Message> messages,
        DiagnosticSession session)
    {
        try
        {
            Log.Information("开始提取对话内容");

            var recentMessages = messages
                .TakeLast(4)
                .Select(m => $"{(m.Role == MessageRole.User ? "用户" : "顾问")}：{m.Content}")
                .Aggregate((a, b) => a + "\n" + b);

            var extractedJson = await _llmService.ExtractTagsAsync(recentMessages);

            Log.Information("[提取结果] AI返回内容: {Content}", extractedJson);

            if (string.IsNullOrWhiteSpace(extractedJson))
            {
                Log.Warning("提取结果为空，使用默认内容");
                SetDefaultCanvasContent(session);
                return;
            }

            var tags = JsonConvert.DeserializeObject<ExtractedTags>(extractedJson, _jsonSettings);
            if (tags == null)
            {
                Log.Warning("无法解析提取结果: {Content}，尝试使用默认内容", extractedJson);
                SetDefaultCanvasContent(session);
                return;
            }

            UpdateCanvasFromTags(tags, session);

            Log.Information("内容提取完成，共提取 {Count} 个标签", tags.GetTotalCount());
        }
        catch (LlmApiKeyNotConfiguredException ex)
        {
            Log.Warning("API Key 未配置，使用默认内容");
            SetDefaultCanvasContent(session);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "内容提取时发生错误，使用默认内容");
            SetDefaultCanvasContent(session);
        }
    }

    private void SetDefaultCanvasContent(DiagnosticSession session)
    {
        var canvas = session.Canvas;

        // 只设置默认内容，不激活卡片
        // 卡片激活应该只在有实际内容时才发生
        if (!canvas.MoatCard.IsActivated)
        {
            canvas.MoatCard.ExclusiveResources = new List<string>();
            canvas.MoatCard.Content = "请开始与AI顾问对话，\n系统将自动提取您的商业护城河...";
        }

        if (!canvas.PainPointCard.IsActivated)
        {
            canvas.PainPointCard.HiddenCosts = new List<string>();
            canvas.PainPointCard.Content = "请开始与AI顾问对话，\n系统将自动识别客户痛点...";
        }

        if (!canvas.EmotionalPremiumCard.IsActivated)
        {
            canvas.EmotionalPremiumCard.EmotionalDrivers = new List<string>();
            canvas.EmotionalPremiumCard.Content = "请开始与AI顾问对话，\n系统将自动发现情感驱动力...";
        }

        if (!canvas.BlueprintCard.IsActivated)
        {
            canvas.BlueprintCard.Content = "完成对话后，\n系统将生成终极商业蓝图...";
        }

        canvas.CompletionPercentage = CalculateCompletion(canvas);
        Log.Information("[默认内容] 画布完成度: {Percentage}", canvas.CompletionPercentage);
    }

    private void UpdateCanvasFromTags(ExtractedTags tags, DiagnosticSession session)
    {
        var canvas = session.Canvas;

        // 护城河：仅在有内容时激活
        if (tags.ExclusiveResources.Count > 0)
        {
            canvas.MoatCard.ExclusiveResources = tags.ExclusiveResources;
            canvas.MoatCard.Content = string.Join("、", tags.ExclusiveResources);
            canvas.MoatCard.IsActivated = true;
        }
        else if (!canvas.MoatCard.IsActivated)
        {
            // 卡片尚未激活时，只设置占位提示，不激活
            canvas.MoatCard.ExclusiveResources = new List<string>();
            canvas.MoatCard.Content = "请开始与AI顾问对话，\n系统将自动提取您的商业护城河...";
        }

        // 客户痛点：仅在有内容时激活
        if (tags.PainPoints.Count > 0)
        {
            canvas.PainPointCard.HiddenCosts = tags.PainPoints;
            canvas.PainPointCard.Content = string.Join("、", tags.PainPoints);
            canvas.PainPointCard.IsActivated = true;
        }
        else if (!canvas.PainPointCard.IsActivated)
        {
            canvas.PainPointCard.HiddenCosts = new List<string>();
            canvas.PainPointCard.Content = "请开始与AI顾问对话，\n系统将自动识别客户痛点...";
        }

        // 情感溢价：仅在有内容时激活
        if (tags.EmotionalDrivers.Count > 0)
        {
            canvas.EmotionalPremiumCard.EmotionalDrivers = tags.EmotionalDrivers;
            canvas.EmotionalPremiumCard.Content = string.Join("、", tags.EmotionalDrivers);
            canvas.EmotionalPremiumCard.IsActivated = true;
        }
        else if (!canvas.EmotionalPremiumCard.IsActivated)
        {
            canvas.EmotionalPremiumCard.EmotionalDrivers = new List<string>();
            canvas.EmotionalPremiumCard.Content = "请开始与AI顾问对话，\n系统将自动发现情感驱动力...";
        }

        // 商业蓝图：仅在有交付模式时激活
        if (!string.IsNullOrEmpty(tags.DeliveryMode))
        {
            canvas.BlueprintCard.DeliveryMode = new DeliveryMode
            {
                Name = tags.DeliveryMode,
                IsHighTouch = tags.DeliveryMode.Contains("陪伴") || tags.DeliveryMode.Contains("高客单")
            };
            canvas.BlueprintCard.IsActivated = true;
        }
        else if (!canvas.BlueprintCard.IsActivated)
        {
            // 蓝图未激活时，重置 DeliveryMode 引用（防止上一次留下）
            canvas.BlueprintCard.DeliveryMode = null;
            canvas.BlueprintCard.Content = "完成对话后，\n系统将生成终极商业蓝图...";
        }

        // 风险补充（不强制激活，因为它是痛点卡片的辅助字段）
        if (tags.Risks.Count > 0)
        {
            canvas.PainPointCard.CriticalRisks = tags.Risks;
        }

        canvas.CompletionPercentage = CalculateCompletion(canvas);
    }

    private static double CalculateCompletion(BusinessCanvas canvas)
    {
        double score = 0;

        if (canvas.MoatCard.IsActivated) score += 0.25;
        if (canvas.PainPointCard.IsActivated) score += 0.25;
        if (canvas.EmotionalPremiumCard.IsActivated) score += 0.25;
        if (canvas.BlueprintCard.IsActivated ||
            canvas.BlueprintCard.DeliveryMode != null) score += 0.25;

        return score;
    }
}

internal class ExtractedTags
{
    public List<string> ExclusiveResources { get; set; } = new();
    public List<string> PainPoints { get; set; } = new();
    public List<string> EmotionalDrivers { get; set; } = new();
    public List<string> Risks { get; set; } = new();
    public string DeliveryMode { get; set; } = string.Empty;

    public int GetTotalCount() =>
        ExclusiveResources.Count +
        PainPoints.Count +
        EmotionalDrivers.Count +
        Risks.Count;
}

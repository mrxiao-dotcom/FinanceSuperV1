using Serilog;
using 商业超体价值与定位.Models;

namespace 商业超体价值与定位.Services;

public interface IDiagnosticEngine
{
    DiagnosticStage DetermineNextStage(IReadOnlyList<Message> messages, DiagnosticStage currentStage);
}

public class DiagnosticEngine : IDiagnosticEngine
{
    private static readonly Dictionary<DiagnosticStage, int> StageMessageThresholds = new()
    {
        { DiagnosticStage.ExploringExclusiveResources, 2 },
        { DiagnosticStage.ProbingHiddenPainPoints, 4 },
        { DiagnosticStage.ConfirmingDeliveryBoundaries, 6 },
        { DiagnosticStage.BuildingMoat, 8 },
        { DiagnosticStage.GeneratingBlueprint, 10 }
    };

    private static readonly List<string>[] StageKeywords = new List<string>[]
    {
        new() { "资源", "优势", "独特", "专利", "渠道", "供应链", "方法论", "模型", "核心竞争力" },
        new() { "痛点", "问题", "风险", "成本", "不买", "后果", "损失", "困扰" },
        new() { "时间", "精力", "团队", "利润", "定价", "交付", "边界", "工作量" },
        new() { "竞争", "对手", "同行", "差异", "护城河", "壁垒", "模仿" },
        new() { "蓝图", "签名", "定位", "方案", "策略", "模式", "建议", "总结" }
    };

    public DiagnosticStage DetermineNextStage(IReadOnlyList<Message> messages, DiagnosticStage currentStage)
    {
        if (messages.Count < 2)
            return currentStage;

        var userMessages = messages.Where(m => m.Role == MessageRole.User).ToList();
        var recentMessages = userMessages.TakeLast(3).ToList();
        var allRecentContent = string.Join(" ", recentMessages.Select(m => m.Content));

        var currentStageIndex = (int)currentStage;
        if (currentStageIndex >= StageKeywords.Length)
            return currentStage;

        // 只要检测到1个关键词就可以推进
        var keywordCount = StageKeywords[currentStageIndex]
            .Count(keyword => allRecentContent.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        if (keywordCount >= 1)
        {
            Log.Information("检测到阶段推进关键词({Count}个)，阶段: {Stage}", keywordCount, currentStage);
            return AdvanceStage(currentStage);
        }

        var messageCount = messages.Count(m => m.Role == MessageRole.User);
        var threshold = StageMessageThresholds.GetValueOrDefault(currentStage, 4);

        if (messageCount >= threshold)
        {
            Log.Information("消息数量达到阈值 {Threshold}，推进阶段: {Stage}", threshold, currentStage);
            return AdvanceStage(currentStage);
        }

        return currentStage;
    }

    private DiagnosticStage AdvanceStage(DiagnosticStage current)
    {
        return current switch
        {
            DiagnosticStage.NotStarted => DiagnosticStage.ExploringExclusiveResources,
            DiagnosticStage.ExploringExclusiveResources => DiagnosticStage.ProbingHiddenPainPoints,
            DiagnosticStage.ProbingHiddenPainPoints => DiagnosticStage.ConfirmingDeliveryBoundaries,
            DiagnosticStage.ConfirmingDeliveryBoundaries => DiagnosticStage.BuildingMoat,
            DiagnosticStage.BuildingMoat => DiagnosticStage.GeneratingBlueprint,
            _ => current
        };
    }
}

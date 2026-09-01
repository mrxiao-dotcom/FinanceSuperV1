using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Serilog;
using 商业超体价值与定位.Models;
using MessageRole = 商业超体价值与定位.Models.MessageRole;

namespace 商业超体价值与定位.Services;

public interface IWeeklyPlanService
{
    /// <summary>加载 prompt 模板（每次生成时调用，确保读取最新配置）。</summary>
    Task LoadPromptsAsync();

    /// <summary>基于商业蓝图生成四周执行大纲。</summary>
    Task<WeeklyPlan> GenerateWeeklyOutlineAsync(DiagnosticSession session, int totalWeeks = 4);

    /// <summary>为某一特定周生成日常任务。</summary>
    Task<WeekPlan> GenerateDailyTasksAsync(WeeklyPlan plan, int weekNumber, DiagnosticSession session);

    /// <summary>从 session.json 加载周计划（启动/切换会话时调用）。</summary>
    WeeklyPlan? LoadWeeklyPlan(string sessionId);

    /// <summary>将周计划保存到 session.json + 导出 Markdown。</summary>
    void SaveWeeklyPlan(DiagnosticSession session);

    /// <summary>获取当前会话的蓝图文本（从对话历史中提取）。</summary>
    string GetBlueprintText(DiagnosticSession session);
}

public class WeeklyPlanService : IWeeklyPlanService
{
    private readonly ILlmService _llmService;
    private readonly IPromptsService _promptsService;
    private readonly JsonSerializerSettings _jsonSettings;
    private readonly string _sessionsFolder;

    private string _weeklyPlanPromptTemplate = string.Empty;
    private string _dailyTasksPromptTemplate = string.Empty;

    private const string WeeklyPlanPromptNotLoaded =
        "提示词未加载，请在调用前先调用 LoadPromptsAsync()";

    public WeeklyPlanService(ILlmService llmService, IPromptsService promptsService)
    {
        _llmService = llmService;
        _promptsService = promptsService;
        _jsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new SnakeCaseNamingStrategy()
            }
        };

        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "商业超体");
        _sessionsFolder = Path.Combine(appDataPath, "sessions");

        // 订阅 prompt 变更：用户在「提示词配置」窗口保存后立即生效
        _promptsService.PromptsChanged += OnPromptsChanged;
    }

    private void OnPromptsChanged(object? sender, EventArgs e)
    {
        // 从 PromptsService 重新拉取最新值（避免重复读盘）
        ReloadFromService();
        Log.Information("[WeeklyPlanService] 收到 PromptsChanged 事件，已更新本地 prompt 缓存");
    }

    private void ReloadFromService()
    {
        var wpp = _promptsService.Get("WeeklyPlanPrompt");
        var dtp = _promptsService.Get("DailyTasksPrompt");
        _weeklyPlanPromptTemplate  = string.IsNullOrWhiteSpace(wpp) ? string.Empty : wpp;
        _dailyTasksPromptTemplate = string.IsNullOrWhiteSpace(dtp) ? string.Empty : dtp;
    }

    public async Task LoadPromptsAsync()
    {
        // 直接从 PromptsService 拉取（PromptsService 启动时已自动加载）
        ReloadFromService();

        Log.Information("[WeeklyPlanService] prompts 已从 PromptsService 加载。WeeklyPlanPrompt={HasIt}, DailyTasksPrompt={HasIt2}",
            !string.IsNullOrEmpty(_weeklyPlanPromptTemplate),
            !string.IsNullOrEmpty(_dailyTasksPromptTemplate));

        await Task.CompletedTask;
    }

    public async Task<WeeklyPlan> GenerateWeeklyOutlineAsync(DiagnosticSession session, int totalWeeks = 4)
    {
        if (string.IsNullOrEmpty(_weeklyPlanPromptTemplate))
        {
            Log.Error("[WeeklyPlanService] WeeklyPlanPrompt 未加载，抛出异常");
            throw new InvalidOperationException(WeeklyPlanPromptNotLoaded);
        }

        var blueprintText = GetBlueprintText(session);
        if (string.IsNullOrWhiteSpace(blueprintText))
        {
            throw new InvalidOperationException("商业蓝图为空，请在生成蓝图后再生成周计划。");
        }

        Log.Information("[GenerateWeeklyOutlineAsync] 开始生成 {TotalWeeks} 周大纲，蓝图长度: {Len}",
            totalWeeks, blueprintText.Length);

        // 蓝图摘要（前 1200 字，避免上下文溢出）
        var blueprintExcerpt = blueprintText.Length > 1200
            ? blueprintText.Substring(0, 1200) + "\n\n（蓝图内容过长，已截断）"
            : blueprintText;

        var prompt = _weeklyPlanPromptTemplate
            .Replace("{{total_weeks}}", totalWeeks.ToString())
            .Replace("{{blueprint}}", blueprintExcerpt);

        var response = await _llmService.ChatAsync(
            "你是商业执行规划专家，专注于将战略蓝图转化为可执行的操作计划。",
            new List<LlmMessage> { new() { Role = "user", Content = prompt } },
            useLongContext: true);

        Log.Information("[GenerateWeeklyOutlineAsync] AI 返回长度: {Len}", response.Length);

        var plan = TryParseWeeklyPlan(response, session.Id, blueprintExcerpt, totalWeeks);

        if (plan == null)
        {
            Log.Warning("[GenerateWeeklyOutlineAsync] JSON 解析失败，使用默认 4 周大纲");
            plan = GenerateDefaultWeeklyPlan(session.Id, blueprintExcerpt, totalWeeks);
        }

        Log.Information("[GenerateWeeklyOutlineAsync] 完成，生成的周数: {Count}", plan.Weeks.Count);
        return plan;
    }

    public async Task<WeekPlan> GenerateDailyTasksAsync(WeeklyPlan plan, int weekNumber, DiagnosticSession session)
    {
        if (string.IsNullOrEmpty(_dailyTasksPromptTemplate))
        {
            Log.Error("[GenerateDailyTasksAsync] DailyTasksPrompt 未加载");
            throw new InvalidOperationException("提示词未加载，请在调用前先调用 LoadPromptsAsync()");
        }

        var week = plan.Weeks.FirstOrDefault(w => w.WeekNumber == weekNumber);
        if (week == null)
        {
            throw new ArgumentException($"未找到第 {weekNumber} 周的计划");
        }

        var blueprintText = GetBlueprintText(session);
        var blueprintExcerpt = blueprintText.Length > 1200
            ? blueprintText.Substring(0, 1200) + "\n\n（蓝图内容过长，已截断）"
            : blueprintText;

        var prompt = _dailyTasksPromptTemplate
            .Replace("{{week_number}}", weekNumber.ToString())
            .Replace("{{total_weeks}}", plan.TotalWeeks.ToString())
            .Replace("{{blueprint}}", blueprintExcerpt)
            .Replace("{{week_outline}}", week.Outline);

        Log.Information("[GenerateDailyTasksAsync] 第 {Week} 周开始生成任务，蓝图长度: {Len}, 大纲长度: {OutlineLen}",
            weekNumber, blueprintExcerpt.Length, week.Outline.Length);

        var response = await _llmService.ChatAsync(
            "你是内容营销专家，专注于生成可直接执行的日常内容任务。",
            new List<LlmMessage> { new() { Role = "user", Content = prompt } },
            useLongContext: true);

        Log.Information("[GenerateDailyTasksAsync] AI 返回长度: {Len}", response.Length);

        var tasks = TryParseDailyTasks(response, weekNumber);

        week.DailyTasks = tasks;
        week.TasksGeneratedAt = DateTime.Now;

        Log.Information("[GenerateDailyTasksAsync] 第 {Week} 周任务生成完成，任务数: {Count}",
            weekNumber, tasks.Count);

        return week;
    }

    public WeeklyPlan? LoadWeeklyPlan(string sessionId)
    {
        try
        {
            var folder = Path.Combine(_sessionsFolder, sessionId);
            var jsonFile = Path.Combine(folder, "weekly-plan.json");

            if (File.Exists(jsonFile))
            {
                var json = File.ReadAllText(jsonFile);
                var plan = JsonConvert.DeserializeObject<WeeklyPlan>(json);
                Log.Information("[LoadWeeklyPlan] 已加载周计划，SessionId={Id}, 周数={Weeks}",
                    sessionId, plan?.Weeks.Count ?? 0);
                return plan;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[LoadWeeklyPlan] 加载周计划失败，SessionId={Id}", sessionId);
        }
        return null;
    }

    public void SaveWeeklyPlan(DiagnosticSession session)
    {
        if (session.WeeklyPlan == null)
            return;

        try
        {
            var folder = Path.Combine(_sessionsFolder, session.Id);
            Directory.CreateDirectory(folder);

            // 1. 保存 JSON（供下次加载）
            var jsonFile = Path.Combine(folder, "weekly-plan.json");
            var json = JsonConvert.SerializeObject(session.WeeklyPlan, Formatting.Indented);
            File.WriteAllText(jsonFile, json, Encoding.UTF8);

            // 2. 导出 Markdown（供人工查阅与复制到下游应用）
            ExportWeeklyPlanToMarkdown(session, folder);

            Log.Information("[SaveWeeklyPlan] 周计划已保存，SessionId={Id}", session.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SaveWeeklyPlan] 保存周计划失败，SessionId={Id}", session.Id);
        }
    }

    public string GetBlueprintText(DiagnosticSession session)
    {
        // 优先使用 BlueprintCard 的完整内容
        var canvas = session.Canvas;
        if (!string.IsNullOrEmpty(canvas.BlueprintCard?.FullContent))
            return canvas.BlueprintCard.FullContent;

        // 退而求其次：从对话历史中找 AI 的最后一条回复（Stage5 蓝图）
        var assistantMessages = session.Messages
            .Where(m => m.Role == MessageRole.Assistant)
            .ToList();

        return assistantMessages.LastOrDefault()?.Content ?? string.Empty;
    }

    // ─────────────────────────────────────────────────────────────────
    // 内部解析逻辑
    // ─────────────────────────────────────────────────────────────────

    private WeeklyPlan? TryParseWeeklyPlan(string response, string sessionId, string blueprintExcerpt, int totalWeeks)
    {
        var json = ExtractJson(response);
        if (string.IsNullOrEmpty(json)) return null;

        try
        {
            dynamic? parsed = JsonConvert.DeserializeObject<WeeklyPlanRaw>(json);
            if (parsed == null) return null;

            var plan = new WeeklyPlan
            {
                SessionId = sessionId,
                GeneratedAt = DateTime.Now,
                BlueprintExcerpt = blueprintExcerpt.Length > 600
                    ? blueprintExcerpt.Substring(0, 600) + "..."
                    : blueprintExcerpt,
                TotalWeeks = parsed.total_weeks ?? totalWeeks
            };

            foreach (var w in parsed.weeks ?? Enumerable.Empty<WeekPlanRaw>())
            {
                plan.Weeks.Add(new WeekPlan
                {
                    WeekNumber = w.week_number,
                    Title = w.title ?? $"第 {w.week_number} 周",
                    Outline = w.outline ?? ""
                });
            }

            // 保证有 4 周
            for (int i = 1; i <= totalWeeks; i++)
            {
                if (!plan.Weeks.Any(w => w.WeekNumber == i))
                    plan.Weeks.Add(new WeekPlan { WeekNumber = i, Title = $"第 {i} 周", Outline = "" });
            }
            plan.Weeks = plan.Weeks.OrderBy(w => w.WeekNumber).ToList();

            return plan;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[TryParseWeeklyPlan] JSON 解析异常，尝试部分解析");
            return TryPartialParseWeeklyPlan(response, sessionId, blueprintExcerpt, totalWeeks);
        }
    }

    private WeeklyPlan? TryPartialParseWeeklyPlan(string response, string sessionId, string blueprintExcerpt, int totalWeeks)
    {
        try
        {
            // 尝试找到 weeks 数组
            var weeksMatch = Regex.Match(response, @"""weeks""\s*:\s*\[([\s\S]+)\]", RegexOptions.Singleline);
            if (!weeksMatch.Success) return null;

            var plan = new WeeklyPlan
            {
                SessionId = sessionId,
                GeneratedAt = DateTime.Now,
                BlueprintExcerpt = blueprintExcerpt.Length > 600
                    ? blueprintExcerpt.Substring(0, 600) + "..."
                    : blueprintExcerpt,
                TotalWeeks = totalWeeks
            };

            // 简单提取 week_number, title, outline
            var itemMatches = Regex.Matches(weeksMatch.Groups[1].Value,
                @"\{[^}]+\}", RegexOptions.Singleline);

            foreach (var itemMatch in itemMatches)
            {
                var item = itemMatch.ToString() ?? "";
                var wnMatch = Regex.Match(item, @"""week_number""\s*:\s*(\d+)");
                var tiMatch = Regex.Match(item, @"""title""\s*:\s*""([^""]+)""");
                var ouMatch = Regex.Match(item, @"""outline""\s*:\s*""([\s\S]*?)""(?=,?\s*\})");

                if (wnMatch.Success)
                {
                    var wn = int.Parse(wnMatch.Groups[1].Value);
                    plan.Weeks.Add(new WeekPlan
                    {
                        WeekNumber = wn,
                        Title = tiMatch.Success ? tiMatch.Groups[1].Value : $"第 {wn} 周",
                        Outline = ouMatch.Success ? ouMatch.Groups[1].Value : ""
                    });
                }
            }

            if (plan.Weeks.Count > 0)
            {
                plan.Weeks = plan.Weeks.OrderBy(w => w.WeekNumber).ToList();
                return plan;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[TryPartialParseWeeklyPlan] 部分解析失败");
        }
        return null;
    }

    private List<DailyTask> TryParseDailyTasks(string response, int weekNumber)
    {
        var json = ExtractJson(response);
        if (string.IsNullOrEmpty(json)) return GenerateDefaultTasks(weekNumber);

        try
        {
            dynamic? parsed = JsonConvert.DeserializeObject<DailyTasksRaw>(json);
            if (parsed?.tasks == null) return GenerateDefaultTasks(weekNumber);

            var tasks = new List<DailyTask>();
            int idx = 1;
            foreach (var t in parsed.tasks)
            {
                var formatStr = (string)(t.format ?? "Generic");
                if (!Enum.TryParse<ContentFormat>(formatStr, true, out var format))
                    format = ContentFormat.Generic;

                tasks.Add(new DailyTask
                {
                    Index = idx++,
                    WeekNumber = weekNumber,
                    Title = (string)(t.title ?? "任务"),
                    Format = format,
                    Strategy = (string)(t.strategy ?? ""),
                    Audience = (string)(t.audience ?? ""),
                    Channel = (string)(t.channel ?? ""),
                    Hook = (string)(t.hook ?? ""),
                    Copywriting = (string)(t.copywriting ?? ""),
                    Notes = (string)(t.notes ?? ""),
                    GeneratedAt = DateTime.Now
                });
            }

            return tasks.Count > 0 ? tasks : GenerateDefaultTasks(weekNumber);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[TryParseDailyTasks] 解析失败，使用默认任务列表");
            return GenerateDefaultTasks(weekNumber);
        }
    }

    private static WeeklyPlan GenerateDefaultWeeklyPlan(string sessionId, string blueprintExcerpt, int totalWeeks)
    {
        var plan = new WeeklyPlan
        {
            SessionId = sessionId,
            GeneratedAt = DateTime.Now,
            BlueprintExcerpt = blueprintExcerpt.Length > 600
                ? blueprintExcerpt.Substring(0, 600) + "..."
                : blueprintExcerpt,
            TotalWeeks = totalWeeks
        };

        var defaultTitles = new[]
        {
            "认知建立周：让目标客户认识你的核心价值",
            "信任深化周：建立专业权威与真实案例背书",
            "转化加速周：设计转化链路与成交钩子",
            "复盘沉淀周：总结 SOP，准备规模化"
        };

        for (int i = 1; i <= totalWeeks; i++)
        {
            plan.Weeks.Add(new WeekPlan
            {
                WeekNumber = i,
                Title = defaultTitles[i - 1],
                Outline = $"- 明确本周核心目标\n- 产出本周主要物料（见周详情）\n- 评估本周效果并记录"
            });
        }

        return plan;
    }

    private static List<DailyTask> GenerateDefaultTasks(int weekNumber)
    {
        return new List<DailyTask>
        {
            new()
            {
                Index = 1, WeekNumber = weekNumber,
                Title = "撰写一篇认知建立类图文",
                Format = ContentFormat.Article,
                Strategy = "教育型软文 / 建立专业形象",
                Audience = "潜在目标客户",
                Channel = "公众号 / 知乎",
                Hook = "你是否也曾为 XXX 问题困扰多年？",
                Copywriting = "（请重新生成日常任务以获取完整文案）",
                Notes = ""
            }
        };
    }

    private static string? ExtractJson(string text)
    {
        // 优先尝试外层花括号匹配（最常见于 AI 输出的 JSON 对象）
        var match = Regex.Match(text, @"\{[\s\S]*\}", RegexOptions.Singleline);
        return match.Success ? match.Value : null;
    }

    private void ExportWeeklyPlanToMarkdown(DiagnosticSession session, string folder)
    {
        try
        {
            var plan = session.WeeklyPlan;
            if (plan == null) return;

            var sb = new StringBuilder();
            sb.AppendLine("# 周执行计划");
            sb.AppendLine();
            sb.AppendLine($"> 生成时间: {plan.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"> 来源会话: {session.Id}");
            sb.AppendLine();
            sb.AppendLine("---\n");

            foreach (var week in plan.Weeks)
            {
                sb.AppendLine($"## 第 {week.WeekNumber} 周：{week.Title}");
                sb.AppendLine();
                sb.AppendLine("### 大纲");
                sb.AppendLine();
                if (!string.IsNullOrEmpty(week.Outline))
                {
                    sb.AppendLine(week.Outline);
                }
                else
                {
                    sb.AppendLine("（暂无大纲）");
                }
                sb.AppendLine();

                sb.AppendLine("### 日常任务");
                sb.AppendLine();
                if (week.HasTasks)
                {
                    foreach (var task in week.DailyTasks)
                    {
                        sb.AppendLine($"#### {task.Index}. {task.Title}");
                        sb.AppendLine();
                        sb.AppendLine($"- **内容形态**: {task.Format}");
                        sb.AppendLine($"- **策略定位**: {task.Strategy}");
                        sb.AppendLine($"- **目标人群**: {task.Audience}");
                        sb.AppendLine($"- **分发渠道**: {task.Channel}");
                        sb.AppendLine();
                        if (!string.IsNullOrEmpty(task.Hook))
                        {
                            sb.AppendLine($"> **核心钩子**: {task.Hook}");
                            sb.AppendLine();
                        }
                        if (!string.IsNullOrEmpty(task.Copywriting))
                        {
                            sb.AppendLine("**文案正文**");
                            sb.AppendLine();
                            sb.AppendLine(task.Copywriting);
                            sb.AppendLine();
                        }
                        if (!string.IsNullOrEmpty(task.Notes))
                        {
                            sb.AppendLine($"> **备注**: {task.Notes}");
                            sb.AppendLine();
                        }
                        sb.AppendLine("---");
                        sb.AppendLine();
                    }
                }
                else
                {
                    sb.AppendLine("（日常任务尚未生成，请在周计划窗口中点击「生成日常任务」）");
                    sb.AppendLine();
                    sb.AppendLine("---");
                    sb.AppendLine();
                }
            }

            sb.AppendLine($"*\n> 以上内容由【商业超体】基于您的商业蓝图自动生成，可直接复制到下游内容生成工具使用。*");

            var mdFile = Path.Combine(folder, "周计划.md");
            File.WriteAllText(mdFile, sb.ToString(), Encoding.UTF8);
            Log.Information("[ExportWeeklyPlanToMarkdown] 已导出: {File}", mdFile);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[ExportWeeklyPlanToMarkdown] 导出失败");
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // JSON 反序列化用的扁平化结构（与 snake_case 映射对应）
    // ─────────────────────────────────────────────────────────────────

    private class WeeklyPlanRaw
    {
        public int? total_weeks { get; set; }
        public List<WeekPlanRaw> weeks { get; set; } = new();
    }

    private class WeekPlanRaw
    {
        public int week_number { get; set; }
        public string? title { get; set; }
        public string? outline { get; set; }
    }

    private class DailyTasksRaw
    {
        public int week_number { get; set; }
        public List<DailyTaskRaw> tasks { get; set; } = new();
    }

    private class DailyTaskRaw
    {
        public int index { get; set; }
        public string? title { get; set; }
        public string? format { get; set; }
        public string? strategy { get; set; }
        public string? audience { get; set; }
        public string? channel { get; set; }
        public string? hook { get; set; }
        public string? copywriting { get; set; }
        public string? notes { get; set; }
    }
}

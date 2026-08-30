using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using 商业超体价值与定位.Models;
using 商业超体价值与定位.Services;
using Serilog;

namespace 商业超体价值与定位.ViewModels;

/// <summary>
/// AI 回复中的诊断进度信息（百分比 + 6 项清单 + 下一步行动）。
/// </summary>
public class DiagnosticProgressInfo
{
    public double Progress { get; set; }
    public int NarrativePercentage { get; set; }
    public string Detail { get; set; } = string.Empty;
    public string NextAction { get; set; } = string.Empty;
    public List<ProgressChecklistItem> Checklist { get; set; } = new();
}

/// <summary>
/// 诊断清单项（如"独占资源明确"、"客户画像清晰"等）。
/// </summary>
public class ProgressChecklistItem
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
}

public partial class ChatViewModel : ObservableObject
{
    private readonly IConversationService _conversationService;
    private readonly ISessionService _sessionService;

    [ObservableProperty]
    private ObservableCollection<Message> _messages = new();

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private DiagnosticStage _currentStage = DiagnosticStage.NotStarted;

    [ObservableProperty]
    private double _messageFontSize = 18;

    [ObservableProperty]
    private int _inputMinLines = 3;

    [ObservableProperty]
    private bool _copyNotificationVisible;

    [ObservableProperty]
    private string _copyNotificationText = "";

    public event EventHandler<DiagnosticProgressInfo>? ProgressUpdated;

    public double[] FontSizeOptions { get; } = { 12, 14, 16, 18, 20, 24 };

    [RelayCommand]
    private void CopyMessage(string content)
    {
        try
        {
            System.Windows.Clipboard.SetText(content);
            CopyNotificationText = "已复制到剪贴板";
            CopyNotificationVisible = true;

            _ = System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    CopyNotificationVisible = false;
                });
            });

            Log.Information("消息已复制到剪贴板");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "复制到剪贴板时发生错误");
            CopyNotificationText = "复制失败";
            CopyNotificationVisible = true;
        }
    }

    [RelayCommand]
    private void IncreaseFontSize()
    {
        var currentIndex = Array.IndexOf(FontSizeOptions, FontSizeOptions.FirstOrDefault(f => f >= MessageFontSize));
        if (currentIndex < FontSizeOptions.Length - 1)
        {
            MessageFontSize = FontSizeOptions[currentIndex + 1];
        }
    }

    [RelayCommand]
    private void DecreaseFontSize()
    {
        var currentIndex = Array.IndexOf(FontSizeOptions, FontSizeOptions.LastOrDefault(f => f <= MessageFontSize));
        if (currentIndex > 0)
        {
            MessageFontSize = FontSizeOptions[currentIndex - 1];
        }
    }

    [RelayCommand]
    private void IncreaseInputHeight()
    {
        if (InputMinLines < 10)
        {
            InputMinLines += 2;
        }
    }

    [RelayCommand]
    private void DecreaseInputHeight()
    {
        if (InputMinLines > 2)
        {
            InputMinLines -= 2;
        }
    }

    public ChatViewModel(
        IConversationService conversationService,
        ISessionService sessionService)
    {
        _conversationService = conversationService;
        _sessionService = sessionService;
    }

    public async Task InitializeAsync()
    {
        var session = _sessionService.CurrentSession;
        if (session.Messages.Count > 0)
        {
            foreach (var msg in session.Messages)
            {
                Messages.Add(msg);
            }
            CurrentStage = session.CurrentStage;
        }
    }

    public async Task StartWelcomeMessageAsync()
    {
        // 检查是否已有欢迎消息
        if (Messages.Count > 0)
        {
            return;
        }

        var welcomeMessage = new Message
        {
            Role = MessageRole.Assistant,
            Content = "欢迎来到【商业超体】价值诊断引擎。\n\n" +
                      "我是您的AI战略顾问，将通过多轮深度对话，帮您挖掘商业底牌、构建护城河、锁定精准定位。\n\n" +
                      "请放心，这不是一次简单的问卷调查。我们会像老中医把脉一样，通过追问让您暴露出真正的商业核心资产。\n\n" +
                      "让我们从第一个问题开始：\n\n" +
                      "请问您目前从事什么行业/业务？您的产品或服务是什么？",
            Timestamp = DateTime.Now
        };

        Messages.Add(welcomeMessage);
        CurrentStage = DiagnosticStage.ExploringExclusiveResources;
        _sessionService.UpdateSession(session =>
        {
            session.Messages.Add(welcomeMessage);
            session.CurrentStage = CurrentStage;
        });
    }

    [RelayCommand]
    public async Task StartNewSessionAsync()
    {
        IsLoading = true;
        Log.Information("开始新的商业诊断会话");

        try
        {
            // 清空现有消息
            Messages.Clear();

            var welcomeMessage = new Message
            {
                Role = MessageRole.Assistant,
                Content = "欢迎来到【商业超体】价值诊断引擎。\n\n" +
                          "我是您的AI战略顾问，将通过多轮深度对话，帮您挖掘商业底牌、构建护城河、锁定精准定位。\n\n" +
                          "请放心，这不是一次简单的问卷调查。我们会像老中医把脉一样，通过追问让您暴露出真正的商业核心资产。\n\n" +
                          "让我们从第一个问题开始：\n\n" +
                          "请问您目前从事什么行业/业务？您的产品或服务是什么？",
                Timestamp = DateTime.Now
            };

            Messages.Add(welcomeMessage);
            CurrentStage = DiagnosticStage.ExploringExclusiveResources;
            _sessionService.UpdateSession(session =>
            {
                session.Messages.Clear();
                session.Messages.Add(welcomeMessage);
                session.CurrentStage = CurrentStage;
            });
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText))
            return;

        var userMessage = new Message
        {
            Role = MessageRole.User,
            Content = InputText.Trim(),
            Timestamp = DateTime.Now
        };

        Messages.Add(userMessage);
        _sessionService.UpdateSession(session => session.Messages.Add(userMessage));

        InputText = string.Empty;
        IsLoading = true;

        try
        {
            var assistantMessage = new Message
            {
                Role = MessageRole.Assistant,
                IsLoading = true,
                Timestamp = DateTime.Now
            };
            Messages.Add(assistantMessage);

            var response = await _conversationService.GetResponseAsync(Messages, CurrentStage);

            assistantMessage.Content = response.Content;
            assistantMessage.IsLoading = false;
            CurrentStage = response.NewStage;

            ParseAndUpdateProgress(response.Content);

            _sessionService.UpdateSession(session =>
            {
                session.Messages.Add(assistantMessage);
                session.CurrentStage = CurrentStage;
                session.LastModifiedAt = DateTime.Now;

                if (CurrentStage == DiagnosticStage.GeneratingBlueprint ||
                    CurrentStage == DiagnosticStage.Complete)
                {
                    session.Canvas.BlueprintCard.IsActivated = true;
                    session.Canvas.BlueprintCard.FullContent = assistantMessage.Content;
                    session.Canvas.BlueprintCard.Content = "商业蓝图已生成，点击查看完整方案";
                }
            });

            _sessionService.AutoSave();
        }
        catch (LlmApiKeyNotConfiguredException)
        {
            var errorMsg = Messages[^1];
            errorMsg.Content = "请先配置 API Key。\n\n点击菜单「设置」→「API 配置」，输入您的 DeepSeek API Key 后保存。";
            errorMsg.IsLoading = false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "获取AI响应时发生错误");
            var errorMsg = Messages[^1];
            errorMsg.Content = "抱歉，发生了错误。请检查网络连接和API配置后重试。";
            errorMsg.IsLoading = false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ClearHistory()
    {
        Messages.Clear();
        _ = StartNewSessionAsync();
    }

    private void ParseAndUpdateProgress(string content)
    {
        var progressMatch = System.Text.RegularExpressions.Regex.Match(
            content, @"【?商业诊断进度[：:]\s*(\d+)%?】?");

        if (!progressMatch.Success || !int.TryParse(progressMatch.Groups[1].Value, out var percentage))
            return;

        var progress = percentage / 100.0;
        var checklist = ExtractProgressChecklist(content);
        var nextAction = ExtractNextAction(content, percentage, checklist);
        var detail = ExtractProgressDetail(content);

        ProgressUpdated?.Invoke(this, new DiagnosticProgressInfo
        {
            Progress = progress,
            Detail = detail,
            Checklist = checklist,
            NextAction = nextAction,
            NarrativePercentage = percentage
        });
        Log.Information("解析到诊断进度: {Progress}%, 完成项: {Done}/{Total}, 下一步: {Next}",
            percentage,
            checklist.Count(c => c.IsCompleted),
            checklist.Count,
            nextAction);
    }

    /// <summary>
    /// 从 AI 回复中解析 6 项进度清单。
    /// 匹配类似 "- ✅ 独占资源明确（线下实操场景）" 或 "- ⏳ 客户画像清晰" 的行。
    /// </summary>
    private List<ProgressChecklistItem> ExtractProgressChecklist(string content)
    {
        var items = new List<ProgressChecklistItem>();

        // 先定位"商业诊断进度"行，从该行之后开始扫描
        var anchorMatch = System.Text.RegularExpressions.Regex.Match(
            content, @"【?商业诊断进度[：:]\s*\d+%?】?");
        var scan = anchorMatch.Success
            ? content.Substring(anchorMatch.Index + anchorMatch.Length)
            : content;

        // 每行匹配 "- ✅ xxx（描述）" / "- ⏳ xxx: 描述" / "- ❌ xxx"
        var lineRegex = new System.Text.RegularExpressions.Regex(
            @"^\s*[-*]\s*([✅⏳❌])\s*([^\n]+?)\s*$",
            System.Text.RegularExpressions.RegexOptions.Multiline);

        foreach (System.Text.RegularExpressions.Match m in lineRegex.Matches(scan))
        {
            var marker = m.Groups[1].Value;
            var raw = m.Groups[2].Value.Trim();

            // 过滤明显不是清单项的
            if (raw.Length < 2) continue;
            if (raw.StartsWith("#") || raw.StartsWith("【") || raw.StartsWith(">")) continue;

            // 把"名称（描述）"或"名称: 描述"拆开
            var splitRegex = new System.Text.RegularExpressions.Regex(@"^([^（(：:]+?)\s*[（(：:]\s*(.+?)\s*[）)]\s*$");
            var split = splitRegex.Match(raw);
            string name, desc;
            if (split.Success && split.Groups[1].Value.Trim().Length >= 2)
            {
                name = split.Groups[1].Value.Trim();
                desc = split.Groups[2].Value.Trim();
            }
            else
            {
                name = raw;
                desc = "";
            }

            items.Add(new ProgressChecklistItem
            {
                Name = name,
                Description = desc,
                IsCompleted = marker == "✅"
            });
        }

        return items;
    }

    /// <summary>
    /// 根据清单完成情况与 AI 文本内容，提取下一步行动指引。
    /// </summary>
    private string ExtractNextAction(string content, int percentage, List<ProgressChecklistItem> checklist)
    {
        // 优先从 AI 文本中识别明确的请求句式（包含"请"或"?"）
        var askPatterns = new[]
        {
            @"请确认[：:]?\s*([^\n。?]+)",
            @"请回答[：:]?\s*([^\n。?]+)",
            @"请明确[：:]?\s*([^\n。?]+)",
            @"请给出\s*([^\n。?]+)",
            @"请告诉我\s*([^\n。?]+)"
        };

        foreach (var pattern in askPatterns)
        {
            var m = System.Text.RegularExpressions.Regex.Match(content, pattern);
            if (m.Success)
            {
                var ask = m.Value.Trim().TrimEnd('?', '？', '.', '。');
                if (ask.Length > 1 && ask.Length < 80)
                    return $"👉 {ask}";
            }
        }

        // 兜底：基于清单状态给出通用指引
        if (percentage >= 100)
            return "✅ 全部维度已就绪，可生成终极商业蓝图";
        if (percentage >= 85 && checklist.All(c => c.IsCompleted))
            return "👉 请确认以上定位与策略，确认后系统将生成《终极商业蓝图》";
        if (percentage >= 70)
            return "👉 继续回答 AI 的追问以锁定最后的细节";

        var pending = checklist.Where(c => !c.IsCompleted).Select(c => c.Name).ToList();
        if (pending.Count > 0)
            return $"👉 还需完成：{string.Join("、", pending.Take(2))}";

        return "👉 继续与 AI 顾问对话";
    }

    private string ExtractProgressDetail(string content)
    {
        var details = new List<string>();

        if (content.Contains("独占资源") || content.Contains("核心资源"))
            details.Add("核心资源");
        if (content.Contains("目标客户") || content.Contains("客户画像"))
            details.Add("目标客户");
        if (content.Contains("隐性痛点") || content.Contains("痛点"))
            details.Add("隐性痛点");
        if (content.Contains("竞品") || content.Contains("竞争"))
            details.Add("竞品分析");
        if (content.Contains("信任") || content.Contains("案例"))
            details.Add("信任证据");
        if (content.Contains("交付") || content.Contains("模式"))
            details.Add("交付模式");

        return string.Join(" · ", details);
    }
}

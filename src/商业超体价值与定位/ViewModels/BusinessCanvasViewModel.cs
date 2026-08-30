using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using 商业超体价值与定位.Models;
using 商业超体价值与定位.Services;
using Serilog;

namespace 商业超体价值与定位.ViewModels;

public partial class BusinessCanvasViewModel : ObservableObject
{
    private readonly IBusinessCanvasService _canvasService;
    private readonly ISessionService _sessionService;
    private readonly IExportService _exportService;

    [ObservableProperty]
    private BusinessCanvas _canvas = new();

    [ObservableProperty]
    private MoatCard _moatCard = new();

    [ObservableProperty]
    private PainPointCard _painPointCard = new();

    [ObservableProperty]
    private EmotionalPremiumCard _emotionalPremiumCard = new();

    [ObservableProperty]
    private BlueprintCard _blueprintCard = new();

    [ObservableProperty]
    private double _completionPercentage;

    [ObservableProperty]
    private bool _showCompetitiveAnalysis;

    [ObservableProperty]
    private CompetitiveAnalysis? _competitiveAnalysis;

    [ObservableProperty]
    private bool _canGenerateBlueprint;

    [ObservableProperty]
    private string _diagnosticProgress = "0%";

    [ObservableProperty]
    private string _diagnosticProgressDetail = "";

    [ObservableProperty]
    private string _nextStepGuidance = "开始与AI顾问对话，让系统诊断您的商业价值";

    [ObservableProperty]
    private ObservableCollection<ProgressChecklistItem> _progressChecklist = new();

    /// <summary>
    /// 标记 AI 自述进度是否已设置。
    /// true 时，UpdateFromSessionAsync 不会用卡片激活进度覆盖它。
    /// </summary>
    private bool _narrativeCompletionOverridden;

    public BusinessCanvasViewModel(
        IBusinessCanvasService canvasService,
        ISessionService sessionService,
        IExportService exportService)
    {
        _canvasService = canvasService;
        _sessionService = sessionService;
        _exportService = exportService;
    }

    public void UpdateDiagnosticProgress(DiagnosticProgressInfo info)
    {
        // AI 自述进度作为"完成度"的主真值，与卡片激活解耦
        CompletionPercentage = info.Progress;
        DiagnosticProgress = $"{(int)(info.Progress * 100)}%";
        if (!string.IsNullOrEmpty(info.Detail))
        {
            DiagnosticProgressDetail = info.Detail;
        }

        // 同步更新 6 项进度清单
        ProgressChecklist.Clear();
        foreach (var item in info.Checklist)
        {
            ProgressChecklist.Add(item);
        }

        // 更新下一步指引
        if (!string.IsNullOrEmpty(info.NextAction))
        {
            NextStepGuidance = info.NextAction;
        }

        // 标记：AI 自述进度已设定，避免后续 UpdateFromSessionAsync 覆盖
        _narrativeCompletionOverridden = true;

        Log.Debug("画布诊断进度已更新: 完成度={Progress}%, 清单项={Count}, 下一步={Next}",
            info.NarrativePercentage, info.Checklist.Count, info.NextAction);
    }

    /// <summary>兼容旧签名（仅百分比 + 详情）。</summary>
    public void UpdateDiagnosticProgress(double percentage, string detail = "")
    {
        UpdateDiagnosticProgress(new DiagnosticProgressInfo
        {
            Progress = percentage,
            Detail = detail
        });
    }

    public async Task UpdateFromSessionAsync(DiagnosticSession session)
    {
        await _canvasService.UpdateCanvasFromSessionAsync(session);

        MoatCard = session.Canvas.MoatCard;
        PainPointCard = session.Canvas.PainPointCard;
        EmotionalPremiumCard = session.Canvas.EmotionalPremiumCard;
        BlueprintCard = session.Canvas.BlueprintCard;

        // 仅在 AI 自述进度尚未设定时，才从 session 恢复（启动/切会话场景）
        if (!_narrativeCompletionOverridden)
        {
            CompletionPercentage = session.Canvas.CompletionPercentage;
            DiagnosticProgress = $"{(int)(CompletionPercentage * 100)}%";
        }

        CanGenerateBlueprint = CompletionPercentage >= 0.8;

        if (CanGenerateBlueprint && CompetitiveAnalysis == null)
        {
            await GenerateCompetitiveAnalysisAsync();
        }
    }

    private async Task GenerateCompetitiveAnalysisAsync()
    {
        try
        {
            Log.Information("开始生成竞品分析");
            CompetitiveAnalysis = await _canvasService.GenerateCompetitiveAnalysisAsync(
                _sessionService.CurrentSession);
            ShowCompetitiveAnalysis = true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "生成竞品分析时发生错误");
        }
    }

    [ObservableProperty]
    private bool _isGeneratingBlueprint;

    [ObservableProperty]
    private string _blueprintStatus = "点击按钮查看商业蓝图";

    [RelayCommand]
    private async Task GenerateBlueprintAsync()
    {
        if (IsGeneratingBlueprint) return;

        try
        {
            IsGeneratingBlueprint = true;
            BlueprintStatus = "正在从对话中提取商业蓝图...";
            Log.Information("从对话历史中提取商业蓝图");

            var blueprint = await _canvasService.ExtractBlueprintFromConversationAsync(_sessionService.CurrentSession);
            BlueprintCard = blueprint;
            BlueprintStatus = "商业蓝图已提取完成";

            var exportWindow = new Views.BlueprintExportWindow(BlueprintCard);
            exportWindow.Show();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "提取商业蓝图时发生错误");
            BlueprintStatus = "提取失败，请重试";
        }
        finally
        {
            IsGeneratingBlueprint = false;
        }
    }

    [RelayCommand]
    private async Task ExportToPdfAsync()
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PDF文档|*.pdf",
                FileName = $"商业蓝图_{DateTime.Now:yyyyMMdd_HHmmss}.pdf",
                Title = "导出商业蓝图"
            };

            if (dialog.ShowDialog() == true)
            {
                await _exportService.ExportBlueprintToPdfAsync(BlueprintCard, dialog.FileName);
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{dialog.FileName}\"");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "导出PDF时发生错误");
        }
    }

    [RelayCommand]
    private void ToggleCompetitiveAnalysis()
    {
        ShowCompetitiveAnalysis = !ShowCompetitiveAnalysis;
    }

    public void Reset()
    {
        Canvas = new BusinessCanvas();
        MoatCard = new MoatCard();
        PainPointCard = new PainPointCard();
        EmotionalPremiumCard = new EmotionalPremiumCard();
        BlueprintCard = new BlueprintCard();
        CompletionPercentage = 0;
        CanGenerateBlueprint = false;
        CompetitiveAnalysis = null;
        ShowCompetitiveAnalysis = false;
        DiagnosticProgress = "0%";
        DiagnosticProgressDetail = "";
        NextStepGuidance = "开始与AI顾问对话，让系统诊断您的商业价值";
        ProgressChecklist.Clear();
        // 新会话：清空"AI 自述进度"标记，等待本会话内首次 AI 回复重新设定
        _narrativeCompletionOverridden = false;
    }
}

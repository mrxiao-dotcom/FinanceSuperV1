using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using 商业超体价值与定位.Models;
using 商业超体价值与定位.Services;

namespace 商业超体价值与定位.ViewModels;

/// <summary>
/// 周计划窗口的 ViewModel。
/// 职责：加载/生成四周大纲 → 选择某周 → 生成该周日常任务 → 查看/复制/导出任务。
/// 数据随 DiagnosticSession 持久化，切换会话即切换周计划。
/// </summary>
public partial class WeeklyPlanViewModel : ObservableObject
{
    private readonly IWeeklyPlanService _weeklyPlanService;
    private readonly ISessionService _sessionService;

    [ObservableProperty]
    private WeeklyPlan? _currentPlan;

    [ObservableProperty]
    private WeekPlan? _selectedWeek;

    [ObservableProperty]
    private ObservableCollection<WeekPlan> _weeks = new();

    [ObservableProperty]
    private ObservableCollection<DailyTask> _currentWeekTasks = new();

    [ObservableProperty]
    private bool _hasPlan;

    [ObservableProperty]
    private bool _hasOutline;

    [ObservableProperty]
    private bool _hasTasks;

    [ObservableProperty]
    private bool _isGeneratingOutline;

    [ObservableProperty]
    private bool _isGeneratingTasks;

    [ObservableProperty]
    private string _statusMessage = "请先生成四周执行大纲";

    [ObservableProperty]
    private string _selectedWeekTitle = "请先选择一周或生成大纲";

    [ObservableProperty]
    private string _selectedWeekOutline = "";

    [ObservableProperty]
    private string _planInfo = "尚未生成周计划";

    [ObservableProperty]
    private DailyTask? _selectedTask;

    public WeeklyPlanViewModel(
        IWeeklyPlanService weeklyPlanService,
        ISessionService sessionService)
    {
        _weeklyPlanService = weeklyPlanService;
        _sessionService = sessionService;
    }

    /// <summary>
    /// 窗口打开时调用：加载 prompts、加载会话中的已有周计划。
    /// </summary>
    public async Task InitializeAsync()
    {
        await _weeklyPlanService.LoadPromptsAsync();

        var session = _sessionService.CurrentSession;
        if (session.WeeklyPlan != null)
        {
            CurrentPlan = session.WeeklyPlan;
            RefreshFromPlan();
            StatusMessage = $"周计划已加载（共 {CurrentPlan.Weeks.Count} 周）";
            PlanInfo = $"生成于 {CurrentPlan.GeneratedAt:yyyy-MM-dd HH:mm}";
            Log.Information("[WeeklyPlanViewModel] 周计划已从会话加载，周数={Count}",
                CurrentPlan.Weeks.Count);
        }
        else
        {
            StatusMessage = "尚未生成周计划";
            PlanInfo = "请先生成四周执行大纲";
        }
    }

    private void RefreshFromPlan()
    {
        if (CurrentPlan == null) return;

        Weeks.Clear();
        foreach (var w in CurrentPlan.Weeks)
        {
            Weeks.Add(w);
        }
        HasPlan = true;

        // 默认选中第一周
        if (Weeks.Count > 0)
        {
            SelectedWeek = Weeks[0];
        }
    }

    partial void OnSelectedWeekChanged(WeekPlan? value)
    {
        CurrentWeekTasks.Clear();
        if (value == null)
        {
            SelectedWeekTitle = "请选择一周";
            SelectedWeekOutline = "";
            HasOutline = false;
            HasTasks = false;
            return;
        }

        SelectedWeekTitle = $"第 {value.WeekNumber} 周：{value.Title}";
        SelectedWeekOutline = value.Outline;
        HasOutline = !string.IsNullOrWhiteSpace(value.Outline);

        foreach (var task in value.DailyTasks)
        {
            CurrentWeekTasks.Add(task);
        }
        HasTasks = value.HasTasks;

        StatusMessage = HasTasks
            ? $"第 {value.WeekNumber} 周任务已生成，共 {value.DailyTasks.Count} 项"
            : $"第 {value.WeekNumber} 周大纲已就绪，请生成日常任务";
    }

    /// <summary>生成四周执行大纲。</summary>
    [RelayCommand]
    private async Task GenerateOutlineAsync()
    {
        if (IsGeneratingOutline) return;

        var session = _sessionService.CurrentSession;

        // 检查是否有蓝图内容
        var blueprintText = _weeklyPlanService.GetBlueprintText(session);
        if (string.IsNullOrWhiteSpace(blueprintText))
        {
            StatusMessage = "请先生成商业蓝图，再生成周计划";
            MessageBox.Show(
                "请先生成商业蓝图。\n\n周计划需要基于您的商业蓝图来规划，请先完成诊断对话并生成蓝图。",
                "提示",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            IsGeneratingOutline = true;
            StatusMessage = "正在生成四周执行大纲...";

            var plan = await _weeklyPlanService.GenerateWeeklyOutlineAsync(session);
            CurrentPlan = plan;
            session.WeeklyPlan = plan;

            RefreshFromPlan();
            _sessionService.AutoSave();

            PlanInfo = $"生成于 {plan.GeneratedAt:yyyy-MM-dd HH:mm}";
            StatusMessage = $"四周大纲生成完成！请选择某一周生成日常任务。";

            Log.Information("[GenerateOutlineAsync] 周计划生成成功，周数={Count}",
                plan.Weeks.Count);
        }
        catch (LlmApiKeyNotConfiguredException)
        {
            StatusMessage = "API Key 未配置，请在设置中配置后重试";
            MessageBox.Show("请先在「设置 → 大模型API配置」中配置 API Key。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            StatusMessage = $"生成失败：{ex.Message}";
            Log.Error(ex, "[GenerateOutlineAsync] 异常");
        }
        finally
        {
            IsGeneratingOutline = false;
        }
    }

    /// <summary>为当前选中周生成日常任务。</summary>
    [RelayCommand]
    private async Task GenerateTasksAsync()
    {
        if (IsGeneratingTasks || SelectedWeek == null) return;

        if (CurrentPlan == null)
        {
            StatusMessage = "请先生成四周大纲";
            return;
        }

        var session = _sessionService.CurrentSession;

        try
        {
            IsGeneratingTasks = true;
            StatusMessage = $"正在为第 {SelectedWeek.WeekNumber} 周生成日常任务...";

            CurrentWeekTasks.Clear();

            var week = await _weeklyPlanService.GenerateDailyTasksAsync(
                CurrentPlan, SelectedWeek.WeekNumber, session);

            // 刷新 UI
            foreach (var task in week.DailyTasks)
            {
                CurrentWeekTasks.Add(task);
            }

            HasTasks = true;
            _sessionService.AutoSave();

            StatusMessage = $"第 {SelectedWeek.WeekNumber} 周任务生成完成，共 {week.DailyTasks.Count} 项";

            Log.Information("[GenerateTasksAsync] 第 {Week} 周任务生成完成，任务数={Count}",
                SelectedWeek.WeekNumber, week.DailyTasks.Count);
        }
        catch (LlmApiKeyNotConfiguredException)
        {
            StatusMessage = "API Key 未配置，请在设置中配置后重试";
            MessageBox.Show("请先在「设置 → 大模型API配置」中配置 API Key。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            StatusMessage = $"生成失败：{ex.Message}";
            Log.Error(ex, "[GenerateTasksAsync] 异常");
        }
        finally
        {
            IsGeneratingTasks = false;
        }
    }

    /// <summary>复制当前选中任务的文案到剪贴板。</summary>
    [RelayCommand]
    private void CopyTaskCopywriting()
    {
        if (SelectedTask == null || string.IsNullOrEmpty(SelectedTask.Copywriting))
        {
            StatusMessage = "当前任务没有文案内容";
            return;
        }

        var text = $"【{SelectedTask.Title}】\n" +
                   $"格式：{SelectedTask.Format} | 策略：{SelectedTask.Strategy}\n" +
                   $"渠道：{SelectedTask.Channel} | 人群：{SelectedTask.Audience}\n" +
                   $"\n【核心钩子】\n{SelectedTask.Hook}\n" +
                   $"\n【正文文案】\n{SelectedTask.Copywriting}" +
                   (string.IsNullOrEmpty(SelectedTask.Notes) ? "" : $"\n\n【备注】\n{SelectedTask.Notes}");

        try
        {
            Clipboard.SetText(text);
            StatusMessage = $"「{SelectedTask.Title}」的文案已复制到剪贴板";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "复制到剪贴板失败");
            StatusMessage = "复制失败，请重试";
        }
    }

    /// <summary>复制某条任务的钩子字段到剪贴板。</summary>
    [RelayCommand]
    private void CopyTaskHook()
    {
        if (SelectedTask == null || string.IsNullOrEmpty(SelectedTask.Hook))
        {
            StatusMessage = "当前任务没有钩子内容";
            return;
        }

        try
        {
            Clipboard.SetText(SelectedTask.Hook);
            StatusMessage = "核心钩子已复制到剪贴板";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "复制钩子到剪贴板失败");
        }
    }

    /// <summary>导出当前周的所有任务到 Markdown 文件。</summary>
    [RelayCommand]
    private void ExportCurrentWeekToMarkdown()
    {
        if (SelectedWeek == null || !SelectedWeek.HasTasks)
        {
            StatusMessage = "当前周没有可导出的任务";
            return;
        }

        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Markdown文档|*.md",
                FileName = $"第{SelectedWeek.WeekNumber}周任务_{DateTime.Now:yyyyMMdd_HHmmss}.md",
                Title = "导出周任务"
            };

            if (dialog.ShowDialog() == true)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"# 第 {SelectedWeek.WeekNumber} 周任务：{SelectedWeek.Title}");
                sb.AppendLine();
                sb.AppendLine($"> 大纲：{SelectedWeek.Title}");
                sb.AppendLine($"> 生成时间：{DateTime.Now:yyyy-MM-dd HH:mm}");
                sb.AppendLine();

                foreach (var task in SelectedWeek.DailyTasks)
                {
                    sb.AppendLine($"## {task.Index}. {task.Title}");
                    sb.AppendLine();
                    sb.AppendLine($"- **内容形态**: {task.Format}");
                    sb.AppendLine($"- **策略定位**: {task.Strategy}");
                    sb.AppendLine($"- **目标人群**: {task.Audience}");
                    sb.AppendLine($"- **分发渠道**: {task.Channel}");
                    sb.AppendLine();
                    if (!string.IsNullOrEmpty(task.Hook))
                    {
                        sb.AppendLine($"> **核心钩子**\n>\n> {task.Hook}");
                        sb.AppendLine();
                    }
                    if (!string.IsNullOrEmpty(task.Copywriting))
                    {
                        sb.AppendLine("### 正文文案");
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

                System.IO.File.WriteAllText(dialog.FileName, sb.ToString(),
                    System.Text.Encoding.UTF8);

                System.Diagnostics.Process.Start("explorer.exe",
                    $"/select,\"{dialog.FileName}\"");

                StatusMessage = $"已导出到：{System.IO.Path.GetFileName(dialog.FileName)}";
                Log.Information("[ExportCurrentWeekToMarkdown] 导出成功: {File}", dialog.FileName);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "导出周任务 Markdown 失败");
            StatusMessage = "导出失败，请重试";
        }
    }

    /// <summary>导出整个周计划（所有 4 周）到 Markdown 文件。</summary>
    [RelayCommand]
    private void ExportFullPlanToMarkdown()
    {
        if (CurrentPlan == null)
        {
            StatusMessage = "请先生成周计划";
            return;
        }

        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Markdown文档|*.md",
                FileName = $"四周计划_{DateTime.Now:yyyyMMdd_HHmmss}.md",
                Title = "导出完整四周计划"
            };

            if (dialog.ShowDialog() == true)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("# 四周执行计划");
                sb.AppendLine();
                sb.AppendLine($"> 生成时间: {CurrentPlan.GeneratedAt:yyyy-MM-dd HH:mm}");
                sb.AppendLine($"> 会话 ID: {CurrentPlan.SessionId}");
                sb.AppendLine();
                sb.AppendLine("---\n");

                foreach (var week in CurrentPlan.Weeks)
                {
                    sb.AppendLine($"## 第 {week.WeekNumber} 周：{week.Title}");
                    sb.AppendLine();
                    sb.AppendLine("### 大纲");
                    sb.AppendLine();
                    sb.AppendLine(string.IsNullOrWhiteSpace(week.Outline)
                        ? "（暂无大纲）"
                        : week.Outline);
                    sb.AppendLine();
                    sb.AppendLine("### 日常任务");
                    sb.AppendLine();

                    if (week.HasTasks)
                    {
                        foreach (var task in week.DailyTasks)
                        {
                            sb.AppendLine($"#### {task.Index}. {task.Title}");
                            sb.AppendLine($"- **形态**: {task.Format} | **策略**: {task.Strategy}");
                            sb.AppendLine($"- **渠道**: {task.Channel} | **人群**: {task.Audience}");
                            if (!string.IsNullOrEmpty(task.Hook))
                                sb.AppendLine($"> **钩子**: {task.Hook}");
                            if (!string.IsNullOrEmpty(task.Copywriting))
                            {
                                sb.AppendLine();
                                sb.AppendLine(task.Copywriting);
                            }
                            if (!string.IsNullOrEmpty(task.Notes))
                                sb.AppendLine($"> 备注: {task.Notes}");
                            sb.AppendLine();
                            sb.AppendLine("---");
                            sb.AppendLine();
                        }
                    }
                    else
                    {
                        sb.AppendLine("（日常任务尚未生成）");
                        sb.AppendLine();
                        sb.AppendLine("---");
                        sb.AppendLine();
                    }
                }

                System.IO.File.WriteAllText(dialog.FileName, sb.ToString(),
                    System.Text.Encoding.UTF8);

                System.Diagnostics.Process.Start("explorer.exe",
                    $"/select,\"{dialog.FileName}\"");

                StatusMessage = $"四周计划已导出到：{System.IO.Path.GetFileName(dialog.FileName)}";
                Log.Information("[ExportFullPlanToMarkdown] 导出成功: {File}", dialog.FileName);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "导出四周计划 Markdown 失败");
            StatusMessage = "导出失败，请重试";
        }
    }

    /// <summary>重新生成当前选中周的任务（覆盖已有内容）。</summary>
    [RelayCommand]
    private async Task RegenerateTasksAsync()
    {
        if (IsGeneratingTasks || SelectedWeek == null) return;

        var result = MessageBox.Show(
            $"重新生成将覆盖第 {SelectedWeek.WeekNumber} 周的所有任务，确定继续？",
            "确认重新生成",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        // 清空旧任务
        SelectedWeek.DailyTasks.Clear();
        CurrentWeekTasks.Clear();

        await GenerateTasksAsync();
    }
}

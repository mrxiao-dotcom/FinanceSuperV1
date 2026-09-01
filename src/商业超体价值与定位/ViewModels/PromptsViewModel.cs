using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using 商业超体价值与定位.Services;

namespace 商业超体价值与定位.ViewModels;

/// <summary>
/// 提示词设置窗口的 ViewModel。
/// 左侧：所有 prompt 的元数据列表（点击切换）
/// 右侧：当前 prompt 的编辑器（描述 + 占位符 + 大文本框）
/// 底部：保存 / 恢复默认 / 重置所有
/// </summary>
public partial class PromptsViewModel : ObservableObject
{
    private readonly IPromptsService _promptsService;
    private readonly Dictionary<string, string> _workingCopies = new();

    [ObservableProperty]
    private ObservableCollection<PromptMeta> _promptList = new();

    [ObservableProperty]
    private PromptMeta? _selectedPrompt;

    [ObservableProperty]
    private string _currentPromptText = "";

    [ObservableProperty]
    private string _currentPromptDescription = "";

    [ObservableProperty]
    private string _currentPromptPlaceholders = "";

    [ObservableProperty]
    private string _currentPromptDisplayName = "";

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private string _statusMessage = "修改后请点击「保存」以应用";

    [ObservableProperty]
    private string _saveButtonText = "💾 保存修改";

    public PromptsViewModel(IPromptsService promptsService)
    {
        _promptsService = promptsService;

        // 初始化所有 prompt 的工作副本
        foreach (var kvp in _promptsService.GetAll())
        {
            _workingCopies[kvp.Key] = kvp.Value;
        }

        // 填充元数据列表
        foreach (var meta in _promptsService.ListMetadata())
        {
            PromptList.Add(meta);
        }

        // 默认选中第一个
        if (PromptList.Count > 0)
        {
            SelectedPrompt = PromptList[0];
        }
    }

    partial void OnSelectedPromptChanged(PromptMeta? value)
    {
        if (value == null) return;

        // 检测切换前是否需要提示
        if (HasUnsavedChanges)
        {
            var prevKey = SelectedPrompt?.Key;
            // 不阻塞切换（简单实现：直接切换）
            Log.Debug("[PromptsViewModel] 切换 prompt 时有未保存修改，prev={Prev}", prevKey);
        }

        CurrentPromptDisplayName = value.DisplayName;
        CurrentPromptDescription = value.Description;
        CurrentPromptPlaceholders = value.Placeholders != null && value.Placeholders.Count > 0
            ? string.Join(" / ", value.Placeholders)
            : "（无）";
        CurrentPromptText = _workingCopies.TryGetValue(value.Key, out var txt)
            ? txt
            : string.Empty;

        HasUnsavedChanges = false;
        SaveButtonText = "💾 保存修改";
        StatusMessage = $"已加载「{value.DisplayName}」";
    }

    partial void OnCurrentPromptTextChanged(string value)
    {
        // 当文本发生变化时，与工作副本比对，标记未保存状态
        if (SelectedPrompt == null) return;

        var key = SelectedPrompt.Key;
        if (_workingCopies.TryGetValue(key, out var original))
        {
            HasUnsavedChanges = original != value;
        }
        else
        {
            HasUnsavedChanges = !string.IsNullOrEmpty(value);
        }

        SaveButtonText = HasUnsavedChanges ? "💾 保存修改 *" : "💾 保存修改";
    }

    /// <summary>保存当前选中的 prompt。</summary>
    [RelayCommand]
    private void Save()
    {
        if (SelectedPrompt == null) return;

        _workingCopies[SelectedPrompt.Key] = CurrentPromptText;

        try
        {
            _promptsService.Save(_workingCopies);
            HasUnsavedChanges = false;
            SaveButtonText = "💾 保存修改";
            StatusMessage = $"✓ 已保存「{SelectedPrompt.DisplayName}」到 prompts.json";
            Log.Information("[PromptsViewModel] 已保存 prompt: {Key}", SelectedPrompt.Key);
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败：{ex.Message}";
            Log.Error(ex, "[PromptsViewModel] 保存失败");
        }
    }

    /// <summary>恢复当前选中的 prompt 到默认值。</summary>
    [RelayCommand]
    private void RestoreCurrent()
    {
        if (SelectedPrompt == null) return;

        var result = System.Windows.MessageBox.Show(
            $"确定要将「{SelectedPrompt.DisplayName}」恢复到出厂默认值吗？\n\n当前修改将会丢失。",
            "恢复默认",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            _promptsService.RestoreDefault(SelectedPrompt.Key);
            // 重新拉取
            var defaultValue = _promptsService.Get(SelectedPrompt.Key) ?? string.Empty;
            _workingCopies[SelectedPrompt.Key] = defaultValue;
            CurrentPromptText = defaultValue;
            HasUnsavedChanges = false;
            SaveButtonText = "💾 保存修改";
            StatusMessage = $"✓ 已恢复「{SelectedPrompt.DisplayName}」到出厂默认值";
            Log.Information("[PromptsViewModel] 已恢复默认: {Key}", SelectedPrompt.Key);
        }
        catch (Exception ex)
        {
            StatusMessage = $"恢复失败：{ex.Message}";
            Log.Error(ex, "[PromptsViewModel] 恢复失败");
        }
    }

    /// <summary>恢复所有 prompt 到出厂默认值。</summary>
    [RelayCommand]
    private void RestoreAll()
    {
        var result = System.Windows.MessageBox.Show(
            "确定要将所有 prompt 恢复到出厂默认值吗？\n\n所有自定义修改都将丢失，此操作不可撤销。",
            "恢复所有默认值",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            _promptsService.RestoreAllDefaults();
            foreach (var kvp in _promptsService.GetAll())
            {
                _workingCopies[kvp.Key] = kvp.Value;
            }

            // 刷新当前选中的文本
            if (SelectedPrompt != null)
            {
                CurrentPromptText = _workingCopies.TryGetValue(SelectedPrompt.Key, out var txt)
                    ? txt
                    : string.Empty;
            }

            HasUnsavedChanges = false;
            SaveButtonText = "💾 保存修改";
            StatusMessage = "✓ 已恢复所有 prompt 到出厂默认值";
            Log.Information("[PromptsViewModel] 已恢复所有默认");
        }
        catch (Exception ex)
        {
            StatusMessage = $"恢复失败：{ex.Message}";
            Log.Error(ex, "[PromptsViewModel] 全部恢复失败");
        }
    }
}
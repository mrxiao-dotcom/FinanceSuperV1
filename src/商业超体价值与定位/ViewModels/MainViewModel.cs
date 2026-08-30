using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using 商业超体价值与定位.Models;
using 商业超体价值与定位.Services;
using System.Timers;
using Timer = System.Timers.Timer;

namespace 商业超体价值与定位.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private ChatViewModel _chatViewModel;

    [ObservableProperty]
    private BusinessCanvasViewModel _businessCanvasViewModel;

    [ObservableProperty]
    private SessionListViewModel _sessionListViewModel;

    [ObservableProperty]
    private string _title = "商业超体：价值与定位";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "准备就绪";

    [ObservableProperty]
    private bool _isHistoryPanelVisible = true;

    private readonly Timer _debounceTimer;
    private readonly object _lockObject = new();
    private bool _isProcessing;
    private bool _isInitializing;

    public MainViewModel(
        ChatViewModel chatViewModel,
        BusinessCanvasViewModel businessCanvasViewModel,
        SessionListViewModel sessionListViewModel,
        ISessionService sessionService)
    {
        ChatViewModel = chatViewModel;
        BusinessCanvasViewModel = businessCanvasViewModel;
        SessionListViewModel = sessionListViewModel;

        _debounceTimer = new Timer(800);
        _debounceTimer.Elapsed += OnDebounceTimerElapsed;
        _debounceTimer.AutoReset = false;

        ChatViewModel.Messages.CollectionChanged += OnMessagesCollectionChanged;

        // 监听会话切换
        SessionListViewModel.SessionSwitched += OnSessionSwitched;
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        lock (_lockObject)
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    private async void OnDebounceTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (_isProcessing) return;

        lock (_lockObject)
        {
            if (_isProcessing) return;
            _isProcessing = true;
        }

        try
        {
            if (ChatViewModel.Messages.Count % 2 == 0 && ChatViewModel.Messages.Count > 0)
            {
                await ProcessConversationAsync();
            }
        }
        finally
        {
            lock (_lockObject)
            {
                _isProcessing = false;
            }
        }
    }

    private async Task ProcessConversationAsync()
    {
        StatusMessage = "正在提炼关键信息...";
        var extractor = App.ServiceProvider.GetRequiredService<IContentExtractorService>();
        var session = App.ServiceProvider.GetRequiredService<ISessionService>().CurrentSession;

        await extractor.ExtractAndUpdateAsync(ChatViewModel.Messages, session);
        await BusinessCanvasViewModel.UpdateFromSessionAsync(session);

        // 自动保存会话
        App.ServiceProvider.GetRequiredService<ISessionService>().AutoSave();

        // 刷新会话列表
        SessionListViewModel.LoadSessions();

        StatusMessage = "商业画布已更新";
    }

    private async void OnSessionSwitched(object? sender, string sessionId)
    {
        await SwitchToSessionAsync(sessionId);
    }

    private async Task SwitchToSessionAsync(string sessionId)
    {
        IsBusy = true;
        StatusMessage = "正在切换会话...";

        try
        {
            var sessionService = App.ServiceProvider.GetRequiredService<ISessionService>();

            // 保存当前会话
            sessionService.AutoSave();

            // 切换会话
            sessionService.SwitchSession(sessionId);
            var session = sessionService.CurrentSession;

            // 更新聊天视图
            _isInitializing = true;
            ChatViewModel.Messages.Clear();
            foreach (var msg in session.Messages)
            {
                ChatViewModel.Messages.Add(msg);
            }
            ChatViewModel.CurrentStage = session.CurrentStage;
            _isInitializing = false;

            // 更新商业画布
            await BusinessCanvasViewModel.UpdateFromSessionAsync(session);

            // 更新会话列表
            SessionListViewModel.NotifySessionSwitched(sessionId);

            StatusMessage = "会话已切换";
        }
        catch (Exception ex)
        {
            StatusMessage = $"切换会话失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ToggleHistoryPanel()
    {
        IsHistoryPanelVisible = !IsHistoryPanelVisible;
    }

    [RelayCommand]
    private async Task StartNewSessionAsync()
    {
        var sessionService = App.ServiceProvider.GetRequiredService<ISessionService>();
        sessionService.NewSession();

        _isInitializing = true;
        ChatViewModel.Messages.Clear();
        ChatViewModel.CurrentStage = DiagnosticStage.NotStarted;
        _isInitializing = false;

        BusinessCanvasViewModel.Reset();
        SessionListViewModel.LoadSessions();

        await ChatViewModel.StartNewSessionAsync();

        StatusMessage = "新会话已开始";
    }

    /// <summary>
    /// 手动提炼按钮 - 根据当前所有对话内容重新提炼
    /// </summary>
    [RelayCommand]
    private async Task ManualExtractAsync()
    {
        if (ChatViewModel.Messages.Count < 2)
        {
            StatusMessage = "对话内容不足，请先进行对话";
            return;
        }

        StatusMessage = "正在手动提炼关键信息...";
        IsBusy = true;

        try
        {
            var extractor = App.ServiceProvider.GetRequiredService<IContentExtractorService>();
            var session = App.ServiceProvider.GetRequiredService<ISessionService>().CurrentSession;

            await extractor.ExtractAndUpdateAsync(ChatViewModel.Messages, session);
            await BusinessCanvasViewModel.UpdateFromSessionAsync(session);

            // 保存会话
            App.ServiceProvider.GetRequiredService<ISessionService>().AutoSave();
            SessionListViewModel.LoadSessions();

            StatusMessage = "手动提炼完成";
        }
        catch (Exception ex)
        {
            StatusMessage = $"提炼失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 保存当前会话
    /// </summary>
    [RelayCommand]
    private void SaveSession()
    {
        try
        {
            var sessionService = App.ServiceProvider.GetRequiredService<ISessionService>();
            sessionService.SaveSession();
            SessionListViewModel.LoadSessions();
            StatusMessage = "会话已保存";
        }
        catch
        {
            StatusMessage = "保存失败";
        }
    }
}

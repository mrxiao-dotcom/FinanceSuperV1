using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using 商业超体价值与定位.Services;
using Serilog;

namespace 商业超体价值与定位.ViewModels;

public partial class SessionListViewModel : ObservableObject
{
    private readonly ISessionService _sessionService;

    [ObservableProperty]
    private ObservableCollection<SessionInfo> _sessions = new();

    [ObservableProperty]
    private SessionInfo? _selectedSession;

    [ObservableProperty]
    private string _currentSessionId = "";

    public event EventHandler<string>? SessionSwitched;
    public event EventHandler? RequestDeleteConfirmation;

    public SessionListViewModel(ISessionService sessionService)
    {
        _sessionService = sessionService;
        LoadSessions();
    }

    public void LoadSessions()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            Sessions.Clear();
            var currentId = _sessionService.CurrentSessionId ?? "";
            foreach (var session in _sessionService.SessionHistory)
            {
                session.IsCurrentSession = session.Id == currentId;
                Sessions.Add(session);
            }
            CurrentSessionId = currentId;
        });
    }

    [RelayCommand]
    private void SwitchToSession(SessionInfo? session)
    {
        if (session == null)
            return;

        if (session.Id == CurrentSessionId)
            return;

        SessionSwitched?.Invoke(this, session.Id);
    }

    [RelayCommand]
    private void DeleteSession(SessionInfo? session)
    {
        if (session == null)
        {
            Log.Warning("删除会话失败：未选中任何会话");
            return;
        }

        Log.Information("请求删除会话: {SessionId}, Title: {Title}", session.Id, session.Title);
        _pendingDeleteSession = session;
        RequestDeleteConfirmation?.Invoke(this, EventArgs.Empty);
    }

    private SessionInfo? _pendingDeleteSession;

    public string? GetPendingDeleteSession()
    {
        return _pendingDeleteSession?.Title;
    }

    public void ConfirmDelete()
    {
        if (_pendingDeleteSession == null)
        {
            Log.Warning("ConfirmDelete 被调用，但没有待删除的会话");
            return;
        }

        var sessionToDelete = _pendingDeleteSession;
        var sessionId = sessionToDelete.Id;
        var isCurrentSession = sessionId == CurrentSessionId;
        _pendingDeleteSession = null;

        Log.Information("确认删除会话: {SessionId}", sessionId);

        try
        {
            _sessionService.DeleteSession(sessionId);

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                LoadSessions();
                Log.Information("从列表中移除会话，剩余: {Count}", Sessions.Count);
            });

            if (isCurrentSession)
            {
                // 删除的是当前会话，需要切换到另一个会话
                var anotherSession = Sessions.FirstOrDefault();
                if (anotherSession != null)
                {
                    Log.Information("删除当前会话后，自动切换到: {SessionId}", anotherSession.Id);
                    SessionSwitched?.Invoke(this, anotherSession.Id);
                }
                else
                {
                    // 没有其他会话了，清空当前会话
                    SelectedSession = null;
                    CurrentSessionId = "";
                }
            }
            else if (SelectedSession?.Id == sessionId)
            {
                SelectedSession = null;
            }

            Log.Information("会话已删除: {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "删除会话失败: {SessionId}", sessionId);
        }
    }

    public void CancelDelete()
    {
        _pendingDeleteSession = null;
    }

    public void NotifySessionSwitched(string sessionId)
    {
        CurrentSessionId = sessionId;
        SelectedSession = Sessions.FirstOrDefault(s => s.Id == sessionId);
        LoadSessions();
    }
}

using CommunityToolkit.Mvvm.ComponentModel;

namespace 商业超体价值与定位.Models;

public partial class Message : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString();

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private MessageRole _role;

    [ObservableProperty]
    private DateTime _timestamp = DateTime.Now;

    [ObservableProperty]
    private bool _isLoading;

    public bool IsUser => Role == MessageRole.User;
    public bool IsAssistant => Role == MessageRole.Assistant;
}

public enum MessageRole
{
    User,
    Assistant,
    System
}

using System.Windows.Controls;
using System.Windows.Threading;
using 商业超体价值与定位.ViewModels;

namespace 商业超体价值与定位.Views;

public partial class ChatPanel : UserControl
{
    public ChatPanel()
    {
        InitializeComponent();
        Loaded += ChatPanel_Loaded;
        DataContextChanged += ChatPanel_DataContextChanged;
    }

    private void ChatPanel_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ChatViewModel viewModel)
        {
            viewModel.Messages.CollectionChanged += Messages_CollectionChanged;
            _ = viewModel.InitializeAsync();
        }
    }

    private void ChatPanel_DataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ChatViewModel oldVm)
        {
            oldVm.Messages.CollectionChanged -= Messages_CollectionChanged;
        }

        if (e.NewValue is ChatViewModel newVm)
        {
            newVm.Messages.CollectionChanged += Messages_CollectionChanged;
        }
    }

    private void Messages_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            MessagesScrollViewer.ScrollToEnd();
        }), DispatcherPriority.Background);
    }
}

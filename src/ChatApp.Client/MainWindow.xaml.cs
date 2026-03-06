using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ChatApp.Core.Models;
using ChatApp.Client.ViewModels;

namespace ChatApp.Client;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is MainViewModel vm)
        {
            // Auto-scroll whenever a new message is added
            vm.Messages.CollectionChanged += (_, args) =>
            {
                if (args.Action == NotifyCollectionChangedAction.Add)
                    Dispatcher.BeginInvoke(ScrollToBottom);
            };
        }
    }

    private void ScrollToBottom()
    {
        if (MessagesScroller is not null)
            MessagesScroller.ScrollToEnd();
    }

    // Contacts list click → open conversation
    private void ContactItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PeerUser peer }
            && DataContext is MainViewModel vm
            && vm.SelectPeerCommand.CanExecute(peer))
        {
            vm.SelectPeerCommand.Execute(peer);
        }
    }

    // Enter to send a message (Shift+Enter could add a new line if desired)
    private void MessageInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyboardDevice.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            if (DataContext is MainViewModel vm && vm.SendMessageCommand.CanExecute(null))
                vm.SendMessageCommand.Execute(null);
        }
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is MainViewModel vm)
            await vm.LogoutAsync();
    }
}

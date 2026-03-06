using System.Windows;
using ChatApp.Client.ViewModels;

namespace ChatApp.Client;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is MainViewModel vm)
            await vm.LogoutAsync();
    }
}


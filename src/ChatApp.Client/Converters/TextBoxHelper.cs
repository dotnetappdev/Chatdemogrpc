using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ChatApp.Client.Converters;

/// <summary>
/// Attached property that lets you bind an ICommand to the Enter key on a TextBox,
/// without needing code-behind per control.
/// </summary>
public static class TextBoxHelper
{
    public static readonly DependencyProperty EnterCommandProperty =
        DependencyProperty.RegisterAttached(
            "EnterCommand",
            typeof(ICommand),
            typeof(TextBoxHelper),
            new PropertyMetadata(null, OnEnterCommandChanged));

    public static void SetEnterCommand(DependencyObject element, ICommand value) =>
        element.SetValue(EnterCommandProperty, value);

    public static ICommand? GetEnterCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(EnterCommandProperty);

    private static void OnEnterCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb) return;

        tb.KeyDown -= OnKeyDown;
        if (e.NewValue is not null)
            tb.KeyDown += OnKeyDown;
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (e.KeyboardDevice.Modifiers != ModifierKeys.None) return;

        if (sender is DependencyObject d)
        {
            var cmd = GetEnterCommand(d);
            if (cmd?.CanExecute(null) == true)
            {
                cmd.Execute(null);
                e.Handled = true;
            }
        }
    }
}

using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Catan.WpfLauncher;

public partial class LauncherMessageDialog : Window
{
    public LauncherMessageDialog(string title, string message)
    {
        InitializeComponent();
        Title = title;
        WindowCaptionText.Text = title;
        DialogTitleText.Text = title;
        DialogMessageText.Text = message;
    }

    public static void ShowWarning(Window? owner, string title, string message)
    {
        var dialog = new LauncherMessageDialog(title, message)
        {
            Owner = owner
        };

        dialog.ShowDialog();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || IsInteractiveElement(e.OriginalSource as DependencyObject))
            return;

        DragMove();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static bool IsInteractiveElement(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is ButtonBase)
                return true;

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }
}

using System.Windows;
using System.Windows.Controls;
using WinGetUpdater.Services;
using WinGetUpdater.ViewModels;

namespace WinGetUpdater.Views;

public partial class ShellWindow : Window
{
    public ShellWindow()
    {
        InitializeComponent();

        try
        {
            var shell = new ShellVm();
            DataContext = shell;
            Page.Shell = shell;
        }
        catch (Exception ex)
        {
            // Ohne diesen Zweig bliebe bei einem Fehler im Aufbau nur ein leeres Fenster.
            ErrorLog.Instance.Error(nameof(ShellWindow), "Die Anwendung ließ sich nicht aufbauen.", ex);
            Content = BuildFatalView(ex);
        }
    }

    private static UIElement BuildFatalView(Exception exception)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(48),
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 720
        };

        panel.Children.Add(new TextBlock
        {
            Text = Localizer.Instance["Shell.FatalTitle"],
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        panel.Children.Add(new TextBlock
        {
            Text = exception.Message,
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });

        panel.Children.Add(new TextBox
        {
            Text = exception.ToString(),
            IsReadOnly = true,
            Margin = new Thickness(0, 16, 0, 0),
            MaxHeight = 320,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontSize = 11
        });

        if (ErrorLog.Instance.FilePath.Length > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = ErrorLog.Instance.FilePath,
                Margin = new Thickness(0, 12, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11
            });
        }

        return panel;
    }
}

using System.Windows;

namespace WinGetUpdater.Views;

/// <summary>
/// Fragt einmalig beim Start, ob die Anwendung als Administrator neu gestartet werden soll,
/// wenn sie das noch nicht ist. Reine Anzeige- und Klick-Logik - der eigentliche Neustart
/// (<see cref="Services.ElevationService.TryRelaunchElevated"/>) und das Beenden des aktuellen
/// Prozesses passieren im Aufrufer (<see cref="App"/>), der danach noch aufräumen muss.
/// </summary>
public partial class ElevationPromptWindow : Window
{
    public bool RestartRequested { get; private set; }

    public ElevationPromptWindow()
    {
        InitializeComponent();
    }

    private void OnRestart(object sender, RoutedEventArgs e)
    {
        RestartRequested = true;
        Close();
    }

    private void OnContinue(object sender, RoutedEventArgs e)
    {
        RestartRequested = false;
        Close();
    }
}

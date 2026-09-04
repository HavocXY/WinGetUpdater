using System.Collections.ObjectModel;
using System.Windows;
using WinGetUpdater.Models;
using WinGetUpdater.Services;

namespace WinGetUpdater.ViewModels;

public sealed class NavGroup
{
    public required string Label { get; init; }
    public required IReadOnlyList<CommandSpec> Commands { get; init; }
}

/// <summary>
/// Die Anwendung hat zwei Gesichter: die Hauptansicht kann genau eine Sache - vorhandene
/// Programme aktualisieren. Wer mehr braucht, wechselt bewusst nach "Alle Befehle" und
/// bekommt dort die vollstaendige winget-Oberflaeche.
/// </summary>
public enum AppMode { Updates, Advanced }

public sealed class ShellVm : ObservableObject
{
    private readonly Dictionary<string, CommandVm> _pages = new(StringComparer.Ordinal);
    private readonly CommandLineBuilder _builder;
    private readonly IWingetRunner? _runner;

    private AppMode _mode = AppMode.Updates;
    private CommandVm? _current;
    private string _navFilter = "";
    private bool _darkTheme = true;
    private bool _showLog;

    public ShellVm()
    {
        Store = SchemaStore.Load();
        _builder = new CommandLineBuilder(Store);

        Winget = WingetLocator.Locate();
        if (Winget is not null)
        {
            _runner = new WingetRunner(Winget.ExePath);
            Updates = new UpdateVm(Store, _builder, _runner);
        }

        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        ToggleLanguageCommand = new RelayCommand(ToggleLanguage);
        SelectCommand = new RelayCommand(p => { if (p is CommandSpec spec) Select(spec.Id); });
        PackageActionCommand = new RelayCommand(p => { if (p is string target) RunPackageAction(target); });
        ShowUpdatesCommand = new RelayCommand(() => Mode = AppMode.Updates);
        ShowAdvancedCommand = new RelayCommand(() => Mode = AppMode.Advanced);
        ToggleLogCommand = new RelayCommand(() => ShowLog = !ShowLog);
        OpenLogFileCommand = new RelayCommand(ErrorLog.Instance.OpenFile);
        ClearLogCommand = new RelayCommand(ErrorLog.Instance.Clear);
        CopyLogCommand = new RelayCommand(CopyLog);
        RestartElevatedCommand = new RelayCommand(RestartElevated, () => !IsElevated);

        // Die Seiten (CommandVm) und die Update-Liste (UpdateVm) registrieren ihre eigenen
        // lokalisierten Eigenschaften selbst - hier bleiben nur die, die zur Schale gehören.
        RegisterLocalized();

        BuildNavigation();
        if (Winget is not null) Select("search");
    }

    public SchemaStore Store { get; }
    public WingetInfo? Winget { get; }
    public UpdateVm? Updates { get; }
    public bool WingetAvailable => Winget is not null;
    public bool IsElevated => ElevationService.IsProcessElevated;
    public Localizer Loc => Localizer.Instance;
    public ErrorLog Log => ErrorLog.Instance;

    public ObservableCollection<NavGroup> Navigation { get; } = new();
    public ObservableCollection<string> SelectedPackageIds { get; } = new();

    public RelayCommand ToggleThemeCommand { get; }
    public RelayCommand ToggleLanguageCommand { get; }
    public RelayCommand SelectCommand { get; }
    public RelayCommand PackageActionCommand { get; }
    public RelayCommand ShowUpdatesCommand { get; }
    public RelayCommand ShowAdvancedCommand { get; }
    public RelayCommand ToggleLogCommand { get; }
    public RelayCommand OpenLogFileCommand { get; }
    public RelayCommand ClearLogCommand { get; }
    public RelayCommand CopyLogCommand { get; }
    public RelayCommand RestartElevatedCommand { get; }

    public AppMode Mode
    {
        get => _mode;
        set
        {
            if (!Set(ref _mode, value)) return;
            OnPropertyChanged(nameof(IsUpdatesMode));
            OnPropertyChanged(nameof(IsAdvancedMode));
        }
    }

    public bool IsUpdatesMode => _mode == AppMode.Updates;
    public bool IsAdvancedMode => _mode == AppMode.Advanced;

    public bool ShowLog
    {
        get => _showLog;
        set => Set(ref _showLog, value);
    }

    [Localized]
    public string WingetSummary => Winget is null
        ? Localizer.Instance["Winget.NotFound"]
        : $"winget {Winget.Version.TrimStart('v')}";

    [Localized]
    public string WingetTooltip => Winget is null
        ? Localizer.Instance["Winget.NotFoundHint"]
        : $"{Winget.ExePath}\n{Localizer.Instance.Format("Winget.FoundVia", Winget.HowFound)}";

    [Localized]
    public string CommandCountText => Localizer.Instance.Format("Nav.CommandCount", Store.Commands.Count);

    public CommandVm? Current
    {
        get => _current;
        private set => Set(ref _current, value);
    }

    public string NavFilter
    {
        get => _navFilter;
        set { if (Set(ref _navFilter, value ?? "")) BuildNavigation(); }
    }

    public bool NavigationEmpty => Navigation.Count == 0;

    public void Select(string commandId)
    {
        var spec = Store.Find(commandId);
        if (spec is null || _runner is null)
        {
            if (spec is null)
                ErrorLog.Instance.Warn(nameof(ShellVm), $"Unbekannter Befehl angefordert: {commandId}");
            return;
        }

        if (!_pages.TryGetValue(commandId, out var page))
        {
            page = new CommandVm(spec, Store, _builder, _runner);
            _pages[commandId] = page;
        }

        // Die Betriebsart bleibt unberuehrt: der Aufruf aus dem Konstruktor darf die
        // Hauptansicht nicht wegschalten. Wer die Befehlsliste sehen will, wechselt oben.
        SelectedPackageIds.Clear();
        Current = page;
        OnPropertyChanged(nameof(CurrentCommandId));
    }

    public string CurrentCommandId => Current?.Spec.Id ?? "";

    /// <summary>
    /// Uebernimmt die im Gitter markierten Pakete in einen anderen Befehl. Ausgefuehrt wird
    /// nichts von allein - die fertige Befehlszeile steht sichtbar da und wartet auf den Klick.
    /// </summary>
    private void RunPackageAction(string targetCommandId)
    {
        var ids = SelectedPackageIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        if (ids.Count == 0) return;

        Select(targetCommandId);
        var page = Current;
        if (page is null) return;

        page.Preset("id", ids[0]);
        page.PresetFlag("exact", true);

        page.BatchIds.Clear();
        if (ids.Count > 1)
        {
            page.BatchIds.AddRange(ids);
            page.Output.Clear();
        }
    }

    private void CopyLog()
    {
        try
        {
            Clipboard.SetText(ErrorLog.Instance.CopyText());
        }
        catch (Exception ex)
        {
            ErrorLog.Instance.Warn(nameof(ShellVm), "Das Protokoll ließ sich nicht kopieren.", ex);
        }
    }

    /// <summary>
    /// Zweite Gelegenheit fuer alle, die den Neustart-Hinweis beim Programmstart abgelehnt
    /// (oder die UAC-Abfrage dort abgebrochen) haben: der Warn-Chip in der Kopfzeile ruft das
    /// hier direkt auf, ohne weitere Rueckfrage - der Klick auf den beschrifteten Chip ist die
    /// Bestaetigung. Bei Erfolg beendet sich dieser, nicht erhoehte Prozess selbst.
    /// </summary>
    private void RestartElevated()
    {
        if (ElevationService.TryRelaunchElevated())
            Application.Current.Shutdown();
    }

    private void BuildNavigation()
    {
        Navigation.Clear();
        var filter = _navFilter.Trim();

        foreach (var group in Store.Groups)
        {
            var commands = Store.Commands
                .Where(c => c.Group == group.Id)
                .Where(Matches)
                .ToList();

            if (commands.Count > 0)
                Navigation.Add(new NavGroup { Label = group.Label.Get(Localizer.Instance.Language), Commands = commands });
        }

        OnPropertyChanged(nameof(NavigationEmpty));

        bool Matches(CommandSpec command)
        {
            if (filter.Length == 0) return true;
            if (command.Title.Get(Localizer.Instance.Language).Contains(filter, StringComparison.OrdinalIgnoreCase)) return true;
            if (command.CommandLine.Contains(filter, StringComparison.OrdinalIgnoreCase)) return true;
            return command.Aliases?.Any(a => a.Contains(filter, StringComparison.OrdinalIgnoreCase)) == true;
        }
    }

    private void ToggleLanguage()
    {
        // Der Toggle löst LanguageChanged aus: jede lokalisierte Eigenschaft der Schale,
        // der Befehlsseiten und der Update-Liste meldet sich damit selbst neu. Es bleibt
        // nur der Navigationseinstellung, die ihre Labels neu baut.
        Localizer.Instance.Toggle();
        BuildNavigation();
    }

    /// <summary>Fuer das Sonne/Mond-Symbol am Umschalter - zeigt, wohin der naechste Klick fuehrt.</summary>
    public bool IsDarkTheme
    {
        get => _darkTheme;
        private set => Set(ref _darkTheme, value);
    }

    private void ToggleTheme()
    {
        IsDarkTheme = !_darkTheme;
        var uri = new Uri(_darkTheme ? "Resources/Themes/Dark.xaml" : "Resources/Themes/Light.xaml",
                          UriKind.Relative);

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        if (dictionaries.Count > 0)
            dictionaries[0] = new ResourceDictionary { Source = uri };
    }
}

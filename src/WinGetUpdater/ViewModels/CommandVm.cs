using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using WinGetUpdater.Models;
using WinGetUpdater.Services;

namespace WinGetUpdater.ViewModels;

public enum RunState { Idle, Running, Succeeded, Failed, Canceled }

public sealed record OutputLine(string Text, LineKind Kind);

/// <summary>Eine Zeile der geparsten Ergebnistabelle. Der Indexer bedient die dynamisch erzeugten Spalten.</summary>
public sealed class TableRowVm
{
    private readonly string[] _cells;

    public TableRowVm(string[] cells, string packageId)
    {
        _cells = cells;
        PackageId = packageId;
    }

    public string this[int index] => index >= 0 && index < _cells.Length ? _cells[index] : "";
    public string PackageId { get; }
}

/// <summary>
/// Eine Befehlsseite. Alles Sichtbare - welche Felder es gibt, wie sie heissen, was die
/// Vorschau zeigt - folgt aus dem Schema und den Eingaben; hier steht keine Sonderbehandlung
/// fuer einzelne winget-Befehle.
/// </summary>
public sealed class CommandVm : ObservableObject
{
    private readonly SchemaStore _store;
    private readonly CommandLineBuilder _builder;
    private readonly IWingetRunner _runner;

    private readonly List<OutputLine> _pending = new();
    private bool _flushScheduled;

    private CancellationTokenSource? _cancellation;
    private string _extraArguments = "";
    private bool _elevated;
    private RunState _state = RunState.Idle;
    private string _statusText = "";
    private TableResult? _table;
    private string _tableFilter = "";
    private bool _showTable;
    private int _rowCount;

    public CommandVm(CommandSpec spec, SchemaStore store, CommandLineBuilder builder, IWingetRunner runner)
    {
        Spec = spec;
        _store = store;
        _builder = builder;
        _runner = runner;

        PrimaryOptions = Build(spec.Positional is null
            ? spec.Primary
            : new[] { spec.Positional }.Concat(spec.Primary.Where(p => p != spec.Positional)).ToList());
        AdvancedOptions = Build(spec.Advanced);
        GlobalOptions = Build(store.Globals);

        // Ohne dies wartet ein unsichtbarer Prozess auf eine Eingabe, die niemand sehen kann.
        var noInteractivity = GlobalOptions.FirstOrDefault(o => o.Spec.Id == "disableInteractivity");
        if (noInteractivity is not null) noInteractivity.BoolValue = true;

        _elevated = ElevationService.Advise(Spec, CurrentValues()).Recommended && !ElevationService.IsProcessElevated;

        RunCommand = new AsyncRelayCommand(RunAsync, () => State != RunState.Running);
        CancelCommand = new RelayCommand(() => _cancellation?.Cancel(), () => State == RunState.Running);
        ToggleOptionsCommand = new RelayCommand(() => OptionsVisible = !OptionsVisible);
        CopyCommand = new RelayCommand(CopyPreview);
        SaveScriptCommand = new RelayCommand(SaveScript);
        ResetCommand = new RelayCommand(Reset);
        OpenDocsCommand = new RelayCommand(OpenDocs);

        UpdatePreview();
    }

    public CommandSpec Spec { get; }
    public Localizer Loc => Localizer.Instance;

    public string Title => Spec.Title.Get(Localizer.Instance.Language);
    public string Description => Spec.Desc.Get(Localizer.Instance.Language);
    public string CommandPath => Spec.CommandLine;
    public bool IsDangerous => Spec.Danger;

    public IReadOnlyList<OptionVm> PrimaryOptions { get; }
    public IReadOnlyList<OptionVm> AdvancedOptions { get; }
    public IReadOnlyList<OptionVm> GlobalOptions { get; }

    // Schalter und Werteingaben werden getrennt dargestellt: Schalter als Kachelfeld,
    // Werte als beschriftete Zeilen. Das haelt auch 41 Optionen uebersichtlich.
    public IReadOnlyList<OptionVm> PrimaryValues => PrimaryOptions.Where(o => !o.IsFlag).ToList();
    public IReadOnlyList<OptionVm> PrimaryFlags => PrimaryOptions.Where(o => o.IsFlag).ToList();
    public IReadOnlyList<OptionVm> AdvancedValues => AdvancedOptions.Where(o => !o.IsFlag).ToList();
    public IReadOnlyList<OptionVm> AdvancedFlags => AdvancedOptions.Where(o => o.IsFlag).ToList();
    public IReadOnlyList<OptionVm> GlobalValues => GlobalOptions.Where(o => !o.IsFlag).ToList();
    public IReadOnlyList<OptionVm> GlobalFlags => GlobalOptions.Where(o => o.IsFlag).ToList();

    public bool HasAdvanced => AdvancedOptions.Count > 0;
    public bool HasAnyOption => PrimaryOptions.Count > 0 || AdvancedOptions.Count > 0;
    public bool CanShowTable => Spec.ParsedOutput == OutputKind.Table;
    public bool IsPackageList => Spec.Id is "search" or "list" or "upgrade";
    public string AdvancedHeader =>
        $"{Localizer.Instance["Options.Advanced"]} · {Localizer.Instance.Format("Options.Count", AdvancedOptions.Count)}";
    public string GlobalHeader =>
        $"{Localizer.Instance["Options.Global"]} · {Localizer.Instance.Format("Options.Count", GlobalOptions.Count)}";

    public ObservableCollection<OutputLine> Output { get; } = new();
    public ObservableCollection<TableRowVm> Rows { get; } = new();

    /// <summary>Wird gesetzt, wenn mehrere Pakete nacheinander bearbeitet werden sollen.</summary>
    public List<string> BatchIds { get; } = new();

    public AsyncRelayCommand RunCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand CopyCommand { get; }
    public RelayCommand SaveScriptCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand OpenDocsCommand { get; }
    public RelayCommand ToggleOptionsCommand { get; }

    // Ansichtseinstellung, keine Eigenschaft eines einzelnen Befehls: wer die Eingabefelder
    // wegklappt, um die Ergebnisliste zu sehen, will das auch nach dem Wechsel auf einen
    // anderen Befehl so vorfinden. Deshalb statisch fuer alle Befehlsseiten zusammen.
    private static bool _optionsVisible = true;

    /// <summary>
    /// Blendet die Eingabefelder aus. Die Befehlszeile bleibt sichtbar - was gesetzt ist,
    /// steht dort weiterhin vollstaendig, es geht also keine Information verloren.
    /// </summary>
    public bool OptionsVisible
    {
        get => _optionsVisible;
        set
        {
            if (_optionsVisible == value) return;
            _optionsVisible = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OptionsToggleText));
        }
    }

    public string OptionsToggleText =>
        Localizer.Instance[_optionsVisible ? "Options.Hide" : "Options.Show"];

    public string ExtraArguments
    {
        get => _extraArguments;
        set { if (Set(ref _extraArguments, value ?? "")) UpdatePreview(); }
    }

    public bool Elevated
    {
        get => _elevated;
        set { if (Set(ref _elevated, value)) OnPropertyChanged(nameof(ElevationNote)); }
    }

    public bool CanElevate => !ElevationService.IsProcessElevated;

    public string ElevationNote
    {
        get
        {
            if (ElevationService.IsProcessElevated) return Localizer.Instance["Elevation.AlreadyElevated"];
            var advice = ElevationService.Advise(Spec, CurrentValues());
            return advice.ReasonKey.Length == 0 ? "" : Localizer.Instance[advice.ReasonKey];
        }
    }

    public bool ShowElevationNote => ElevationNote.Length > 0;

    public RunState State
    {
        get => _state;
        private set
        {
            if (!Set(ref _state, value)) return;
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(TableHint));
            RunCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsRunning => State == RunState.Running;

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public IReadOnlyList<string> PreviewArgs { get; private set; } = [];
    public string PreviewLine { get; private set; } = "winget";

    public bool ShowTable
    {
        get => _showTable;
        private set => Set(ref _showTable, value);
    }

    private bool _outputView;

    /// <summary>Umschalter zwischen Ergebnisgitter und Rohausgabe.</summary>
    public bool OutputView
    {
        get => _outputView || !CanShowTable;
        set => Set(ref _outputView, value);
    }

    private RelayCommand? _showTableView;
    private RelayCommand? _showOutputView;

    public RelayCommand ShowTableViewCommand => _showTableView ??= new RelayCommand(() =>
    {
        OutputView = false;
        OnPropertyChanged(nameof(OutputView));
    });

    public RelayCommand ShowOutputViewCommand => _showOutputView ??= new RelayCommand(() =>
    {
        OutputView = true;
        OnPropertyChanged(nameof(OutputView));
    });

    public int RowCount
    {
        get => _rowCount;
        private set
        {
            if (!Set(ref _rowCount, value)) return;
            OnPropertyChanged(nameof(RowCountText));
            OnPropertyChanged(nameof(TableHint));
        }
    }

    public string RowCountText => Localizer.Instance.Format("Result.Rows", RowCount);

    /// <summary>
    /// Erklaert eine leere Liste, statt eine leere Flaeche stehen zu lassen: noch nichts
    /// ausgefuehrt, vom Filter weggenommen, oder eine Ausgabe, die keine Tabelle war.
    /// </summary>
    public string TableHint
    {
        get
        {
            if (RowCount > 0) return "";
            if (State == RunState.Idle) return Localizer.Instance["Result.Empty"];
            if (State == RunState.Running) return "";
            if (_tableFilter.Trim().Length > 0) return Localizer.Instance["Result.NoMatch"];
            return Localizer.Instance["Result.NoTable"];
        }
    }

    public string TableFilter
    {
        get => _tableFilter;
        set { if (Set(ref _tableFilter, value ?? "")) ApplyTableFilter(); }
    }

    public TableResult? Table
    {
        get => _table;
        private set
        {
            _table = value;
            TableChanged?.Invoke();
        }
    }

    /// <summary>Meldet der Ansicht, dass die Spalten des Gitters neu aufgebaut werden muessen.</summary>
    public event Action? TableChanged;

    public IReadOnlyDictionary<string, object?> CurrentValues()
    {
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var option in PrimaryOptions.Concat(AdvancedOptions).Concat(GlobalOptions))
        {
            var value = option.CurrentValue;
            if (value is not null) map[option.Spec.Id] = value;
        }
        return map;
    }

    /// <summary>Setzt eine Option von aussen, z. B. wenn aus der Ergebnisliste ein Paket uebernommen wird.</summary>
    public void Preset(string optionId, string value)
    {
        var option = AllOptions().FirstOrDefault(o => o.Spec.Id == optionId);
        if (option is null) return;

        if (option.IsFlag) option.BoolValue = value is "true" or "1";
        else option.SetText(value);
    }

    public void PresetFlag(string optionId, bool value)
    {
        var option = AllOptions().FirstOrDefault(o => o.Spec.Id == optionId);
        if (option is not null && option.IsFlag) option.BoolValue = value;
    }

    public IEnumerable<OptionVm> AllOptions() =>
        PrimaryOptions.Concat(AdvancedOptions).Concat(GlobalOptions);

    public void RefreshLanguage()
    {
        foreach (var option in AllOptions()) option.RefreshLanguage();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(AdvancedHeader));
        OnPropertyChanged(nameof(GlobalHeader));
        OnPropertyChanged(nameof(ElevationNote));
        OnPropertyChanged(nameof(RowCountText));
        OnPropertyChanged(nameof(OptionsToggleText));
        OnPropertyChanged(nameof(TableHint));
    }

    private IReadOnlyList<OptionVm> Build(IEnumerable<string> ids) =>
        ids.Where(id => _store.TryGetOption(id, out _))
           .Select(id => new OptionVm(_store.Option(id), UpdatePreview))
           .ToList();

    private void UpdatePreview()
    {
        var extra = SplitExtraArguments(_extraArguments);
        PreviewArgs = _builder.Build(Spec, CurrentValues(), extra);
        PreviewLine = CommandLineBuilder.ToDisplayLine(PreviewArgs);
        OnPropertyChanged(nameof(PreviewArgs));
        OnPropertyChanged(nameof(PreviewLine));
        OnPropertyChanged(nameof(ElevationNote));
        OnPropertyChanged(nameof(ShowElevationNote));

        var advice = ElevationService.Advise(Spec, CurrentValues());
        if (advice.Recommended && !ElevationService.IsProcessElevated) Elevated = true;
    }

    /// <summary>Zerlegt das Freitextfeld wie eine Kommandozeile, damit Pfade mit Leerzeichen heil bleiben.</summary>
    internal static List<string> SplitExtraArguments(string text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var c in text)
        {
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }

    public async Task RunAsync()
    {
        Output.Clear();
        Rows.Clear();
        Table = null;
        ShowTable = false;
        RowCount = 0;

        // Die Ansicht folgt dem Lauf: solange er laeuft, ist die mitlaufende Ausgabe das
        // einzige, was es zu sehen gibt - ein leeres Gitter waere nur eine leere Flaeche.
        // Entsteht am Ende eine Tabelle, schaltet BuildTable zurueck auf das Gitter.
        OutputView = true;
        OnPropertyChanged(nameof(OutputView));

        State = RunState.Running;
        StatusText = Localizer.Instance["Run.Running"];

        _cancellation = new CancellationTokenSource();
        var runs = BatchIds.Count > 0 ? BatchIds.ToList() : [string.Empty];
        var idOption = AllOptions().FirstOrDefault(o => o.Spec.Id == "id");
        var savedId = idOption?.TextValue ?? "";

        RunResult result = new(0, TimeSpan.Zero, false, "");
        var total = TimeSpan.Zero;

        try
        {
            foreach (var packageId in runs)
            {
                if (_cancellation.IsCancellationRequested) break;

                if (packageId.Length > 0 && idOption is not null)
                {
                    idOption.SetText(packageId);
                    AppendLine($"── {packageId} ──", LineKind.Info);
                }

                var args = _builder.Build(Spec, CurrentValues(), SplitExtraArguments(_extraArguments));
                result = await _runner.RunAsync(args, Elevated, AppendLine, _cancellation.Token);
                total += result.Duration;

                if (!result.Succeeded && runs.Count > 1)
                    AppendLine($"⚠ {packageId}: Exitcode {result.ExitCode}", LineKind.Error);

                if (result.Canceled) break;
            }
        }
        finally
        {
            if (savedId.Length > 0 && idOption is not null) idOption.SetText(savedId);
            FlushPending();
            _cancellation?.Dispose();
            _cancellation = null;
            BatchIds.Clear();
        }

        State = result.Canceled ? RunState.Canceled : result.Succeeded ? RunState.Succeeded : RunState.Failed;
        StatusText = string.Join(" · ", new[]
        {
            Localizer.Instance[State switch
            {
                RunState.Succeeded => "Status.Success",
                RunState.Canceled => "Status.Canceled",
                _ => "Status.Failed"
            }],
            Localizer.Instance.Format("Status.ExitCode", result.ExitCode),
            Localizer.Instance.Format("Status.Duration", total.TotalSeconds)
        });

        if (Spec.ParsedOutput == OutputKind.Table)
            BuildTable(string.Join(Environment.NewLine, Output.Select(o => o.Text)));
    }

    private void BuildTable(string text)
    {
        var parsed = TableParser.Parse(text);
        Table = parsed;
        Rows.Clear();

        if (parsed is null || parsed.Rows.Count == 0)
        {
            ShowTable = false;
            RowCount = 0;
            return;
        }

        foreach (var cells in parsed.Rows)
        {
            var id = parsed.IdColumn >= 0 && parsed.IdColumn < cells.Length ? cells[parsed.IdColumn] : "";
            Rows.Add(new TableRowVm(cells, id));
        }

        RowCount = Rows.Count;
        ShowTable = true;
        OutputView = false;
        OnPropertyChanged(nameof(OutputView));
        ApplyTableFilter();
    }

    private void ApplyTableFilter()
    {
        if (Table is null) return;

        var filter = _tableFilter.Trim();
        Rows.Clear();
        foreach (var cells in Table.Rows)
        {
            if (filter.Length > 0 &&
                !cells.Any(c => c.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                continue;

            var id = Table.IdColumn >= 0 && Table.IdColumn < cells.Length ? cells[Table.IdColumn] : "";
            Rows.Add(new TableRowVm(cells, id));
        }
        RowCount = Rows.Count;
        OnPropertyChanged(nameof(TableHint));
    }

    private void AppendLine(string text, LineKind kind)
    {
        lock (_pending)
        {
            _pending.Add(new OutputLine(text, kind));
            if (_flushScheduled) return;
            _flushScheduled = true;
        }

        Application.Current?.Dispatcher.InvokeAsync(FlushPending, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void FlushPending()
    {
        OutputLine[] batch;
        lock (_pending)
        {
            if (_pending.Count == 0) { _flushScheduled = false; return; }
            batch = _pending.ToArray();
            _pending.Clear();
            _flushScheduled = false;
        }

        foreach (var line in batch) Output.Add(line);

        // Sehr lange Ausgaben nicht unbegrenzt im Speicher halten.
        const int limit = 8000;
        while (Output.Count > limit) Output.RemoveAt(0);
    }

    private void CopyPreview()
    {
        try
        {
            Clipboard.SetText(PreviewLine);
            StatusText = Localizer.Instance["Preview.Copied"];
        }
        catch (Exception ex)
        {
            ErrorLog.Instance.Error(nameof(CommandVm), "Die Befehlszeile ließ sich nicht kopieren.", ex);
            StatusText = ex.Message;
        }
    }

    private void SaveScript()
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Title = Localizer.Instance["Preview.Save"],
                Filter = "PowerShell (*.ps1)|*.ps1",
                FileName = Spec.Id.Replace('.', '-') + ".ps1",
                AddExtension = true
            };
            if (dialog.ShowDialog() != true) return;

            File.WriteAllText(dialog.FileName,
                CommandLineBuilder.ToPowerShellScript(PreviewArgs, Title), new System.Text.UTF8Encoding(true));
            StatusText = dialog.FileName;
        }
        catch (Exception ex)
        {
            ErrorLog.Instance.Error(nameof(CommandVm), "Das Skript konnte nicht gespeichert werden.", ex);
            StatusText = ex.Message;
        }
    }

    private void Reset()
    {
        foreach (var option in AllOptions()) option.Clear();
        ExtraArguments = "";
        var noInteractivity = GlobalOptions.FirstOrDefault(o => o.Spec.Id == "disableInteractivity");
        if (noInteractivity is not null) noInteractivity.BoolValue = true;
        UpdatePreview();
    }

    private void OpenDocs()
    {
        var url = _store.DocsUrl(Spec);
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ErrorLog.Instance.Warn(nameof(CommandVm), $"Die Dokumentation ließ sich nicht öffnen: {url}", ex);
            StatusText = url;
        }
    }
}

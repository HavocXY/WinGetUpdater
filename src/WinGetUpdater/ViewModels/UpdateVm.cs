using System.Collections.ObjectModel;
using WinGetUpdater.Models;
using WinGetUpdater.Services;

namespace WinGetUpdater.ViewModels;

public enum UpdateStage { Start, Checking, Ready, UpToDate, Updating, Finished }

public enum ItemState { Waiting, Running, Succeeded, Failed, RollingBack, Restored }

/// <summary>Ein aktualisierbares Programm in der Hauptansicht.</summary>
public sealed class UpdateItem : ObservableObject
{
    private bool _isSelected = true;
    private ItemState _state = ItemState.Waiting;
    private string _note = "";

    public UpdateItem()
    {
        // RestoreHint ist lokalisiert - meldet sich wie alle anderen an, sonst bleibt
        // er nach einem Sprachwechsel im alten Stand stehen.
        RegisterLocalized(nameof(RestoreHint));
    }

    public required string Name { get; init; }
    public required string Id { get; init; }
    public required string CurrentVersion { get; init; }
    public required string NewVersion { get; init; }
    public required string Source { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (Set(ref _isSelected, value)) SelectionChanged?.Invoke(); }
    }

    public ItemState State
    {
        get => _state;
        set
        {
            if (!Set(ref _state, value)) return;
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsDone));
            OnPropertyChanged(nameof(IsFailed));
            OnPropertyChanged(nameof(IsRestored));
            OnPropertyChanged(nameof(CanRestore));
        }
    }

    public bool IsRunning => _state is ItemState.Running or ItemState.RollingBack;
    public bool IsDone => _state == ItemState.Succeeded;
    public bool IsFailed => _state == ItemState.Failed;
    public bool IsRestored => _state == ItemState.Restored;

    /// <summary>Ob die zuvor installierte Version bekannt ist - sonst lässt sich nichts zurücksetzen.</summary>
    public bool HasPreviousVersion =>
        !string.IsNullOrWhiteSpace(CurrentVersion) && CurrentVersion != "—";

    /// <summary>Ob der „Zurücksetzen“-Knopf an dieser Zeile erscheinen darf.</summary>
    public bool CanRestore => _state == ItemState.Failed && HasPreviousVersion;

    /// <summary>Knopf-Hinweis mit der Version, die neu installiert würde.</summary>
    public string RestoreHint => Localizer.Instance.Format("Update.RestoreHint", CurrentVersion);

    /// <summary>Kurzer Klartext neben der Zeile, etwa der Grund fuer ein Scheitern.</summary>
    public string Note
    {
        get => _note;
        set { if (Set(ref _note, value)) OnPropertyChanged(nameof(HasNote)); }
    }

    public bool HasNote => _note.Length > 0;

    public string VersionChange => $"{CurrentVersion}  →  {NewVersion}";

    internal Action? SelectionChanged;
}

/// <summary>
/// Die Hauptansicht: vorhandene Programme aktualisieren, sonst nichts.
///
/// Alles Zusaetzliche liegt hinter dem Optionen-Ausklapper oder im Bereich "Alle Befehle".
/// Die Befehlszeilen entstehen ueber denselben <see cref="CommandLineBuilder"/> wie dort -
/// die einfache Ansicht ist damit keine Abkuerzung mit eigenen Regeln, sondern eine
/// aufgeraeumte Bedienung derselben Maschinerie.
/// </summary>
public sealed class UpdateVm : ObservableObject
{
    private readonly SchemaStore _store;
    private readonly CommandLineBuilder _builder;
    private readonly IWingetRunner _runner;
    private readonly CommandSpec _upgrade;
    private readonly CommandSpec _install;

    private CancellationTokenSource? _cancellation;
    private UpdateStage _stage = UpdateStage.Start;
    private string _problem = "";
    private string _problemDetail = "";
    private DateTimeOffset? _lastCheck;
    private bool _showOutput;
    private bool _showOptions;

    private bool _silent = true;
    private bool _acceptAgreements = true;
    private bool _includeUnknown;
    private bool _includePinned;
    private bool _elevated;

    public UpdateVm(SchemaStore store, CommandLineBuilder builder, IWingetRunner runner)
    {
        _store = store;
        _builder = builder;
        _runner = runner;
        _upgrade = store.Find("upgrade")
                   ?? throw new InvalidOperationException("Der Befehl 'upgrade' fehlt im Schema.");
        _install = store.Find("install")
                   ?? throw new InvalidOperationException("Der Befehl 'install' fehlt im Schema.");

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        RunCommand = new AsyncRelayCommand(RunAsync, () => !IsBusy && SelectedCount > 0);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        RestoreCommand = new RelayCommand(
            p => _ = RestoreAsync(p as UpdateItem),
            p => p is UpdateItem i && i.CanRestore && !IsBusy);
        SelectAllCommand = new RelayCommand(() => SetAll(true));
        SelectNoneCommand = new RelayCommand(() => SetAll(false));
        ToggleOptionsCommand = new RelayCommand(() => ShowOptions = !ShowOptions);
        ToggleOutputCommand = new RelayCommand(() => ShowOutput = !ShowOutput);

        // Jede lokalisierte Eigenschaft meldet sich hier an - wer eine neue hinzufügt,
        // ergänzt sie in diese Liste und ist damit automatisch sprachwechsel-fähig.
        RegisterLocalized(
            nameof(Headline), nameof(SubLine), nameof(SelectionText),
            nameof(RunButtonText), nameof(OptionSummary), nameof(PreviewLine),
            nameof(OutputButtonText), nameof(RefreshButtonText));
    }

    public Localizer Loc => Localizer.Instance;
    public ObservableCollection<UpdateItem> Items { get; } = new();
    public ObservableCollection<OutputLine> Output { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand RunCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand RestoreCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }
    public RelayCommand ToggleOptionsCommand { get; }
    public RelayCommand ToggleOutputCommand { get; }

    // ---------------------------------------------------------------- Zustand

    public UpdateStage Stage
    {
        get => _stage;
        private set
        {
            if (!Set(ref _stage, value)) return;
            foreach (var name in new[]
                     {
                         nameof(IsBusy), nameof(IsChecking), nameof(IsUpdating), nameof(ShowList),
                         nameof(ShowUpToDate), nameof(ShowWelcome), nameof(Headline), nameof(SubLine),
                         nameof(RefreshButtonText)
                     })
                OnPropertyChanged(name);

            RefreshCommand.RaiseCanExecuteChanged();
            RunCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsBusy =>
        _stage is UpdateStage.Checking or UpdateStage.Updating
        || Items.Any(i => i.State == ItemState.RollingBack);
    public bool IsChecking => _stage == UpdateStage.Checking;
    public bool IsUpdating => _stage == UpdateStage.Updating;
    public bool ShowList => Items.Count > 0;
    public bool ShowUpToDate => _stage is UpdateStage.UpToDate;
    public bool ShowWelcome => _stage == UpdateStage.Start;

    /// <summary>
    /// Der Kopf-Knopf lädt vor der ersten Prüfung zu einer Suche ein und wiederholt sie
    /// danach. Ein fester Text würde im Startzustand behaupten, es gäbe etwas zu
    /// wiederholen - da ist aber noch nichts geprüft.
    /// </summary>
    public string RefreshButtonText =>
        Localizer.Instance[_stage == UpdateStage.Start ? "Update.Check" : "Update.CheckAgain"];

    public int SelectedCount => Items.Count(i => i.IsSelected);

    public string Headline => _stage switch
    {
        UpdateStage.Start => Localizer.Instance["Update.HeadlineStart"],
        UpdateStage.Checking => Localizer.Instance["Update.HeadlineChecking"],
        UpdateStage.UpToDate => Localizer.Instance["Update.HeadlineUpToDate"],
        UpdateStage.Updating => Localizer.Instance["Update.HeadlineUpdating"],
        UpdateStage.Finished => Localizer.Instance["Update.HeadlineFinished"],
        _ => Items.Count == 1
            ? Localizer.Instance["Update.HeadlineReadyOne"]
            : Localizer.Instance.Format("Update.HeadlineReadyMany", Items.Count)
    };

    public string SubLine
    {
        get
        {
            if (_stage == UpdateStage.Start) return Localizer.Instance["Update.SubStart"];
            if (_stage == UpdateStage.Checking) return Localizer.Instance["Update.SubChecking"];

            var checkedAt = _lastCheck is null
                ? ""
                : Localizer.Instance.Format("Update.LastCheck", _lastCheck.Value.ToString("HH:mm"));

            return _stage switch
            {
                UpdateStage.UpToDate => Localizer.Instance["Update.SubUpToDate"] + checkedAt,
                UpdateStage.Updating => Localizer.Instance["Update.SubUpdating"],
                UpdateStage.Finished => FinishedSummary(),
                _ => Localizer.Instance["Update.SubReady"] + checkedAt
            };
        }
    }

    private string FinishedSummary()
    {
        var ok = Items.Count(i => i.State == ItemState.Succeeded);
        var failed = Items.Count(i => i.State == ItemState.Failed);

        if (failed > 0) return Localizer.Instance.Format("Update.SubFinishedMixed", ok, failed);
        return ok == 1
            ? Localizer.Instance["Update.SubFinishedOkOne"]
            : Localizer.Instance.Format("Update.SubFinishedOkMany", ok);
    }

    /// <summary>"2 von 5 ausgewählt" - ersetzt die Spaltenkoepfe durch eine nuetzlichere Angabe.</summary>
    public string SelectionText => Localizer.Instance.Format("Update.SelectedOf", SelectedCount, Items.Count);

    public string RunButtonText => SelectedCount switch
    {
        0 => Localizer.Instance["Update.RunNone"],
        1 => Localizer.Instance["Update.RunOne"],
        _ => Localizer.Instance.Format("Update.RunMany", SelectedCount)
    };

    /// <summary>Klartextfehler, der oben im Bereich erscheint. Leer, wenn alles glatt lief.</summary>
    public string Problem
    {
        get => _problem;
        private set { if (Set(ref _problem, value)) OnPropertyChanged(nameof(HasProblem)); }
    }

    public string ProblemDetail
    {
        get => _problemDetail;
        private set => Set(ref _problemDetail, value);
    }

    public bool HasProblem => _problem.Length > 0;

    public bool ShowOutput
    {
        get => _showOutput;
        set { if (Set(ref _showOutput, value)) OnPropertyChanged(nameof(OutputButtonText)); }
    }

    public string OutputButtonText =>
        Localizer.Instance[_showOutput ? "Update.HideOutput" : "Update.ShowOutput"];

    public bool ShowOptions
    {
        get => _showOptions;
        set => Set(ref _showOptions, value);
    }

    // ---------------------------------------------------------------- Optionen

    public bool Silent
    {
        get => _silent;
        set { if (Set(ref _silent, value)) NotifyPreview(); }
    }

    public bool AcceptAgreements
    {
        get => _acceptAgreements;
        set { if (Set(ref _acceptAgreements, value)) NotifyPreview(); }
    }

    public bool IncludeUnknown
    {
        get => _includeUnknown;
        set { if (Set(ref _includeUnknown, value)) NotifyPreview(); }
    }

    public bool IncludePinned
    {
        get => _includePinned;
        set { if (Set(ref _includePinned, value)) NotifyPreview(); }
    }

    public bool Elevated
    {
        get => _elevated;
        set { if (Set(ref _elevated, value)) NotifyPreview(); }
    }

    public bool CanElevate => !ElevationService.IsProcessElevated;

    /// <summary>Die Kurzfassung der wirksamen Einstellungen, direkt neben dem Knopf.</summary>
    public string OptionSummary
    {
        get
        {
            var parts = new List<string>();
            parts.Add(Localizer.Instance[_silent ? "Update.ChipSilent" : "Update.ChipInteractive"]);
            if (_acceptAgreements) parts.Add(Localizer.Instance["Update.ChipAgreements"]);
            if (_includeUnknown) parts.Add(Localizer.Instance["Update.ChipUnknown"]);
            if (_includePinned) parts.Add(Localizer.Instance["Update.ChipPinned"]);
            if (_elevated) parts.Add(Localizer.Instance["Update.ChipAdmin"]);
            return string.Join(" · ", parts);
        }
    }

    /// <summary>Die Befehlszeile, die pro Programm ausgefuehrt wird - am Beispiel des ersten.</summary>
    public string PreviewLine
    {
        get
        {
            var example = Items.FirstOrDefault(i => i.IsSelected) ?? Items.FirstOrDefault();
            return CommandLineBuilder.ToDisplayLine(BuildUpgradeArgs(example?.Id ?? "<Paket-ID>"));
        }
    }

    private void NotifyPreview()
    {
        OnPropertyChanged(nameof(PreviewLine));
        OnPropertyChanged(nameof(OptionSummary));
    }

    // ---------------------------------------------------------------- Ablauf

    public async Task RefreshAsync()
    {
        Stage = UpdateStage.Checking;
        Problem = "";
        ProblemDetail = "";
        Output.Clear();
        ClearItems();

        _cancellation = new CancellationTokenSource();
        var args = _builder.Build(_upgrade, new Dictionary<string, object?>
        {
            ["includeUnknown"] = _includeUnknown ? true : null,
            ["includePinned"] = _includePinned ? true : null,
            ["disableInteractivity"] = true
        });

        RunResult result;
        try
        {
            result = await _runner.RunAsync(args, elevated: false, Append, _cancellation.Token);
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
        }

        _lastCheck = DateTimeOffset.Now;

        // result.Output ist die voellig gesammelte Ausgabe des Laufs - die ObservableCollection
        // "Output" wird asynchron ueber den Dispatcher gefuellt und kann hier noch unvollstaendig sein.
        var table = TableParser.Parse(result.Output);
        if (table is not null && table.IdColumn >= 0)
        {
            foreach (var row in Enumerable.Range(0, table.Rows.Count))
            {
                var item = new UpdateItem
                {
                    Name = table.Cell(row, table.NameColumn),
                    Id = table.Cell(row, table.IdColumn),
                    CurrentVersion = table.Cell(row, table.VersionColumn),
                    NewVersion = table.Cell(row, table.AvailableColumn),
                    Source = table.Cell(row, table.SourceColumn)
                };
                if (item.Id.Length == 0) continue;

                item.SelectionChanged = OnSelectionChanged;
                Items.Add(item);
            }
        }

        if (Items.Count == 0 && !result.Succeeded && !result.Canceled)
        {
            // Kein Ergebnis und ein Fehlercode: das muss der Benutzer sehen, nicht nur das Protokoll.
            Problem = Localizer.Instance.Format("Update.CheckFailed", result.ExitCode);
            ProblemDetail = LastLines(4);
            ShowOutput = true;
        }

        OnPropertyChanged(nameof(ShowList));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(RunButtonText));
        NotifyPreview();

        Stage = Items.Count > 0 ? UpdateStage.Ready : UpdateStage.UpToDate;
        RunCommand.RaiseCanExecuteChanged();
    }

    public async Task RunAsync()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;

        Stage = UpdateStage.Updating;
        Problem = "";
        ProblemDetail = "";
        Output.Clear();
        ShowOutput = true;

        foreach (var item in Items)
        {
            item.State = ItemState.Waiting;
            item.Note = "";
        }

        _cancellation = new CancellationTokenSource();
        var failures = new List<string>();

        try
        {
            foreach (var item in selected)
            {
                if (_cancellation.IsCancellationRequested) break;

                item.State = ItemState.Running;
                Append($"── {item.Name} ({item.Id}) ──", LineKind.Info);

                var result = await _runner.RunAsync(
                    BuildUpgradeArgs(item.Id), _elevated, Append, _cancellation.Token);

                if (result.Canceled)
                {
                    item.State = ItemState.Waiting;
                    item.Note = Localizer.Instance["Update.ItemCanceled"];
                    break;
                }

                if (result.Succeeded)
                {
                    item.State = ItemState.Succeeded;
                    item.Note = Localizer.Instance["Update.ItemDone"];
                }
                else
                {
                    item.State = ItemState.Failed;
                    item.Note = Localizer.Instance.Format("Update.ItemFailed", result.ExitCode);
                    failures.Add(item.Name);
                }
            }
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
        }

        if (failures.Count > 0)
        {
            Problem = Localizer.Instance.Format("Update.SomeFailed", failures.Count,
                                                string.Join(", ", failures));
            ProblemDetail = Localizer.Instance["Update.SeeLog"];
        }

        Stage = UpdateStage.Finished;
        OnPropertyChanged(nameof(SubLine));
    }

    private void Cancel()
    {
        _cancellation?.Cancel();
        ErrorLog.Instance.Info(nameof(UpdateVm), "Der Benutzer hat den laufenden Vorgang abgebrochen.");
    }

    /// <summary>
    /// Installiert fuer ein einzelnes fehlgeschlagenes Programm die zuvor installierte
    /// Version neu. Die Version kommt aus der Update-Liste und ist damit genau die,
    /// die vor dem fehlgeschlagenen Update installiert war.
    /// </summary>
    public async Task RestoreAsync(UpdateItem? item)
    {
        if (item is null || !item.CanRestore || IsBusy) return;

        item.State = ItemState.RollingBack;
        item.Note = Localizer.Instance["Update.Restoring"];
        Append($"── {item.Name}: Version {item.CurrentVersion} wird wiederhergestellt ──", LineKind.Info);
        RaiseBusyState();

        _cancellation = new CancellationTokenSource();
        try
        {
            var result = await _runner.RunAsync(
                BuildRestoreArgs(item), _elevated, Append, _cancellation.Token);

            if (result.Canceled)
            {
                item.State = ItemState.Failed;
                item.Note = Localizer.Instance["Update.ItemCanceled"];
            }
            else if (result.Succeeded)
            {
                item.State = ItemState.Restored;
                item.Note = Localizer.Instance.Format("Update.RestoredNote", item.CurrentVersion);
            }
            else
            {
                item.State = ItemState.Failed;
                item.Note = Localizer.Instance.Format("Update.RestoreFailed", result.ExitCode);
            }
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            RaiseBusyState();
        }
    }

    /// <summary>Argumente fuer die Neuinstallation der Vorversion - gleiche Optionen wie der Update-Lauf.</summary>
    private List<string> BuildRestoreArgs(UpdateItem item) =>
        _builder.Build(_install, new Dictionary<string, object?>
        {
            ["id"] = item.Id,
            ["exact"] = true,
            ["version"] = item.CurrentVersion,
            ["silent"] = _silent ? true : null,
            ["interactive"] = _silent ? null : true,
            ["acceptPackageAgreements"] = _acceptAgreements ? true : null,
            ["acceptSourceAgreements"] = _acceptAgreements ? true : null,
            ["disableInteractivity"] = true
        });

    /// <summary>
    /// Busy bedeutet auch „ein Zurücksetzen läuft“. Deshalb melden alle Befehle hier
    /// ihre Erreichbarkeit, wenn sich dieser Teil umdreht.
    /// </summary>
    private void RaiseBusyState()
    {
        OnPropertyChanged(nameof(IsBusy));
        RefreshCommand.RaiseCanExecuteChanged();
        RunCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        RestoreCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Argumente fuer die Aktualisierung eines einzelnen Programms.</summary>
    private List<string> BuildUpgradeArgs(string packageId) =>
        _builder.Build(_upgrade, new Dictionary<string, object?>
        {
            ["id"] = packageId,
            ["exact"] = true,
            ["silent"] = _silent ? true : null,
            ["interactive"] = _silent ? null : true,
            ["acceptPackageAgreements"] = _acceptAgreements ? true : null,
            ["acceptSourceAgreements"] = _acceptAgreements ? true : null,
            ["includeUnknown"] = _includeUnknown ? true : null,
            ["includePinned"] = _includePinned ? true : null,
            ["disableInteractivity"] = true
        });

    private void SetAll(bool selected)
    {
        foreach (var item in Items) item.IsSelected = selected;
        OnSelectionChanged();
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(RunButtonText));
        NotifyPreview();
        RunCommand.RaiseCanExecuteChanged();
    }

    private void ClearItems()
    {
        foreach (var item in Items) item.SelectionChanged = null;
        Items.Clear();
        OnPropertyChanged(nameof(ShowList));
    }

    private void Append(string text, LineKind kind)
    {
        var app = System.Windows.Application.Current;
        if (app is null || app.Dispatcher.CheckAccess()) AppendCore(text, kind);
        else app.Dispatcher.BeginInvoke(new Action(() => AppendCore(text, kind)));
    }

    private void AppendCore(string text, LineKind kind)
    {
        Output.Add(new OutputLine(text, kind));
        while (Output.Count > 4000) Output.RemoveAt(0);
    }

    private string LastLines(int count) =>
        string.Join(Environment.NewLine,
            Output.Select(o => o.Text).Where(t => t.Trim().Length > 0).TakeLast(count));
}

using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Win32;
using WinGetUpdater.Models;
using WinGetUpdater.Services;

namespace WinGetUpdater.ViewModels;

/// <summary>
/// Ein Bedienelement fuer genau eine winget-Option. Welche Art von Element daraus wird,
/// entscheidet allein <see cref="OptionSpec.Kind"/> aus dem Schema - deshalb muss fuer
/// eine neue winget-Option nichts an der Oberflaeche geaendert werden.
/// </summary>
public sealed class OptionVm : ObservableObject
{
    private readonly Action _changed;
    private bool _boolValue;
    private string _textValue = "";

    public OptionVm(OptionSpec spec, Action changed)
    {
        Spec = spec;
        _changed = changed;
        Items = new ObservableCollection<string>();
        Items.CollectionChanged += (_, _) => { _changed(); OnPropertyChanged(nameof(IsSet)); };

        BrowseCommand = new RelayCommand(Browse);
        ClearCommand = new RelayCommand(Clear, () => IsSet);
        AddItemCommand = new RelayCommand(() =>
        {
            if (string.IsNullOrWhiteSpace(TextValue)) return;
            Items.Add(TextValue.Trim());
            TextValue = "";
        });
        RemoveItemCommand = new RelayCommand(p => { if (p is string s) Items.Remove(s); });
    }

    public OptionSpec Spec { get; }
    public Localizer Loc => Localizer.Instance;

    public string Label => Spec.Label.Get(Localizer.Instance.Language);
    public string Description => Spec.Desc.Get(Localizer.Instance.Language);

    /// <summary>Die Schreibweise, wie sie in der Befehlszeile erscheint - inklusive Kurzform als Hinweis.</summary>
    public string CliHint => string.IsNullOrEmpty(Spec.Alias) ? Spec.Cli : $"{Spec.Cli}, {Spec.Alias}";

    public bool IsRisky => Spec.IsRisky;
    public string? Placeholder => Spec.Placeholder;
    public IReadOnlyList<string> EnumValues => Spec.Values ?? [];

    public OptionKind Kind => Spec.ParsedKind;
    public bool IsFlag => Kind == OptionKind.Flag;
    public bool IsEnum => Kind == OptionKind.Enum;
    public bool IsRepeatable => Spec.Repeatable;
    public bool IsPath => Kind is OptionKind.FilePath or OptionKind.FolderPath or OptionKind.SaveFilePath;
    public bool IsPlainText => !IsFlag && !IsEnum && !IsPath;

    public bool BoolValue
    {
        get => _boolValue;
        set
        {
            if (!Set(ref _boolValue, value)) return;
            OnPropertyChanged(nameof(IsSet));
            ClearCommand.RaiseCanExecuteChanged();
            _changed();
        }
    }

    public string TextValue
    {
        get => _textValue;
        set
        {
            if (!Set(ref _textValue, value ?? "")) return;
            OnPropertyChanged(nameof(IsSet));
            ClearCommand.RaiseCanExecuteChanged();
            _changed();
        }
    }

    /// <summary>Werte wiederholbarer Optionen, z. B. mehrfaches --sort.</summary>
    public ObservableCollection<string> Items { get; }

    public bool IsSet => IsFlag
        ? _boolValue
        : IsRepeatable ? Items.Count > 0 : !string.IsNullOrWhiteSpace(_textValue);

    public RelayCommand BrowseCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand AddItemCommand { get; }
    public RelayCommand RemoveItemCommand { get; }

    /// <summary>Der Wert, wie ihn der <see cref="CommandLineBuilder"/> erwartet.</summary>
    public object? CurrentValue
    {
        get
        {
            if (IsFlag) return _boolValue ? true : null;
            if (IsRepeatable) return Items.Count > 0 ? Items.ToList() : null;
            return string.IsNullOrWhiteSpace(_textValue) ? null : _textValue;
        }
    }

    public void Clear()
    {
        BoolValue = false;
        TextValue = "";
        if (Items.Count > 0) Items.Clear();
    }

    public void SetText(string value) => TextValue = value;

    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Description));
    }

    private void Browse()
    {
        try
        {
            ShowBrowseDialog();
        }
        catch (Exception ex)
        {
            // Ein ungueltiger Startpfad reicht, damit der Dialog wirft.
            ErrorLog.Instance.Warn(nameof(OptionVm),
                $"Der Auswahldialog für \"{Label}\" ließ sich nicht öffnen.", ex);
        }
    }

    private void ShowBrowseDialog()
    {
        switch (Kind)
        {
            case OptionKind.FolderPath:
            {
                var dialog = new OpenFolderDialog { Title = Label };
                if (Directory.Exists(_textValue)) dialog.InitialDirectory = _textValue;
                if (dialog.ShowDialog() == true) TextValue = dialog.FolderName;
                break;
            }
            case OptionKind.SaveFilePath:
            {
                var dialog = new SaveFileDialog { Title = Label, Filter = FileFilter(), AddExtension = true };
                if (!string.IsNullOrWhiteSpace(_textValue)) dialog.FileName = _textValue;
                if (dialog.ShowDialog() == true) TextValue = dialog.FileName;
                break;
            }
            default:
            {
                var dialog = new OpenFileDialog { Title = Label, Filter = FileFilter(), CheckFileExists = true };
                if (File.Exists(_textValue)) dialog.FileName = _textValue;
                if (dialog.ShowDialog() == true) TextValue = dialog.FileName;
                break;
            }
        }
    }

    private string FileFilter() => Spec.Filter switch
    {
        "yaml" => "Manifest (*.yaml;*.yml)|*.yaml;*.yml|Alle Dateien (*.*)|*.*",
        "json" => "JSON (*.json)|*.json|Alle Dateien (*.*)|*.*",
        "log" => "Protokoll (*.log;*.txt)|*.log;*.txt|Alle Dateien (*.*)|*.*",
        "config" => "Konfiguration (*.winget;*.yaml;*.yml)|*.winget;*.yaml;*.yml|Alle Dateien (*.*)|*.*",
        _ => "Alle Dateien (*.*)|*.*"
    };
}

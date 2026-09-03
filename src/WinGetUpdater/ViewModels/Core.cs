using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using WinGetUpdater.Services;

namespace WinGetUpdater.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private readonly HashSet<string> _localizedPropertyNames = new();
    private bool _subscribedToLanguage;

    /// <summary>
    /// Macht die genannten Eigenschaften sprachwechsel-fähig: sie melden sich selbst neu,
    /// sobald die Oberflächensprache wechselt. Eine lokalisierte Eigenschaft muss damit nur
    /// einmal hier genannt werden - wer sie vergisst, verfestigt still die alte Sprache
    /// statt die neue anzuzeigen. Mehrere Aufrufe werden zusammengefasst; abonniert wird
    /// nur einmal.
    /// </summary>
    protected void RegisterLocalized(params string[] names)
    {
        foreach (var name in names) _localizedPropertyNames.Add(name);
        if (!_subscribedToLanguage)
        {
            _subscribedToLanguage = true;
            Localizer.Instance.LanguageChanged += (_, _) =>
            {
                foreach (var name in _localizedPropertyNames) OnPropertyChanged(name);
            };
        }
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute()) { }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private bool _running;

    public AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute()) { }

    public bool CanExecute(object? parameter) => !_running && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        _running = true;
        RaiseCanExecuteChanged();
        try { await _execute(parameter); }
        finally
        {
            _running = false;
            RaiseCanExecuteChanged();
        }
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

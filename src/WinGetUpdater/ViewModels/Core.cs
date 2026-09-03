using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using WinGetUpdater.Services;

namespace WinGetUpdater.ViewModels;

/// <summary>
/// Markiert eine Eigenschaft als sprachabhängig. <see cref="ObservableObject.RegisterLocalized"/>
/// findet alle so markierten Eigenschaften einer Klasse per Reflection und meldet sie bei
/// jedem Sprachwechsel automatisch neu. Das Attribut sitzt direkt an der Eigenschaft, die es
/// betrifft - anders als eine Namensliste an einer entfernten Aufrufstelle lässt es sich beim
/// Anlegen einer neuen lokalisierten Eigenschaft kaum übersehen.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class LocalizedAttribute : Attribute { }

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

    // Pro Typ einmal per Reflection ermittelt und wiederverwendet - eine Handvoll ViewModel-
    // Typen über die gesamte Laufzeit, kein Aufwand, der bei jeder Instanz erneut anfiele.
    private static readonly ConcurrentDictionary<Type, string[]> LocalizedPropertiesByType = new();
    private bool _subscribedToLanguage;

    /// <summary>
    /// Meldet diese Instanz für die Sprachumschaltung an: jede mit <see cref="LocalizedAttribute"/>
    /// markierte Eigenschaft dieses Typs feuert bei jedem Sprachwechsel automatisch ihr eigenes
    /// PropertyChanged. Keine Namensliste hier im Konstruktor - wer eine neue lokalisierte
    /// Eigenschaft hinzufügt, markiert nur sie selbst. Mehrere Aufrufe abonnieren nur einmal.
    /// </summary>
    protected void RegisterLocalized()
    {
        if (_subscribedToLanguage) return;
        _subscribedToLanguage = true;

        var names = LocalizedPropertiesByType.GetOrAdd(GetType(), FindLocalizedProperties);
        Localizer.Instance.LanguageChanged += (_, _) =>
        {
            foreach (var name in names) OnPropertyChanged(name);
        };
    }

    private static string[] FindLocalizedProperties(Type type) =>
        type.GetProperties()
            .Where(p => Attribute.IsDefined(p, typeof(LocalizedAttribute)))
            .Select(p => p.Name)
            .ToArray();
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

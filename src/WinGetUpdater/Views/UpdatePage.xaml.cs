using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using WinGetUpdater.Services;
using WinGetUpdater.ViewModels;

namespace WinGetUpdater.Views;

public partial class UpdatePage : UserControl
{
    private UpdateVm? _vm;
    private bool _checkedOnce;

    public UpdatePage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Resources["Spin"] is Storyboard spinner) spinner.Begin(this, true);
        Attach();

        // Beim ersten Anzeigen sofort nachsehen - wer die Anwendung oeffnet, will wissen,
        // ob etwas ansteht, und nicht erst einen Knopf suchen.
        if (_checkedOnce || _vm is null) return;
        _checkedOnce = true;

        Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                await _vm.RefreshAsync();
            }
            catch (Exception ex)
            {
                ErrorLog.Instance.Error(nameof(UpdatePage), "Die erste Update-Prüfung ist fehlgeschlagen.", ex);
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void Attach()
    {
        if (DataContext is not UpdateVm vm || ReferenceEquals(vm, _vm)) return;

        if (_vm is not null)
            ((INotifyCollectionChanged)_vm.Output).CollectionChanged -= OnOutputChanged;

        _vm = vm;
        ((INotifyCollectionChanged)_vm.Output).CollectionChanged += OnOutputChanged;
    }

    /// <summary>Der Verlauf soll mitlaufen, ohne dass jemand scrollen muss.</summary>
    private void OnOutputChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
            OutputScroll.ScrollToEnd();
    }

    /// <summary>
    /// Ein Klick irgendwo in der Zeile - Name, Version, Quelle oder Leerraum - schaltet die
    /// Auswahl um. Checkbox und Zurückrollen-Knopf sind ButtonBase und haben das Event
    /// bereits als behandelt markiert, also erreicht hier nur, was wirklich auf die Zeile
    /// selbst geklickt wurde. Kein Doppelumschalten, keine Nebenwirkung auf die Knöpfe.
    /// </summary>
    private void OnRowToggled(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled) return;
        if (sender is FrameworkElement fe && fe.DataContext is UpdateItem item)
            item.IsSelected = !item.IsSelected;
    }

    /// <summary>
    /// Leertaste oder Eingabetaste auf der fokussierten Zeile schalten dieselbe Auswahl um
    /// wie ein Mausklick - Checkbox und Zurückrollen-Knopf behandeln ihre eigenen Tasten
    /// selbst und markieren das Ereignis dabei als behandelt, erreichen diesen Handler also
    /// nicht.
    /// </summary>
    private void OnRowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key is not (Key.Space or Key.Enter)) return;
        if (sender is FrameworkElement fe && fe.DataContext is UpdateItem item)
        {
            item.IsSelected = !item.IsSelected;
            e.Handled = true;
        }
    }
}

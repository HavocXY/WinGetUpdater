using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
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
}

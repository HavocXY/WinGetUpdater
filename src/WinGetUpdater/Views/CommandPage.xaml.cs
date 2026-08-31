using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WinGetUpdater.ViewModels;

namespace WinGetUpdater.Views;

public partial class CommandPage : UserControl
{
    private CommandVm? _vm;

    public CommandPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>Wird gesetzt, damit die Paketaktionen an die Schale weitergereicht werden koennen.</summary>
    public ShellVm? Shell { get; set; }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null) _vm.TableChanged -= RebuildColumns;

        _vm = DataContext as CommandVm;
        if (_vm is not null) _vm.TableChanged += RebuildColumns;

        RebuildColumns();
    }

    /// <summary>
    /// Die Spalten stehen erst nach dem Lauf fest - sie kommen aus der Kopfzeile der
    /// winget-Ausgabe und heissen deshalb je nach Windows-Sprache anders.
    /// </summary>
    private void RebuildColumns()
    {
        ResultGrid.Columns.Clear();
        var table = _vm?.Table;
        if (table is null) return;

        for (var i = 0; i < table.Columns.Count; i++)
        {
            // Name und Paket-ID sind die beiden langen Spalten; sie bekommen doppeltes Gewicht,
            // damit Bezeichner wie "Microsoft.VisualStudioCode" nicht abgeschnitten werden.
            var wide = i == 0 || i == table.IdColumn;
            ResultGrid.Columns.Add(new DataGridTextColumn
            {
                Header = table.Columns[i],
                Binding = new Binding($"[{i}]"),
                MinWidth = wide ? 160 : 70,
                Width = new DataGridLength(wide ? 2 : 1, DataGridLengthUnitType.Star)
            });
        }
    }

    private void OnGridSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Shell is null) return;

        Shell.SelectedPackageIds.Clear();
        foreach (var item in ResultGrid.SelectedItems)
            if (item is TableRowVm row && !string.IsNullOrWhiteSpace(row.PackageId))
                Shell.SelectedPackageIds.Add(row.PackageId);
    }

    private void OnPackageAction(object sender, RoutedEventArgs e)
    {
        if (Shell is null || sender is not Button { Tag: string target }) return;
        Shell.PackageActionCommand.Execute(target);
    }
}

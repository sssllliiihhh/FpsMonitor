using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FpsMonitor.Services;

namespace FpsMonitor.Views;

public partial class GameSelectionWindow : Window
{
    private List<GameProcessInfo> _allProcesses = [];

    public GameProcessInfo? SelectedGame { get; private set; }

    public GameSelectionWindow()
    {
        InitializeComponent();
        LoadProcesses();
    }

    private void LoadProcesses()
    {
        var includeAll = ShowAllCheckBox.IsChecked == true;
        _allProcesses = GameProcessService.GetProcesses(includeAll);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? _allProcesses
            : _allProcesses.Where(p =>
                p.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.ExeName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        ProcessList.ItemsSource = filtered;
        if (filtered.Count > 0)
            ProcessList.SelectedIndex = 0;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ShowAllCheckBox_Changed(object sender, RoutedEventArgs e) => LoadProcesses();

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => LoadProcesses();

    private void ProcessList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => AcceptSelection();

    private void SelectButton_Click(object sender, RoutedEventArgs e) => AcceptSelection();

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void AcceptSelection()
    {
        if (ProcessList.SelectedItem is not GameProcessInfo game)
        {
            MessageBox.Show(this, "Сначала выберите процесс из списка.", "Выбор игры",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedGame = game;
        DialogResult = true;
        Close();
    }
}

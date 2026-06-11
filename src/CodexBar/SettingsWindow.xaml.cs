using System.Windows;
using System.Windows.Controls;
using CodexBar.Models;
using CodexBar.ViewModels;

namespace CodexBar;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel _viewModel;

    public SettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        LoadFromSettings(viewModel.Settings);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _viewModel.Settings.CodexPath = string.IsNullOrWhiteSpace(CodexPathBox.Text) ? null : CodexPathBox.Text.Trim();
        _viewModel.Settings.StartWithWindows = StartupBox.IsChecked == true;
        _viewModel.Settings.TopMost = TopMostBox.IsChecked == true;
        _viewModel.Settings.LockPosition = LockPositionBox.IsChecked == true;
        _viewModel.Settings.AutoDockToTaskbar = AutoDockBox.IsChecked == true;
        _viewModel.Settings.RefreshIntervalSeconds = int.TryParse(RefreshBox.Text, out var seconds) ? seconds : 15;
        _viewModel.Settings.Language = ((ComboBoxItem?)LanguageBox.SelectedItem)?.Content?.ToString() ?? "en";
        _viewModel.SaveSettings();
        Close();
    }

    private void OnDefaults(object sender, RoutedEventArgs e)
    {
        LoadFromSettings(new AppSettings());
    }

    private void LoadFromSettings(AppSettings settings)
    {
        CodexPathBox.Text = settings.CodexPath ?? "";
        StartupBox.IsChecked = settings.StartWithWindows;
        TopMostBox.IsChecked = settings.TopMost;
        LockPositionBox.IsChecked = settings.LockPosition;
        AutoDockBox.IsChecked = settings.AutoDockToTaskbar;
        RefreshBox.Text = settings.RefreshIntervalSeconds.ToString();
        LanguageBox.SelectedIndex = settings.Language.Equals("zh", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }
}

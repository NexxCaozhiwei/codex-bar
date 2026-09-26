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
        DataContext = viewModel;
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
        _viewModel.Settings.OpacityPercent = Math.Clamp((int)Math.Round(OpacitySlider.Value), 20, 100);
        _viewModel.Settings.Language = ((ComboBoxItem?)LanguageBox.SelectedItem)?.Tag?.ToString() ?? "zh";
        _viewModel.Settings.NotifyOnTaskCompleted = NotifyCompletedBox.IsChecked == true;
        _viewModel.Settings.NotifyOnWaitingUser = NotifyWaitingUserBox.IsChecked == true;
        _viewModel.Settings.NotifyOnWaitingApproval = NotifyWaitingApprovalBox.IsChecked == true;
        _viewModel.Settings.NotifyOnError = NotifyErrorBox.IsChecked == true;
        _viewModel.Settings.NotificationMinimumTaskDurationSeconds =
            int.TryParse(MinimumDurationBox.Text, out var minimumDuration) ? minimumDuration : 30;
        _viewModel.Settings.UpdateMode = Enum.TryParse<UpdateMode>(
            ((ComboBoxItem?)UpdateModeBox.SelectedItem)?.Tag?.ToString(),
            out var updateMode) ? updateMode : UpdateMode.Notify;
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
        OpacitySlider.Value = Math.Clamp(settings.OpacityPercent, 20, 100);
        UpdateOpacityText();
        LanguageBox.SelectedIndex = settings.Language.Equals("en", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        NotifyCompletedBox.IsChecked = settings.NotifyOnTaskCompleted;
        NotifyWaitingUserBox.IsChecked = settings.NotifyOnWaitingUser;
        NotifyWaitingApprovalBox.IsChecked = settings.NotifyOnWaitingApproval;
        NotifyErrorBox.IsChecked = settings.NotifyOnError;
        MinimumDurationBox.Text = settings.NotificationMinimumTaskDurationSeconds.ToString();
        UpdateModeBox.SelectedItem = UpdateModeBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), settings.UpdateMode.ToString(), StringComparison.Ordinal));
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => UpdateOpacityText();

    private void UpdateOpacityText()
    {
        if (OpacityValueText is not null)
        {
            OpacityValueText.Text = $"{Math.Clamp((int)Math.Round(OpacitySlider.Value), 20, 100)}%";
        }
    }
}

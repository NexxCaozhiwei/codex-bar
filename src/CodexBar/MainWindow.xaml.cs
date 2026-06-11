using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CodexBar.ViewModels;

namespace CodexBar;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.AttachWindow(this);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_viewModel.Settings.LockPosition && e.ClickCount == 1)
        {
            DragMove();
        }
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _viewModel.SaveWindowPosition();
        if (e.ClickCount == 1)
        {
            _viewModel.ShowDetails();
        }
    }

    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();
        menu.Items.Add(MenuItem("Show Details", (_, _) => _viewModel.ShowDetails()));
        menu.Items.Add(MenuItem("Refresh", async (_, _) => await _viewModel.RefreshAsync()));
        menu.Items.Add(MenuItem("Settings", (_, _) => _viewModel.ShowSettings()));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Dock", (_, _) => _viewModel.DockNow()));
        menu.Items.Add(MenuItem("Exit", (_, _) => Application.Current.Shutdown()));
        menu.IsOpen = true;
    }

    private static MenuItem MenuItem(string header, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = header };
        item.Click += handler;
        return item;
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CodexBar.ViewModels;

namespace CodexBar;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _suppressNextClick;

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
            var startLeft = Left;
            var startTop = Top;
            DragMove();
            _viewModel.SaveWindowPosition();
            _suppressNextClick =
                Math.Abs(Left - startLeft) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(Top - startTop) > SystemParameters.MinimumVerticalDragDistance;
        }
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _viewModel.SaveWindowPosition();
        if (_suppressNextClick)
        {
            _suppressNextClick = false;
            return;
        }

        if (e.ClickCount >= 2)
        {
            _viewModel.ShowDetails();
            return;
        }

        if (e.ClickCount == 1)
        {
            _viewModel.ToggleQuotaDisplayMode();
        }
    }

    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();
        menu.Items.Add(MenuItem("查看详情", (_, _) => _viewModel.ShowDetails()));
        menu.Items.Add(MenuItem("刷新", async (_, _) => await _viewModel.RefreshAsync()));
        menu.Items.Add(MenuItem("设置", (_, _) => _viewModel.ShowSettings()));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("吸附到任务栏附近", (_, _) => _viewModel.DockNow()));
        menu.Items.Add(MenuItem("退出", (_, _) => System.Windows.Application.Current.Shutdown()));
        menu.IsOpen = true;
    }

    private static MenuItem MenuItem(string header, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = header };
        item.Click += handler;
        return item;
    }
}

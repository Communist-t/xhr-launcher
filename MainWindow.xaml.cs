using System.Windows;
using Launcher.Models;
using Launcher.ViewModels;
using Launcher.Views;

namespace Launcher;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(UserSession session)
    {
        InitializeComponent();
        _vm = new MainViewModel(session);
        DataContext = _vm;

        // 首屏滚动到员工列表顶部，避免初始化时被可聚焦元素带到下方卡片。
        Loaded += (_, _) => AppsScroll.ScrollToTop();
    }

    private void PayMethod_Checked(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        if (sender is System.Windows.Controls.RadioButton rb && rb.Tag is string method && !string.IsNullOrWhiteSpace(method))
        {
            _vm.PaymentMethod = method;
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        _vm.Shutdown();
    }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        _vm.Logout();
        var login = new LoginWindow();
        login.Show();
        Close();
    }

}

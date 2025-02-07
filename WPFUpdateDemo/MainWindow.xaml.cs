using System.Reflection;
using System.Windows;

namespace WPFUpdateDemo;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow
{
    private readonly AppUpdateTool appUpdateTool;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        appUpdateTool = new()
        {
            mainWindow = this
        };
        versionText.Text = $"当前版本 v{Assembly.GetExecutingAssembly().GetName().Version}";

        // 启动时自动检查新版本
        CheckNewVersion();
    }

    private void UpdateButtonClick(object sender, RoutedEventArgs e)
    {
        appUpdateTool.UpdateApp();
    }

    private async void CheckNewVersion()
    {
        await Task.Run(() =>
        {
            if (AppUpdateTool.CheckNewVersion(out string? latestVersionString, out string? downloadUrl))
            {
                if (string.IsNullOrEmpty(latestVersionString))
                    outputText.Text = "检测到新版本 v" + latestVersionString;
            }
        });
    }
}
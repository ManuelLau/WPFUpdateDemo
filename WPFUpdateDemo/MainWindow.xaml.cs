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
    }

    private void TestButton0Click(object sender, RoutedEventArgs e)
    {
        
        appUpdateTool.UpdateApp();
    }

    private void TestButton1Click(object sender, RoutedEventArgs e)
    {
        
    }
}
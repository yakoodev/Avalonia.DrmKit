
using Avalonia.Controls;

namespace Avalonia.DrmKit.Sample.Views;
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnOpenSecondWindow(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var w = new SecondWindow();
        w.Show();
    }
}

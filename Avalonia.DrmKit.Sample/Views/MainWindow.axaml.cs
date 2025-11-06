
using Avalonia.Controls;
using Avalonia.VisualTree;
using System;
using System.Linq;

namespace Avalonia.DrmKit.Sample.Views;
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        var tb = this.FindControl<TextBox>("FirstTb") ?? this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
        tb?.Focus();
    }

    private void OnOpenSecondWindow(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var w = new SecondWindow();
        w.Show();
    }
}

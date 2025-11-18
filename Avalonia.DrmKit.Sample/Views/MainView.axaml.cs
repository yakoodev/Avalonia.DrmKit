
using Avalonia.Controls;

namespace Avalonia.DrmKit.Sample.Views;
public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    private void Button_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button button) {
            Console.WriteLine(button.IsPointerOver);
            Console.WriteLine(string.Join('|', button.Classes.Select(t => t.ToString())));
            //btn2.Classes.Add(":pointerover");
        }
    }
}

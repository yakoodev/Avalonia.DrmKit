using Avalonia;
using Avalonia.DrmKit;
using Avalonia.DrmKit.Input;
using Avalonia.ReactiveUI;

namespace Avalonia.DrmKit.Sample;

internal static class Program
{
    public static int Main(string[] args)
    {
        var options = new SmartPlatformOptions
        {
            // Пример: явно выбрать backend ввода evdev, карту и режим вывода.
            InputBackend = new EvdevInputBackend(),
            DrmCard = "/dev/dri/card1",
            Resolution = new PixelSize(1920, 1080),
            Scaling = 1.0,
            ConnectorsForceProbe = true,
        };

        return SmartPlatform.Start(BuildAvaloniaApp(), args, options);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI()
            .UseDrmSoftCursor();
}

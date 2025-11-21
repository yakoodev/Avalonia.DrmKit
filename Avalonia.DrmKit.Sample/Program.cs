
using Avalonia;
using Avalonia.DrmKit;
using Avalonia.ReactiveUI;

namespace Avalonia.DrmKit.Sample;
internal static class Program
{
    public static int Main(string[] args)
    {
        var options = new SmartPlatformOptions
        {
            // Свой IInputBackend (например, твой, реализующий разграничение mouse/touch)
            // InputBackend = new MyInputBackend(),

            // Или фабрика (если надо из DI достать)
            // InputBackendFactory = () => new MyInputBackend(),

            // Пример явного выбора карты и режима:
            DrmCard = "/dev/dri/card1",
            Resolution = new PixelSize(1920, 1080),
            Scaling = 1.0,
            ConnectorsForceProbe = true,
            //InputBackend = 

        };
        return SmartPlatform.Start(BuildAvaloniaApp(), args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI()
            .UseDrmSoftCursor()
            .UseLinuxKeyboardListener()
            .UseEvdevMouseFallback();
}

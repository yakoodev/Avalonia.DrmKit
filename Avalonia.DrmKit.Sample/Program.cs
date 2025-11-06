
using Avalonia;
using Avalonia.DrmKit;
using Avalonia.ReactiveUI;

namespace Avalonia.DrmKit.Sample;
internal static class Program
{
    public static int Main(string[] args)
        => SmartPlatform.Start(BuildAvaloniaApp(), args);

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


using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Themes.Fluent;
using System;

namespace CTGP7InstallerSharp
{
    public class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = new MainWindow();
            base.OnFrameworkInitializationCompleted();
        }

        public static int Main(string[] args)
            => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        public static AppBuilder BuildAvaloniaApp()
          => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}
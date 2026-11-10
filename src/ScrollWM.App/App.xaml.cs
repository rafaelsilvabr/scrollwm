using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using ScrollWM.Windows;

namespace ScrollWM.App;

public partial class App : Application
{
    private SessionLock? sessionLock;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args is ["--fixture"] || e.Args is ["--fixture", _])
        {
            var fixtureCount = 3;
            if (e.Args.Length == 2 && !int.TryParse(e.Args[1], out fixtureCount)) { Shutdown(1); return; }
            if (fixtureCount is < 1 or > 10) { Shutdown(1); return; }
            ShutdownMode = ShutdownMode.OnLastWindowClose;
            for (var index = 0; index < fixtureCount; index++)
            {
                var fixture = new Window
                {
                    Title = $"ScrollWM test window {index + 1}",
                    Width = 440 + index * 40,
                    Height = 300,
                    MinWidth = 240 + index * 40,
                    MinHeight = 180,
                    Left = 60 + index * 80,
                    Top = 80 + index * 60,
                    Background = Brushes.WhiteSmoke,
                };
                var content = new StackPanel { Margin = new Thickness(28) };
                content.Children.Add(new TextBlock
                {
                    Text = $"Test window {index + 1}\n\nDisposable window for positioning and recovery experiments.",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 20,
                });
                var moveButton = new Button
                {
                    Content = "Move this test window",
                    Margin = new Thickness(0, 20, 0, 0),
                    Padding = new Thickness(12, 8, 12, 8),
                };
                moveButton.Click += (_, _) =>
                {
                    fixture.Left += 160;
                    fixture.Top += 80;
                };
                content.Children.Add(moveButton);
                fixture.Content = content;
                fixture.SourceInitialized += (_, _) => NativeWindowSystem.MarkTestFixture(new WindowInteropHelper(fixture).Handle);
                fixture.Show();
            }

            return;
        }

        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScrollWM");
            if (e.Args.Length == 2 && e.Args[0] == "--data-dir")
            {
                directory = Path.GetFullPath(e.Args[1]);
            }
            else if (e.Args.Length != 0)
            {
                throw new ArgumentException("Usage: ScrollWM.App [--data-dir <directory>] or --fixture");
            }

            sessionLock = new SessionLock();
            MainWindow = new MainWindow(directory);
            MainWindow.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "ScrollWM", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        sessionLock?.Dispose();
        base.OnExit(e);
    }
}

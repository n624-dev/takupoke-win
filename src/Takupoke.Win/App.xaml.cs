using Microsoft.UI.Xaml;

namespace Takupoke.Win;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        if (Environment.GetEnvironmentVariable("TAKUPOKE_OFFLINE_TEST_MODE") == "1"
            && Environment.GetEnvironmentVariable("TAKUPOKE_DATA_ROOT") is { Length: > 0 } testRoot)
            UnhandledException += (_, args) =>
            {
                // CI uses only synthetic offline data. Production never writes exception details.
                Directory.CreateDirectory(testRoot);
                File.AppendAllText(Path.Combine(testRoot, "ui-error.txt"), args.Exception.ToString());
            };
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}

using Microsoft.UI.Xaml;

namespace Past.App;

public partial class App : Application
{
    private AppHost? _host;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Tray-only app: no primary window is shown at launch. The overlay appears
        // on the global hotkey; the tray icon owns the lifecycle.
        _host = new AppHost();
        _host.Start();
    }
}

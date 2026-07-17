using System.Threading;
using Microsoft.UI.Xaml;

namespace Past.App;

public partial class App : Application
{
    // Held for the process lifetime to enforce a single instance.
    private static Mutex? _instanceMutex;

    private AppHost? _host;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // One clipboard listener + one global hotkey per machine. A second instance would
        // fight over the hotkey and double-capture, which is exactly the "zombie process"
        // behaviour seen when an upgrade left an old copy running. Bail out if we are not
        // the first.
        _instanceMutex = new Mutex(initiallyOwned: true, @"Local\Past.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Diag.Log("another instance is already running; exiting");
            Exit();
            return;
        }

        // Tray-only app: no primary window is shown at launch. The overlay appears
        // on the global hotkey; the tray icon owns the lifecycle.
        _host = new AppHost();
        _host.Start();
    }
}

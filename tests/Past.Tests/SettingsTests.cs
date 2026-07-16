using Past.Infrastructure.Storage;
using Past.Services;

namespace Past.Tests;

public class SettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"past_settings_{Guid.NewGuid():N}.json");

    [Fact]
    public void Defaults_to_pasting_immediately_on_select()
    {
        var store = new JsonSettingsStore(_path);

        // No file yet -> defaults, and the default must be "paste immediately".
        Assert.True(store.Load().PasteOnSelect);
    }

    [Fact]
    public void Turning_paste_on_select_off_survives_a_restart()
    {
        new JsonSettingsStore(_path).Save(new AppSettings { PasteOnSelect = false });

        // Fresh store = new app run reading the same file.
        Assert.False(new JsonSettingsStore(_path).Load().PasteOnSelect);
    }

    [Fact]
    public void Corrupt_settings_fall_back_to_defaults()
    {
        File.WriteAllText(_path, "{ not valid json");

        Assert.True(new JsonSettingsStore(_path).Load().PasteOnSelect);
    }

    public void Dispose()
    {
        try { File.Delete(_path); } catch { /* best effort */ }
    }
}

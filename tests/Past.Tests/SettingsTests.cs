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

    [Fact]
    public void No_custom_hotkey_by_default()
    {
        // Fresh install has no saved hotkey, so the app uses its default candidate list.
        Assert.False(new JsonSettingsStore(_path).Load().HasCustomHotkey);
    }

    [Fact]
    public void Chosen_hotkey_persists_across_restart()
    {
        // Win(8) + Shift(4) + V(0x56)
        new JsonSettingsStore(_path).Save(new AppSettings { HotkeyModifiers = 8 | 4, HotkeyVk = 0x56 });

        var loaded = new JsonSettingsStore(_path).Load();
        Assert.True(loaded.HasCustomHotkey);
        Assert.Equal(8u | 4u, loaded.HotkeyModifiers);
        Assert.Equal(0x56u, loaded.HotkeyVk);
    }

    public void Dispose()
    {
        try { File.Delete(_path); } catch { /* best effort */ }
    }
}

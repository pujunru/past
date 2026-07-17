using System.Runtime.InteropServices;
using Past.Infrastructure.Interop;

namespace Past.Tests;

public class HotkeyChordTests
{
    [Theory]
    [InlineData(8u | 4u, 0x56u, "Win+Shift+V")]   // Win+Shift+V
    [InlineData(8u | 1u, 0x56u, "Win+Alt+V")]     // Win+Alt+V
    [InlineData(2u | 4u, 0x43u, "Ctrl+Shift+C")]  // Ctrl+Shift+C
    [InlineData(8u, 0x71u, "Win+F2")]             // Win+F2
    public void Formats_a_readable_display(uint mods, uint vk, string expected)
    {
        Assert.Equal(expected, HotkeyChord.From(mods, vk).Display);
    }

    [Fact]
    public void From_adds_no_repeat_but_bare_modifiers_round_trip()
    {
        var chord = HotkeyChord.From(8u | 4u, 0x56u);
        Assert.Equal(8u | 4u, chord.BareModifiers);      // what we persist
        Assert.NotEqual(chord.BareModifiers, chord.Modifiers); // MOD_NOREPEAT added for registration
    }
}

/// <summary>
/// Guards the SendInput marshalling layout. This bit us for real: the INPUT union was
/// sized off KEYBDINPUT only (32 bytes on x64) instead of the larger MOUSEINPUT (40),
/// so SendInput rejected cbSize and returned 0 — pasting silently did nothing.
/// </summary>
public class InteropLayoutTests
{
    [Fact]
    public void Input_struct_size_matches_win32_expectation()
    {
        var expected = Environment.Is64BitProcess ? 40 : 28;
        Assert.Equal(expected, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    [Fact]
    public void Input_union_is_sized_by_its_largest_member()
    {
        // MOUSEINPUT is the largest union member; if the union ever shrinks to KEYBDINPUT
        // size again, INPUT goes back to 32 bytes and SendInput starts failing silently.
        Assert.True(Marshal.SizeOf<NativeMethods.MOUSEINPUT>() >= Marshal.SizeOf<NativeMethods.KEYBDINPUT>());
        Assert.Equal(Marshal.SizeOf<NativeMethods.MOUSEINPUT>(), Marshal.SizeOf<NativeMethods.INPUTUNION>());
    }
}

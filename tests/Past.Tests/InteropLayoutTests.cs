using System.Runtime.InteropServices;
using Past.Infrastructure.Interop;

namespace Past.Tests;

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

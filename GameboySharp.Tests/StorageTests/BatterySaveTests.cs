using System;
using System.IO;
using Xunit;

namespace GameboySharp.Tests.StorageTests;

/// <summary>
/// Round-trips battery-backed cartridge RAM through a <c>.sav</c> file. These don't need a real game
/// ROM — the MBCs only care about the RAM size — so they always run (no skip).
/// </summary>
public class BatterySaveTests
{
    private const int RamSize = 8 * 1024;

    [Fact]
    public void Mbc1_BatteryRam_RoundTripsThroughDisk()
    {
        var source = new Mbc1(new byte[0x8000], RamSize, hasBattery: true);
        Assert.True(source.HasBattery);

        byte[] expected = FillWithPattern(source.GetRam());

        string path = TempSavePath();
        try
        {
            BatterySave.Save(path, source);
            Assert.True(File.Exists(path));
            Assert.Equal(RamSize, new FileInfo(path).Length); // no RTC footer for MBC1

            var restored = new Mbc1(new byte[0x8000], RamSize, hasBattery: true);
            BatterySave.Load(path, restored);

            Assert.Equal(expected, restored.GetRam());
        }
        finally
        {
            SafeDelete(path);
        }
    }

    [Fact]
    public void Mbc3_WithRtc_AppendsRtcFooterAndRoundTripsRam()
    {
        var source = new Mbc3(new byte[0x8000], RamSize, hasBattery: true, hasRtc: true);
        Assert.True(source.HasRtc);

        byte[] expected = FillWithPattern(source.GetRam());

        string path = TempSavePath();
        try
        {
            BatterySave.Save(path, source);

            // RAM followed by the 48-byte BGB RTC footer (ten 32-bit registers + a 64-bit timestamp).
            Assert.Equal(RamSize + 48, new FileInfo(path).Length);

            var restored = new Mbc3(new byte[0x8000], RamSize, hasBattery: true, hasRtc: true);
            BatterySave.Load(path, restored); // must consume RAM + RTC footer without error

            Assert.Equal(expected, restored.GetRam());
        }
        finally
        {
            SafeDelete(path);
        }
    }

    private static byte[] FillWithPattern(byte[] ram)
    {
        for (int i = 0; i < ram.Length; i++) ram[i] = (byte)(i * 7);
        return (byte[])ram.Clone();
    }

    private static string TempSavePath() =>
        Path.Combine(Path.GetTempPath(), "gbsharp_battery_" + Guid.NewGuid().ToString("N") + ".sav");

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort cleanup */ }
    }
}

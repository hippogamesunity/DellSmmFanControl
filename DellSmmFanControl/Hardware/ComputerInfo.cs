using System.Security;
using Microsoft.Win32;

namespace DellSmmFanControl.Hardware;

internal static class ComputerInfo
{
    public static string Summary { get; } = ReadSummary();

    private static string ReadSummary()
    {
        string? model = null;
        string? version = null;
        try
        {
            using RegistryKey? bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            model = (bios?.GetValue("SystemProductName") as string)?.Trim();
            version = (bios?.GetValue("BIOSVersion") as string)?.Trim();
        }
        catch (Exception error) when (error is SecurityException or UnauthorizedAccessException or IOException) { }

        return $"{(string.IsNullOrWhiteSpace(model) ? "Model unavailable" : model)} · " +
            $"BIOS {(string.IsNullOrWhiteSpace(version) ? "unavailable" : version)}";
    }
}

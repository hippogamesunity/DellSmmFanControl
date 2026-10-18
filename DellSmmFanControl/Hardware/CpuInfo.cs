using System.Security;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DellSmmFanControl.Hardware;

internal static class CpuInfo
{
    public static string TemperatureTitle { get; } = BuildTemperatureTitle(ReadProcessorName());

    internal static string BuildTemperatureTitle(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "CPU Temperature";
        Match model = Regex.Match(name, @"\bi[3579][-\s]+\d{4,5}[a-z0-9]*\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        string shortName;
        if (model.Success)
            shortName = Regex.Replace(model.Value, @"\s+", "-");
        else
        {
            model = Regex.Match(name, @"\bRyzen\s+[3579]\s+\d{4,5}[a-z0-9]*\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            shortName = model.Success ? model.Value
                : Regex.Replace(name, @"\(R\)|\(TM\)|\bCPU\b|\bProcessor\b|\s+@.*$", "",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            shortName = Regex.Replace(shortName, @"\s+", " ").Trim();
        }
        return shortName.Length > 0 ? $"CPU Temperature · {shortName}" : "CPU Temperature";
    }

    private static string? ReadProcessorName()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("ProcessorNameString") as string;
        }
        catch (Exception error) when (error is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}

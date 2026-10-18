using System.Text.Json;
using System.Text.Json.Serialization;

namespace DellSmmFanControl.Settings;

internal enum FanMode { Min, Normal, Max, Auto }

internal sealed class UserPreferences
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<FanMode>() }
    };
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DellFanControl", "settings.json");

    private sealed record StoredPreferences(FanMode Mode = FanMode.Auto);

    public FanMode LoadMode()
    {
        try
        {
            FanMode mode = JsonSerializer.Deserialize<StoredPreferences>(File.ReadAllText(_path), JsonOptions)?.Mode
                ?? FanMode.Auto;
            return Enum.IsDefined(mode) ? mode : FanMode.Auto;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return FanMode.Auto;
        }
    }

    public void SaveMode(FanMode mode)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new StoredPreferences(mode), JsonOptions));
        File.Move(temporaryPath, _path, overwrite: true);
    }
}

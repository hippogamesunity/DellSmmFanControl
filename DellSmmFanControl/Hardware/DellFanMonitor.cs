using System.ComponentModel;

namespace DellSmmFanControl.Hardware;

internal sealed record FanReading(int Index, int? Rpm, int? MaximumRpm = null)
{
    public int? Percent => Rpm.HasValue && MaximumRpm is > 0
        ? Math.Clamp((int)Math.Round(100.0 * Rpm.Value / MaximumRpm.Value), 0, 100) : null;
}

internal sealed record FanSnapshot(DateTimeOffset Timestamp, FanReading[] Fans,
    int? CpuTemperature = null, string? TemperatureError = null);

internal sealed record FanProfile(int Index, int? MiddleRpm, int? MaximumRpm,
    int? LowestRpm = null, bool LevelZeroObservedRunning = false)
{
    public bool SupportsMinimum => LowestRpm.HasValue || LevelZeroObservedRunning;
}

internal sealed class DellFanMonitor : IDisposable
{
    internal const uint GetFanRpmCommand = 0x02A3;
    private readonly DellSmmSession _session = new();
    private readonly List<FanProfile> _profiles = [];
    private readonly List<int> _cpuSensors = [];
    private bool _discovered;

    public IReadOnlyList<FanProfile> Profiles => _profiles;

    internal DellSmmSession Hardware => _session;

    private void Discover()
    {
        if (_discovered)
            return;
        _discovered = true;
        for (int index = 0; index < 2; index++)
        {
            try
            {
                int rpm = DecodeRpm(_session.QueryFanRpm(index));
                int? level = TryRead(() => _session.QueryFanLevel(index));
                int? low = TryRead(() => _session.QueryNominalFanRpm(index, 1));
                int? high = TryRead(() => _session.QueryNominalFanRpm(index, 2));
                int? lowest = TryRead(() => _session.QueryNominalFanRpm(index, 0));
                low = ValidNominal(low);
                high = ValidNominal(high);
                lowest = ValidNominal(lowest) ?? ValidNominal(_session.GetMeasuredMinimumRpm(index));
                if (low.HasValue && high <= low)
                    high = null;
                if (low.HasValue && lowest >= low)
                    lowest = null;
                // Nominal RPM is optional metadata, not a prerequisite for setting a state.
                _profiles.Add(new FanProfile(index, low, high, lowest, level == 0 && rpm > 0));
            }
            catch (Exception error) when (error is Win32Exception or InvalidDataException) { }
        }
        DiscoverCpuSensors();
    }

    private void DiscoverCpuSensors()
    {
        _cpuSensors.Clear();
        for (int index = 0; index < 10; index++)
        {
            try
            {
                // Linux dell-smm-hwmon uses zero-based sensor types: 0 is CPU.
                if (_session.QuerySensorType(index) == 0)
                    _cpuSensors.Add(index);
            }
            catch (Exception error) when (error is Win32Exception or InvalidDataException) { }
        }
    }

    private static int? ValidNominal(int? rpm) => rpm is > 0 and <= 30000 ? rpm : null;

    private static int? TryRead(Func<int> read)
    {
        try { return read(); }
        catch (Exception error) when (error is Win32Exception or InvalidDataException) { return null; }
    }

    public FanSnapshot Read()
    {
        Discover();
        FanReading[] fans = new FanReading[2];
        for (int index = 0; index < fans.Length; index++)
        {
            try
            {
                uint eax = _session.QueryFanRpm(index);
                FanProfile? profile = _profiles.Find(profile => profile.Index == index);
                fans[index] = new FanReading(index, DecodeRpm(eax), profile?.MaximumRpm);
            }
            catch (Exception error) when (error is Win32Exception or InvalidDataException)
            {
                // An absent second fan must not hide the working first fan.
                fans[index] = new FanReading(index, null);
            }
        }
        (int? cpuTemperature, string? temperatureError) = ReadCpuTemperature();
        return new FanSnapshot(DateTimeOffset.Now, fans, cpuTemperature, temperatureError);
    }

    private (int? Temperature, string? Error) ReadCpuTemperature()
    {
        string? errorMessage = null;
        // Firmware may temporarily lose a sensor after sleep. Rediscover and retry
        // when none can be read, instead of retaining an unusable sensor list.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            int? hottest = null;
            foreach (int sensor in _cpuSensors)
            {
                try
                {
                    int temperature = _session.QueryTemperature(sensor);
                    if (temperature is < 1 or > 127)
                        throw new InvalidDataException($"Invalid CPU temperature from BIOS sensor {sensor}.");
                    hottest = hottest.HasValue ? Math.Max(hottest.Value, temperature) : temperature;
                }
                catch (Exception error) when (error is Win32Exception or InvalidDataException)
                {
                    // One unavailable sensor must not discard other valid CPU readings.
                    errorMessage = error.Message;
                }
            }
            if (hottest.HasValue)
                return (hottest, null);
            if (attempt == 0)
                DiscoverCpuSensors();
        }
        // Never reuse a stale reading or substitute an ambient/ACPI sensor for CPU.
        return (null, errorMessage ?? "CPU temperature sensor is unavailable.");
    }

    internal static int DecodeRpm(ulong eax)
    {
        // Firmware failures can leave EAX unchanged or return 0xffff.
        uint result = (uint)eax;
        if (result == GetFanRpmCommand || (result & 0xffff) == 0xffff)
            throw new InvalidDataException($"BIOS did not return a fan speed (EAX=0x{result:X8}).");
        return (int)(result & 0xffff);
    }

    public string? CleanupError => _session.CleanupError;

    public void Dispose() => _session.Dispose();
}

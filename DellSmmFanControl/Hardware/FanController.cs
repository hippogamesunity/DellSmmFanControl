namespace DellSmmFanControl.Hardware;

internal sealed record FanControlRequest(bool Auto, int Level, int Revision = 0);
internal sealed record FanControlResult(FanSnapshot Snapshot, bool CanControl, bool LowestLevelAvailable,
    string Message, int? AppliedLevel = null);

internal sealed class FanController : IDisposable
{
    private readonly DellFanMonitor _monitor;
    private readonly SilentFanPolicy _silent = new();
    private FanProfile[]? _activeFans;
    private bool _attemptedControl;
    private bool _wasAuto = true;
    private string? _fault;
    private bool _emergency;
    private bool _restoreFailed;
    private FanControlRequest? _lastRequest;
    private readonly Dictionary<int, FanTarget> _targets = [];
    private sealed record FanTarget(int Level, DateTimeOffset ChangedAt);

    public FanController(DellFanMonitor monitor) => _monitor = monitor;

    public FanControlResult Tick(FanControlRequest request)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(request.Level);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(request.Level, 2);
        if (_lastRequest != request)
        {
            if (!_restoreFailed)
                _fault = null;
            _lastRequest = request;
        }
        FanSnapshot snapshot = _monitor.Read();
        _activeFans = (_activeFans ?? []).Concat(_monitor.Profiles.Where(profile =>
            snapshot.Fans.Any(fan => fan.Index == profile.Index && fan.Rpm is > 0)))
            .DistinctBy(profile => profile.Index).ToArray();
        bool lowestAvailable = _activeFans.Length > 0 && _activeFans.All(fan => fan.SupportsMinimum);
        string? reason = _fault;
        if (reason is null && !request.Auto && request.Level == 0 && !lowestAvailable)
            reason = "BIOS has not confirmed that Min keeps the fan spinning";
        // Manual modes also need a live CPU reading for overheat protection.
        if (reason is null && snapshot.CpuTemperature is null)
            reason = snapshot.TemperatureError ?? "CPU temperature sensor is unavailable";
        if (reason is null && (_activeFans.Length == 0 || snapshot.Fans.Any(fan => fan.Rpm is > 0 &&
            !_activeFans.Any(profile => profile.Index == fan.Index))))
            reason = "No fans are available for control";

        if (reason is not null)
        {
            ReleaseControl();
            return new(snapshot, false, lowestAvailable, FallbackMessage(_fault ?? reason));
        }

        try
        {
            // Do not send speed commands to a stopped/unreadable channel. If a
            // previously controlled fan stops, return cooling control to BIOS.
            if (_activeFans.Any(fan => snapshot.Fans.Single(reading => reading.Index == fan.Index).Rpm is null or <= 0))
                throw new InvalidOperationException("Fan rotation could not be confirmed");
            if (!_attemptedControl)
            {
                _attemptedControl = true;
                _monitor.Hardware.TakeFanControl();
            }
            if (_wasAuto != request.Auto)
                _silent.Reset();
            _wasAuto = request.Auto;
            int? temperature = snapshot.CpuTemperature;
            int autoLevel = request.Auto
                ? _silent.SelectLevel(temperature!.Value, snapshot.Timestamp, lowestAvailable ? 0 : 1) : 1;
            if (temperature >= 85)
                _emergency = true;
            else if (temperature <= 75)
                _emergency = false;
            bool waitingForRpm = false;
            int level = _emergency ? 2 : request.Auto ? autoLevel : request.Level;
            foreach (FanProfile fan in _activeFans)
            {
                FanReading reading = snapshot.Fans.Single(reading => reading.Index == fan.Index);
                bool changed = !_targets.TryGetValue(fan.Index, out FanTarget? target) || target.Level != level;
                target = changed ? new FanTarget(level, snapshot.Timestamp) : target!;
                int? expectedRpm = level switch { 0 => fan.LowestRpm, 1 => fan.MiddleRpm, _ => fan.MaximumRpm };
                bool reached = expectedRpm.HasValue && Math.Abs(reading.Rpm!.Value - expectedRpm.Value)
                    <= Math.Max(150, expectedRpm.Value * 0.2);
                if (!changed && expectedRpm.HasValue && !reached &&
                    snapshot.Timestamp - target.ChangedAt >= TimeSpan.FromSeconds(60))
                    throw new InvalidOperationException($"Fan {fan.Index + 1} did not reach {ModeName(level)}: " +
                        $"{reading.Rpm} RPM measured, approximately {expectedRpm} RPM expected.");
                // Reapply the target on each poll without depending on 0x00A3.
                // This also handles firmware resets after sleep/resume.
                _monitor.Hardware.SetFanLevel(fan.Index, level);
                _targets[fan.Index] = target;
                waitingForRpm |= expectedRpm.HasValue && !reached;
            }
            string message = _emergency ? "Maximum cooling · overheat protection"
                : request.Auto ? $"Auto · Silent · {ModeName(autoLevel)}"
                : $"Manual mode · {ModeName(request.Level)}";
            if (waitingForRpm)
                message += " · waiting for RPM";
            return new(snapshot, true, lowestAvailable, message, level);
        }
        catch (Exception error)
        {
            _fault = error.Message;
            ReleaseControl();
            return new(snapshot, false, lowestAvailable, FallbackMessage(_fault));
        }
    }

    private string FallbackMessage(string reason) => _restoreFailed
        ? $"Cooling error · {reason}" : reason;

    private static string ModeName(int level) => level switch { 0 => "Min", 1 => "Normal", _ => "Max" };

    private void ReleaseControl()
    {
        if (!_attemptedControl)
            return;
        try
        {
            _monitor.Hardware.RestoreBiosControl();
            _restoreFailed = false;
            _attemptedControl = false;
            _silent.Reset();
            _targets.Clear();
        }
        catch (Exception error)
        {
            _restoreFailed = true;
            _fault = $"Failed to restore automatic firmware control: {error.Message}";
            foreach (FanProfile fan in _activeFans ?? [])
            {
                try { _monitor.Hardware.SetFanLevel(fan.Index, 2); }
                catch (Exception) { }
            }
        }
    }

    public void Dispose()
    {
        ReleaseControl();
        _monitor.Dispose();
    }
}

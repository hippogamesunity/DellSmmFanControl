using System.Runtime.InteropServices;

namespace DellSmmFanControl.Hardware;

// Used only while Auto has no temperature reading. GetSystemTimes includes idle
// time in kernel time; subtract it to get utilization across all logical CPUs.
internal sealed class CpuLoadFanPolicy
{
    private const long WindowMilliseconds = 60_000;
    private const long MaximumSampleGapMilliseconds = 10_000;
    private readonly Queue<LoadInterval> _intervals = new();
    private CpuTimes? _previous;
    private readonly record struct CpuTimes(long Timestamp, ulong Idle, ulong Total);
    private readonly record struct LoadInterval(long Start, long End, double Percent);

    public double? AveragePercent { get; private set; }
    public int SampledSeconds { get; private set; }

    public int SelectLevel()
    {
        long now = Environment.TickCount64;
        if (!GetSystemTimes(out ulong idle, out ulong kernel, out ulong user))
        {
            Reset();
            return 0;
        }
        var current = new CpuTimes(now, idle, kernel + user);
        if (_previous is not CpuTimes previous || now - previous.Timestamp > MaximumSampleGapMilliseconds ||
            current.Total < previous.Total || current.Idle < previous.Idle)
        {
            // A new fallback period, suspend or counter reset starts a fresh minute.
            Reset();
            _previous = current;
            return 0;
        }
        ulong totalDelta = current.Total - previous.Total;
        ulong idleDelta = current.Idle - previous.Idle;
        if (now <= previous.Timestamp || totalDelta == 0)
            return CurrentLevel;
        if (idleDelta > totalDelta)
        {
            Reset();
            _previous = current;
            return 0;
        }

        double percent = 100.0 * (totalDelta - idleDelta) / totalDelta;
        _intervals.Enqueue(new LoadInterval(previous.Timestamp, now, percent));
        _previous = current;
        long cutoff = now - WindowMilliseconds;
        while (_intervals.TryPeek(out LoadInterval oldest) && oldest.End <= cutoff)
            _intervals.Dequeue();

        double weightedLoad = 0;
        long duration = 0;
        foreach (LoadInterval interval in _intervals)
        {
            long milliseconds = interval.End - Math.Max(interval.Start, cutoff);
            weightedLoad += interval.Percent * milliseconds;
            duration += milliseconds;
        }
        // Weight by time, not sample count: mode clicks can trigger extra polls.
        AveragePercent = duration > 0 ? weightedLoad / duration : null;
        SampledSeconds = (int)(duration / 1000);
        return CurrentLevel;
    }

    private int CurrentLevel => SampledSeconds >= 60 && AveragePercent is > 90 ? 1 : 0;

    public void Reset()
    {
        _previous = null;
        _intervals.Clear();
        AveragePercent = null;
        SampledSeconds = 0;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
}

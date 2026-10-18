namespace DellSmmFanControl.Hardware;

// Escalate immediately at 65/85 C; lower one step after 20 seconds below
// the corresponding 55/75 C cooldown threshold. Unsupported level 0 is skipped.
internal sealed class SilentFanPolicy
{
    private int _level;
    private DateTimeOffset? _coolSince;

    public int SelectLevel(int temperature, DateTimeOffset now, int minimumLevel = 0)
    {
        int target = Math.Max(minimumLevel, temperature >= 85 ? 2 : temperature >= 65 ? 1 : 0);
        _level = Math.Max(_level, minimumLevel);
        if (target > _level)
        {
            _level = target;
            _coolSince = null;
        }
        else if (target < _level && temperature <= (_level == 2 ? 75 : 55))
        {
            _coolSince ??= now;
            if (now - _coolSince.Value >= TimeSpan.FromSeconds(20))
            {
                _level--;
                _coolSince = null;
            }
        }
        else
        {
            _coolSince = null;
        }
        return _level;
    }

    public void Reset()
    {
        _level = 0;
        _coolSince = null;
    }
}

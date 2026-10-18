namespace DellSmmFanControl.Hardware;

internal static class DellFanModelProfile
{
    // User-measured CPU fan speeds on this board/firmware: approximately
    // 900 / 1600 / 4170 RPM. Its nominal table reports zero for level 0,
    // and Get Fan State is unavailable, so neither confirms the running floor.
    // Keep this measured override scoped to the identified board and CPU fan.
    public static int? GetMeasuredMinimumRpm(string? model, string? board, string? bios, int fanIndex)
    {
        return fanIndex == 0 &&
            string.Equals(model?.Trim(), "OptiPlex 9010", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(board?.Trim(), "03JR84", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(bios?.Trim(), "A30", StringComparison.OrdinalIgnoreCase)
            ? 900 : null;
    }
}

namespace WindowsDynamicIsland.Models;

public sealed record PowerSnapshot(
    int ChargePercent,
    bool IsPluggedIn);

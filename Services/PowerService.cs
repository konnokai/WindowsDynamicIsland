using Windows.System.Power;
using WindowsDynamicIsland.Models;

namespace WindowsDynamicIsland.Services;

public sealed class PowerService
{
    public event EventHandler<PowerSnapshot?>? SnapshotChanged;

    public void Initialize()
    {
        PowerManager.RemainingChargePercentChanged += OnPowerChanged;
        PowerManager.PowerSupplyStatusChanged += OnPowerChanged;
        Publish();
    }

    private void OnPowerChanged(object? sender, object args)
    {
        Publish();
    }

    private void Publish()
    {
        try
        {
            if (PowerManager.BatteryStatus == BatteryStatus.NotPresent)
            {
                SnapshotChanged?.Invoke(this, null);
                return;
            }

            var isPluggedIn = PowerManager.BatteryStatus is BatteryStatus.Charging or BatteryStatus.Idle;
            SnapshotChanged?.Invoke(this, new PowerSnapshot(
                PowerManager.RemainingChargePercent,
                isPluggedIn));
        }
        catch (Exception)
        {
            SnapshotChanged?.Invoke(this, null);
        }
    }
}

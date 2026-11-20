using ASCII_Hero.Client.Game.Browser;
using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.Physics;

/// <summary>
/// The post-hit safe window (<see cref="HitRecovery"/>) shared by the player and killable hazards,
/// each with its own duration.
/// </summary>
[TestClass]
public sealed class HitRecoveryTests
{
    [TestMethod]
    public void NewWindow_IsNotActive()
    {
        var recovery = new HitRecovery(durationSeconds: 1.0);

        Assert.IsFalse(recovery.IsActive);
        Assert.AreEqual(0.0, recovery.RemainingSeconds);
    }

    [TestMethod]
    public void Started_RunsForItsDurationThenEnds()
    {
        var recovery = new HitRecovery(durationSeconds: 0.5);
        recovery.Start();

        recovery.Tick(0.3);
        Assert.IsTrue(recovery.IsActive);

        recovery.Tick(0.3);
        Assert.IsFalse(recovery.IsActive);
        Assert.AreEqual(0.0, recovery.RemainingSeconds);
    }

    [TestMethod]
    public void Clear_EndsTheWindowAtOnce()
    {
        var recovery = new HitRecovery(durationSeconds: 1.0);
        recovery.Start();

        recovery.Clear();

        Assert.IsFalse(recovery.IsActive);
    }

    [TestMethod]
    public async Task PlayerAndHazard_GetTheirOwnDefaultDurations()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "Enemies");
        var snake = world.Objects.OfType<DynamicHazard2D>().First(hazard => hazard.IsKillable);

        Assert.AreEqual(GameDefaults.DefaultPlayerRecoverySeconds, world.Player.Recovery.DurationSeconds, 1e-9);
        Assert.AreEqual(GameDefaults.DefaultHazardRecoverySeconds, snake.Recovery.DurationSeconds, 1e-9);
        Assert.IsLessThan(world.Player.Recovery.DurationSeconds, snake.Recovery.DurationSeconds);
    }
}

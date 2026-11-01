using ASCII_Hero.Client.Game.Browser;
using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.Physics;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.Physics;

/// <summary>
/// Changes a sleeping body would not notice through contact alone - a gate opening beneath it, or
/// its own collision shape changing - must still wake it.
/// </summary>
[TestClass]
public sealed class SleepWakeTests
{
    private static void Run(World2D world, int steps)
    {
        var input = new InputState();
        var physics = new PhysicsSystem();
        var collision = new CollisionSystem();
        physics.Reset(input);
        for (var i = 0; i < steps; i++)
        {
            physics.Step(world, input, PhysicsConstants.FixedPhysicsStepSeconds);
            collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
            world.ApplyPendingRemovals();
        }
    }

    [TestMethod]
    public async Task OpeningGateUnderSleepingCrate_WakesItAndItFalls()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "Doors");
        var crates = await World2D.LoadAsync(new DiskAssetFileProvider(), "Crates");
        var crate = crates.Objects.OfType<DynamicObject2D>().First();
        var hatch = world.Objects.OfType<Gate2D>().Single(gate => gate.LinkName == "GreyHatch");
        var lever = world.Objects.OfType<Switch2D>().Single(switchObject => switchObject.LinkName == "GreyHatch");

        crate.Position = new Vector2D(hatch.Left + (hatch.Size.X - crate.Size.X) / 2, hatch.Top - crate.Size.Y);
        crate.Velocity = Vector2D.Zero;
        world.Objects.Add(crate);
        Run(world, steps: 120);
        Assert.IsTrue(crate.IsSleeping, "crate should be asleep on the closed hatch");
        var restingY = crate.Position.Y;

        world.Player.Position = lever.Position;
        Assert.IsTrue(world.TryOperateSwitch());

        Assert.IsFalse(crate.IsSleeping);
        Run(world, steps: 60);
        Assert.IsGreaterThan(restingY + 0.5, crate.Position.Y);
    }
}

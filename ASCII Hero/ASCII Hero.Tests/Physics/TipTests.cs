using ASCII_Hero.Client.Game.Browser;
using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.Physics;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.Physics;

/// <summary>
/// Tipping off a ledge (see <see cref="TipSystem"/>): a body whose center of mass overhangs what it
/// stands on slides off, a balanced one stays, and the player keeps its footing a little further
/// past an edge. Run as real physics steps in the shipped Throwables world (stones on a floor).
/// </summary>
[TestClass]
public sealed class TipTests
{
    private static async Task<(World2D World, ThrowableObject2D Below, ThrowableObject2D Above)> LoadAsync()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "Throwables");
        Run(world, 4.0);
        var stones = world.Objects.OfType<ThrowableObject2D>().OrderBy(stone => stone.Position.X).ToList();
        return (world, stones[0], stones[1]);
    }

    private static void Run(World2D world, double seconds)
    {
        var input = new InputState();
        var physics = new PhysicsSystem();
        var collision = new CollisionSystem();
        physics.Reset(input);
        var steps = (int)Math.Round(seconds / PhysicsConstants.FixedPhysicsStepSeconds);
        for (var i = 0; i < steps; i++)
        {
            physics.Step(world, input, PhysicsConstants.FixedPhysicsStepSeconds);
            collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
            world.ApplyPendingRemovals();
        }
    }

    private static void PlaceOn(Body2D body, Body2D support, double offsetX)
    {
        body.Position = new Vector2D(x: support.Position.X + offsetX, y: support.Position.Y - body.Size.Y - 0.01);
        if (body is IPhysicsBody physicsBody)
        {
            physicsBody.Velocity = new Vector2D(0, 0);
        }

        body.Wake();
    }

    private static bool StandsOn(Body2D body, Body2D support) =>
        body.GetContactingBodies(ContactType.SurfaceBottom).Contains(support);

    /// <summary>A throwable whose gear isn't listed can't be picked up, so the player can stand on it.</summary>
    private static void MakeUnpickable(ThrowableObject2D stone) => stone.GearName = "Unlisted";

    [TestMethod]
    public async Task StoneBarelyOverlappingStoneBelow_SlidesOff()
    {
        var (world, below, above) = await LoadAsync();
        PlaceOn(above, below, offsetX: 0.9);

        Run(world, 2.0);

        Assert.IsFalse(StandsOn(above, below));
        Assert.AreEqual(below.Position.Y, above.Position.Y, 0.05, "The stone should have fallen to the floor beside the other one.");
    }

    [TestMethod]
    public async Task StoneWithCenterOverStoneBelow_StaysStacked()
    {
        var (world, below, above) = await LoadAsync();
        PlaceOn(above, below, offsetX: 0.3);

        Run(world, 2.0);

        Assert.IsTrue(StandsOn(above, below));
        Assert.IsFalse(above.IsTipping);
    }

    [TestMethod]
    public async Task PlayerWithMostOfAFootOnStone_KeepsFooting()
    {
        var (world, below, _) = await LoadAsync();
        var player = world.Player;
        MakeUnpickable(below);
        PlaceOn(player, below, offsetX: -1.7);

        Run(world, 1.0);

        Assert.IsTrue(StandsOn(player, below));
    }

    [TestMethod]
    public async Task PlayerWithLittleOfAFootOnStone_TipsOff()
    {
        var (world, below, _) = await LoadAsync();
        var player = world.Player;
        MakeUnpickable(below);
        PlaceOn(player, below, offsetX: -2.7);

        Run(world, 1.0);

        Assert.Contains(below, world.Objects);
        Assert.IsFalse(StandsOn(player, below));
    }
}

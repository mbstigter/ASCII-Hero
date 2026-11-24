using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Browser;
using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.Physics;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.Physics;

/// <summary>
/// A player standing on a movable support: carried along by a patrolling one, and not shoving a
/// crate away just by walking on it.
/// </summary>
[TestClass]
public sealed class SupportCarryTests
{
    private static void Run(World2D world, InputState input, PhysicsSystem physics, CollisionSystem collision, double seconds)
    {
        var steps = (int)Math.Round(seconds / PhysicsConstants.FixedPhysicsStepSeconds);
        for (var i = 0; i < steps; i++)
        {
            physics.Step(world, input, PhysicsConstants.FixedPhysicsStepSeconds);
            collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
            world.ApplyPendingRemovals();
        }
    }

    private static void PlaceOnTop(Player2D player, Body2D support)
    {
        player.Position = new Vector2D(
            x: support.Position.X + (support.Size.X - player.Size.X) / 2,
            y: support.Position.Y - player.Size.Y - 0.05);
        player.Velocity = new Vector2D(0, 0);
    }

    [TestMethod]
    public async Task PatrollingSupport_CarriesIdlePlayer_WithoutSliding()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "Crates");
        var support = world.Objects.OfType<DynamicObject2D>().First();
        foreach (var other in world.Objects.OfType<DynamicObject2D>().Where(body => body != support).ToList())
        {
            world.QueueRemoval(other);
        }
        world.ApplyPendingRemovals();
        support.Position = new Vector2D(40, 20);
        support.Friction = 0.8;
        support.SetPatrol(
            patrolMinX: 35, patrolMaxX: 70, cruiseSpeedX: 2,
            patrolMinY: null, patrolMaxY: null, cruiseSpeedY: 0,
            forceMultiplierX: 40, initialDirectionRight: true);

        var player = world.Player;
        var input = new InputState();
        var physics = new PhysicsSystem();
        var collision = new CollisionSystem();
        physics.Reset(input);

        Run(world, input, physics, collision, 0.5);
        PlaceOnTop(player, support);
        Run(world, input, physics, collision, 0.5);
        Assert.IsTrue(player.GetContactingBodies(ContactType.SurfaceBottom).Contains(support));

        var supportStartX = support.Position.X;
        var offsetStart = player.Position.X - support.Position.X;
        Run(world, input, physics, collision, 2.0);

        Assert.IsTrue(player.GetContactingBodies(ContactType.SurfaceBottom).Contains(support));
        Assert.IsGreaterThan(1.0, support.Position.X - supportStartX, "The support should keep moving with the player on top.");
        Assert.AreEqual(offsetStart, player.Position.X - support.Position.X, 0.05, "An idle player should ride along without sliding on the support.");
    }

    [TestMethod]
    public async Task WalkingOffCrate_DoesNotShoveIt()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "Crates");
        var input = new InputState();
        var physics = new PhysicsSystem();
        var collision = new CollisionSystem();
        physics.Reset(input);
        Run(world, input, physics, collision, 3.0);
        var player = world.Player;
        var crate = world.Objects.OfType<DynamicObject2D>()
            .Where(c => c.IsGrounded && c.GetContactingBodies(ContactType.SurfaceTop).Count == 0 && c.Size.X >= 3)
            .OrderBy(c => c.Mass)
            .First();
        PlaceOnTop(player, crate);
        Run(world, input, physics, collision, 0.5);
        Assert.IsTrue(player.GetContactingBodies(ContactType.SurfaceBottom).Contains(crate));

        var crateStartX = crate.Position.X;
        input.KeyDown("ArrowRight");
        Run(world, input, physics, collision, 0.3);

        Assert.AreEqual(crateStartX, crate.Position.X, 0.02, "Walking on a crate resting on an ordinary floor should not slide it backward.");
    }
}

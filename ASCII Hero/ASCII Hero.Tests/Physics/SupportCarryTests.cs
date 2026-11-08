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
    public async Task PatrollingTurtle_CarriesIdlePlayer_WithoutSliding()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "Enemies");
        foreach (var hazard in world.Objects.OfType<IHazardBody>().Cast<Body2D>().ToList())
        {
            world.QueueRemoval(hazard);
        }
        world.ApplyPendingRemovals();
        var turtle = world.Objects.OfType<DynamicObject2D>().First(o => o.Sprite.Name == "Turtle");
        var player = world.Player;
        var input = new InputState();
        var physics = new PhysicsSystem();
        var collision = new CollisionSystem();
        physics.Reset(input);

        // Clear of the low beam that covers the start of its route.
        turtle.Position = new Vector2D(62, turtle.Position.Y);
        Run(world, input, physics, collision, 0.5);
        PlaceOnTop(player, turtle);
        Run(world, input, physics, collision, 0.5);
        Assert.IsTrue(player.GetContactingBodies(ContactType.SurfaceBottom).Contains(turtle));

        var turtleStartX = turtle.Position.X;
        var offsetStart = player.Position.X - turtle.Position.X;
        Run(world, input, physics, collision, 2.0);

        Assert.IsTrue(player.GetContactingBodies(ContactType.SurfaceBottom).Contains(turtle));
        Assert.IsGreaterThan(1.0, turtle.Position.X - turtleStartX, "The turtle should keep walking with the player on its back.");
        Assert.AreEqual(offsetStart, player.Position.X - turtle.Position.X, 0.05, "An idle player should ride along without sliding on the turtle's back.");
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

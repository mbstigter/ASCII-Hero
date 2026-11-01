using ASCII_Hero.Client.Game.Browser;
using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.Physics;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.Physics;

/// <summary>
/// While climbing with no Left/Right held, the player is gently pulled toward the horizontal
/// centre of the climbable; holding Left/Right still side-steps freely.
/// </summary>
[TestClass]
public sealed class ClimbCenteringTests
{
    private const double StartOffset = 0.75;
    private const int Steps = 60;

    private static async Task<(World2D World, Player2D Player, Body2D Climbable)> GrabClimbableOffCentreAsync()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "TheMountains");
        var climbable = world.Objects.First(body => body.IsStatic && body.IsClimbable && body.Size.Y > body.Size.X);
        var player = world.Player;
        player.Position = new Vector2D(
            x: climbable.Position.X + climbable.Size.X / 2 - player.Size.X / 2 + StartOffset,
            y: climbable.Position.Y + climbable.Size.Y / 2 - player.Size.Y / 2);
        player.Velocity = new Vector2D(0, 0);
        player.IsTouchingClimbable = true;
        player.IsClimbing = true;
        new CollisionSystem().Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        Assert.IsTrue(player.IsTouchingClimbable, "Test setup must overlap the climbable.");
        return (world, player, climbable);
    }

    private static double OffsetFromCentre(Player2D player, Body2D climbable) =>
        (player.Position.X + player.Size.X / 2) - (climbable.Position.X + climbable.Size.X / 2);

    private static void Run(World2D world, InputState input)
    {
        var physics = new PhysicsSystem();
        var collision = new CollisionSystem();
        for (var i = 0; i < Steps; i++)
        {
            physics.Step(world, input, PhysicsConstants.FixedPhysicsStepSeconds);
            collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        }
    }

    [TestMethod]
    public async Task ClimbingWithoutSideInput_DriftsToClimbableCentre()
    {
        var (world, player, climbable) = await GrabClimbableOffCentreAsync();

        Run(world, new InputState());

        Assert.IsTrue(player.IsClimbing);
        Assert.AreEqual(0.0, OffsetFromCentre(player, climbable), 0.05);
    }

    [TestMethod]
    public async Task ClimbingWithSideInput_IsNotPulledBack()
    {
        var (world, player, climbable) = await GrabClimbableOffCentreAsync();
        var input = new InputState();
        input.KeyDown("ArrowRight");

        Run(world, input);

        Assert.IsGreaterThan(StartOffset, OffsetFromCentre(player, climbable));
    }
}

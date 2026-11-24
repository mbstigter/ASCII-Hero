using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Browser;
using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.Physics;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.Physics;

[TestClass]
public sealed class GripStrengthTests
{
    private sealed class GravityOverrideProvider(string gravity) : IAssetFileProvider
    {
        private readonly DiskAssetFileProvider _disk = new();

        public Task<string?> TryReadTextAsync(string relativePath) =>
            relativePath.EndsWith("MovingClimbables_settings.ini", StringComparison.Ordinal)
                ? Task.FromResult<string?>($"[Physics]\nGravity = {gravity}\n")
                : _disk.TryReadTextAsync(relativePath);
    }

    private static async Task<(World2D World, KinematicObject2D Grip)> CreateGripAsync(string gravity, bool hanging, double friction)
    {
        var world = await World2D.LoadAsync(new GravityOverrideProvider(gravity), "MovingClimbables");
        var player = world.Player;
        var grip = world.Objects.OfType<KinematicObject2D>().First(body => hanging ? body.IsHangable : body.IsClimbable);
        grip.Velocity = Vector2D.Zero;
        grip.ConveyorVelocity = Vector2D.Zero;
        player.Position = new Vector2D(x: grip.Position.X + (grip.Size.X - player.Size.X) / 2, y: player.Position.Y);
        player.Velocity = Vector2D.Zero;
        player.Friction = 1;
        grip.Friction = friction;
        if (hanging)
        {
            player.IsTouchingHangable = true;
            player.IsHanging = true;
            player.AddContact(ContactType.Hangable, grip);
        }
        else
        {
            player.IsTouchingClimbable = true;
            player.IsClimbing = true;
            player.AddContact(ContactType.Climbable, grip);
        }
        return (world, grip);
    }

    [TestMethod]
    [DataRow("0", false)]
    [DataRow("40", false)]
    [DataRow("100", false)]
    [DataRow("0", true)]
    [DataRow("40", true)]
    [DataRow("100", true)]
    public async Task GripJump_UsesSameStrengthAtDifferentGravity(string gravity, bool hanging)
    {
        var (world, _) = await CreateGripAsync(gravity, hanging, friction: 0.01);
        var input = new InputState();
        input.KeyDown("Space");

        new PhysicsSystem().Step(world, input, 0);

        Assert.AreEqual(-4.0, world.Player.Velocity.Y, 1e-8);
        Assert.IsFalse(world.Player.IsClimbing);
        Assert.IsFalse(world.Player.IsHanging);
    }

    [TestMethod]
    [DataRow("0", false)]
    [DataRow("40", false)]
    [DataRow("100", false)]
    [DataRow("0", true)]
    [DataRow("40", true)]
    [DataRow("100", true)]
    public async Task GripMotor_ForceLimitIsIndependentOfGravity(string gravity, bool hanging)
    {
        var (world, _) = await CreateGripAsync(gravity, hanging, friction: 0.01);
        var input = new InputState();
        input.KeyDown("ArrowRight");

        new PhysicsSystem().Step(world, input, 0);

        var force = world.Player.MotorForce;
        var forcePerMass = Math.Sqrt(force.X * force.X + force.Y * force.Y) / world.Player.Mass;
        Assert.AreEqual(40.0, forcePerMass, 1e-8);
        Assert.IsGreaterThan(0, force.X);
    }

    [TestMethod]
    public async Task GripMotor_GravityAboveStrengthExceedsHoldCapacity()
    {
        var (world, _) = await CreateGripAsync("800", hanging: false, friction: 1);

        new PhysicsSystem().Step(world, new InputState(), PhysicsConstants.FixedPhysicsStepSeconds);

        Assert.AreEqual(-400.0, world.Player.MotorForce.Y / world.Player.Mass, 1e-8);
        Assert.IsGreaterThan(0, world.Player.Velocity.Y);
    }

    [TestMethod]
    public async Task GripMotor_ZeroFrictionProvidesNoGripForce()
    {
        var (world, _) = await CreateGripAsync("0", hanging: false, friction: 0);
        var input = new InputState();
        input.KeyDown("ArrowUp");

        new PhysicsSystem().Step(world, input, 0);

        Assert.AreEqual(0, world.Player.MotorForce.X, 1e-8);
        Assert.AreEqual(0, world.Player.MotorForce.Y, 1e-8);
    }
}

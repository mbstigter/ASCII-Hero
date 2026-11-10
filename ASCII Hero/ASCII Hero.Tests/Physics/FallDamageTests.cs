using ASCII_Hero.Client.Game.Browser;
using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.Physics;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.Physics;

/// <summary>
/// Landing hard enough on an ordinary surface costs the player <see cref="Player2D.FallDamage"/>;
/// ordinary landings and stomps on hazards do not.
/// </summary>
[TestClass]
public sealed class FallDamageTests
{
    private const double Penetration = 0.1;

    /// <summary>Loads the Crates world and lets the player settle on the floor.</summary>
    private static async Task<World2D> LoadGroundedPlayerAsync()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "Crates");
        var input = new InputState();
        var physics = new PhysicsSystem();
        var collision = new CollisionSystem();
        physics.Reset(input);
        for (var i = 0; i < 300; i++)
        {
            physics.Step(world, input, PhysicsConstants.FixedPhysicsStepSeconds);
            collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
            world.ApplyPendingRemovals();
        }

        Assert.IsTrue(world.Player.IsGrounded, "the player should have settled on the floor");

        // The Crates world spawns the player in the air; start each test from a full bar.
        world.Player.Health = GameDefaults.PlayerFullHealth;
        return world;
    }

    /// <summary>Resolves the player arriving on the surface it rests on at <paramref name="speed"/>.</summary>
    private static void LandAt(World2D world, double speed)
    {
        var player = world.Player;
        player.Position = new Vector2D(player.Position.X, player.Position.Y + Penetration);
        player.Velocity = new Vector2D(0, speed);
        new CollisionSystem().Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        world.ApplyPendingRemovals();
    }

    [TestMethod]
    public void DefaultFallDamageImpactSpeed_MatchesTenCellDropAtDefaultGravity()
    {
        Assert.AreEqual(Math.Sqrt(2 * PhysicsConstants.DefaultGravity * 10), GameDefaults.DefaultFallDamageImpactSpeed, 1e-9);
        Assert.IsLessThan(GameDefaults.DefaultFallDamageImpactSpeed, GameDefaults.GroundJumpSpeed, "landing from a plain jump must stay safe");
    }

    [TestMethod]
    public async Task HardLanding_RemovesFallDamage()
    {
        var world = await LoadGroundedPlayerAsync();
        var player = world.Player;

        world.DrainPendingSounds();

        LandAt(world, player.FallDamageImpactSpeed * 1.2);

        Assert.AreEqual(GameDefaults.PlayerFullHealth - GameDefaults.DefaultFallDamage, player.Health, 1e-9);
        Assert.Contains(player.Sprite.PoseSounds[InteractionHandler.HardLandingSoundKey], world.DrainPendingSounds());
    }

    [TestMethod]
    public async Task OrdinaryLanding_DoesNotHurt()
    {
        var world = await LoadGroundedPlayerAsync();
        var player = world.Player;

        world.DrainPendingSounds();

        LandAt(world, player.FallDamageImpactSpeed * 0.8);

        Assert.AreEqual(GameDefaults.PlayerFullHealth, player.Health);
        Assert.DoesNotContain(player.Sprite.PoseSounds[InteractionHandler.HardLandingSoundKey], world.DrainPendingSounds());
    }

    [TestMethod]
    public async Task RestingAfterHardLanding_HurtsOnlyOnce()
    {
        var world = await LoadGroundedPlayerAsync();
        var player = world.Player;
        LandAt(world, player.FallDamageImpactSpeed * 1.2);

        var input = new InputState();
        var physics = new PhysicsSystem();
        var collision = new CollisionSystem();
        physics.Reset(input);
        for (var i = 0; i < 60; i++)
        {
            physics.Step(world, input, PhysicsConstants.FixedPhysicsStepSeconds);
            collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        }

        Assert.AreEqual(GameDefaults.PlayerFullHealth - GameDefaults.DefaultFallDamage, player.Health, 1e-9);
    }

    [TestMethod]
    public async Task HardLandingWithLastHealth_CostsALife()
    {
        var world = await LoadGroundedPlayerAsync();
        var player = world.Player;
        player.Health = GameDefaults.DefaultFallDamage / 2;
        var lives = player.Lives;

        LandAt(world, player.FallDamageImpactSpeed * 1.2);

        Assert.AreEqual(0.0, player.Health);
        Assert.AreEqual(lives - 1, player.Lives);
        Assert.AreEqual(PendingTransition.Death, world.PendingTransition);
    }

    [TestMethod]
    public async Task FatalHardLanding_ShowsOnlyDiedEffect()
    {
        var world = await LoadGroundedPlayerAsync();
        var player = world.Player;
        player.HurtEffectClipName = "hurt";
        player.Health = GameDefaults.DefaultFallDamage / 2;

        LandAt(world, player.FallDamageImpactSpeed * 1.2);

        var effectClips = world.Objects.OfType<EffectInstance2D>().Select(effect => effect.Clip.Name).ToList();
        CollectionAssert.AreEqual(new[] { "died" }, effectClips);
    }

    [TestMethod]
    public async Task HardLandingDuringHazardRecovery_StillHurts()
    {
        var world = await LoadGroundedPlayerAsync();
        var player = world.Player;
        player.RecoveryRemainingSeconds = GameDefaults.HazardRecoverySeconds;

        LandAt(world, player.FallDamageImpactSpeed * 1.2);

        Assert.AreEqual(GameDefaults.PlayerFullHealth - GameDefaults.DefaultFallDamage, player.Health, 1e-9);
    }
}

using ASCII_Hero.Client.Game.Browser;
using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.Physics;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.Physics;

/// <summary>
/// Crawling only exists on the ground (or as Clamber while hanging): mid-air the stance is
/// always upright, and Down in the air never starts a crawl.
/// </summary>
[TestClass]
public sealed class StanceTests
{
    private const int MaxSettleSteps = 600;

    private sealed record Stepper(PhysicsSystem Physics, CollisionSystem Collision)
    {
        public Stepper() : this(new PhysicsSystem(), new CollisionSystem()) { }
    }

    private static async Task<(World2D World, Player2D Player, Stepper Physics, InputState Input)> LoadAirborneAsync()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "TestMovement");
        var player = world.Player;
        Assert.IsFalse(player.IsGrounded, "A freshly loaded player has no contacts yet.");
        return (world, player, new Stepper(), new InputState());
    }

    private static void Step(World2D world, Stepper physics, InputState input)
    {
        physics.Physics.Step(world, input, PhysicsConstants.FixedPhysicsStepSeconds);
        physics.Collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        world.ApplyPendingRemovals();
    }

    private static void SettleOnGround(World2D world, Player2D player, Stepper physics, InputState input)
    {
        for (var i = 0; i < MaxSettleSteps && !player.IsGrounded; i++)
        {
            Step(world, physics, input);
        }

        Assert.IsTrue(player.IsGrounded, "Player never landed.");
    }

    [TestMethod]
    public async Task Airborne_CrawlStanceReturnsToWalk()
    {
        var (world, player, physics, input) = await LoadAirborneAsync();
        player.Stance = "Crawl";

        Step(world, physics, input);

        Assert.AreEqual("Walk", player.Stance);
    }

    [TestMethod]
    public async Task Airborne_DownPressDoesNotStartCrawl()
    {
        var (world, player, physics, input) = await LoadAirborneAsync();
        input.KeyDown("ArrowDown");

        Step(world, physics, input);

        Assert.AreEqual("Walk", player.Stance);
    }

    [TestMethod]
    public async Task Hanging_KeepsClamberStanceWhileAirborne()
    {
        var (world, player, physics, input) = await LoadAirborneAsync();
        player.IsTouchingHangable = true;
        player.IsHanging = true;
        player.Stance = "Crawl";

        Step(world, physics, input);

        Assert.IsTrue(player.IsHanging);
        Assert.AreEqual("Crawl", player.Stance);
    }

    [TestMethod]
    public async Task Grounded_DownPressStartsCrawl()
    {
        var (world, player, physics, input) = await LoadAirborneAsync();
        SettleOnGround(world, player, physics, input);
        input.KeyDown("ArrowDown");

        Step(world, physics, input);

        Assert.AreEqual("Crawl", player.Stance);
    }
}

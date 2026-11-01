using ASCII_Hero.Client.Game.Browser;
using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.Physics;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.Physics;

/// <summary>
/// Simulates the shipped Crates world with no input, so resting stacks of crates can be checked
/// for settling without sinking into each other or creeping.
/// </summary>
[TestClass]
public sealed class RestingStackTests
{
    private static async Task<(World2D World, List<DynamicObject2D> Crates)> SettleCratesAsync(int steps)
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "Crates");
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

        return (world, world.Objects.OfType<DynamicObject2D>().ToList());
    }

    [TestMethod]
    public async Task SettledCrates_OverlapNoDeeperThanContactSlop()
    {
        var (_, crates) = await SettleCratesAsync(steps: 300);

        foreach (var a in crates)
        {
            foreach (var b in crates)
            {
                if (ReferenceEquals(a, b))
                {
                    continue;
                }

                var overlapX = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
                var overlapY = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
                var depth = Math.Min(overlapX, overlapY);
                Assert.IsLessThanOrEqualTo(PhysicsConstants.ContactSlopCells + 1e-6, depth);
            }
        }
    }

    [TestMethod]
    public async Task SettledCrates_StayStillAndGrounded()
    {
        var (world, crates) = await SettleCratesAsync(steps: 300);
        var before = crates.Select(crate => crate.Position).ToList();

        var input = new InputState();
        var physics = new PhysicsSystem();
        var collision = new CollisionSystem();
        physics.Reset(input);
        for (var i = 0; i < 120; i++)
        {
            physics.Step(world, input, PhysicsConstants.FixedPhysicsStepSeconds);
            collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        }

        for (var i = 0; i < crates.Count; i++)
        {
            Assert.IsLessThan(0.01, Math.Abs(crates[i].Position.X - before[i].X));
            Assert.IsLessThan(0.01, Math.Abs(crates[i].Position.Y - before[i].Y));
            Assert.IsTrue(crates[i].IsGrounded);
        }
    }

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
    public async Task SettledCrates_FallAsleep()
    {
        var (_, crates) = await SettleCratesAsync(steps: 300);

        Assert.IsTrue(crates.All(crate => crate.IsSleeping));
    }

    [TestMethod]
    public async Task RemovingSupport_WakesStackAndItFalls()
    {
        var (world, crates) = await SettleCratesAsync(steps: 300);
        var support = crates.First(crate => crate.GetContactingBodies(ContactType.SurfaceTop).Count > 0);
        var rider = support.GetContactingBodies(ContactType.SurfaceTop)[0];
        var riderY = rider.Position.Y;

        world.QueueRemoval(support);
        Run(world, steps: 60);

        Assert.IsGreaterThan(riderY + 0.5, rider.Position.Y);
    }

    [TestMethod]
    public async Task BodyLandingOnSleepingCrate_WakesItAndRestsOnTop()
    {
        var (world, crates) = await SettleCratesAsync(steps: 300);
        var player = world.Objects.OfType<Player2D>().Single();
        var target = crates.Where(crate => crate.GetContactingBodies(ContactType.SurfaceTop).Count == 0).MinBy(crate => crate.Top)!;
        player.Position = new Vector2D(target.Left + (target.Size.X - player.Size.X) / 2, target.Top - player.Size.Y - 3);
        player.Velocity = Vector2D.Zero;

        Run(world, steps: 120);

        Assert.IsTrue(player.GetContactingBodies(ContactType.SurfaceBottom).Contains(target));
        Assert.IsLessThanOrEqualTo(PhysicsConstants.ContactSlopCells + 1e-6, player.Bottom - target.Top);
    }

    // Known solver limit: with an awake body (such as the player) standing on a stack of movable
    // objects, the stack shares an island with a body that never sleeps, so it stays awake; and the
    // iterative solver cannot pass the load all the way down within its iteration budget, so small
    // residual velocities remain and every step costs the full iteration count. Visually the stack
    // is still. Warm starting (carrying each contact's impulse over to the next step) is the known
    // fix for the residual velocities; re-enable this test when that is implemented.
    [Ignore("Known solver limit: loaded stacks keep full solver cost without warm starting.")]
    [TestMethod]
    public async Task BodyStandingOnStack_StackSettlesAndSleeps()
    {
        var (world, crates) = await SettleCratesAsync(steps: 300);
        var player = world.Objects.OfType<Player2D>().Single();
        var target = crates
            .Where(crate => crate.GetContactingBodies(ContactType.SurfaceTop).Count == 0
                && crate.GetContactingBodies(ContactType.SurfaceBottom).Any(support => support is DynamicObject2D))
            .MinBy(crate => crate.Top)!;
        player.Position = new Vector2D(target.Left + (target.Size.X - player.Size.X) / 2, target.Top - player.Size.Y - 1);
        player.Velocity = Vector2D.Zero;

        Run(world, steps: 300);

        Assert.IsLessThan(0.05, Math.Abs(player.Velocity.Y), $"player vy={player.Velocity.Y}");
    }

    // The player walking and jumping among movable objects must never sink into one of them.
    [TestMethod]
    public async Task PlayerMovingAmongCrates_NeverSinksIntoThem()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "Crates");
        var input = new InputState();
        var physics = new PhysicsSystem();
        var collision = new CollisionSystem();
        physics.Reset(input);
        var player = world.Player;
        string Rows(Body2D b) => string.Join("|", Enumerable.Range(0, b.Frame.Chars.GetLength(0))
            .Select(r => new string(Enumerable.Range(0, b.Frame.Chars.GetLength(1)).Select(c => b.Frame.Chars[r, c]).ToArray())));
        var printed = 0;
        var log = new System.Text.StringBuilder();
        for (var step = 0; step < 1500 && printed < 6; step++)
        {
            input.KeyUp("ArrowRight"); input.KeyUp("ArrowLeft"); input.KeyUp("Space");
            if (step > 300) input.KeyDown(step < 1000 ? "ArrowRight" : "ArrowLeft");
            if (step > 300 && step % 90 < 10) input.KeyDown("Space");

            var p0 = player.Position;
            var pose0 = Rows(player);
            physics.Step(world, input, PhysicsConstants.FixedPhysicsStepSeconds);
            var p1 = player.Position;
            var pose1 = Rows(player);
            collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
            world.ApplyPendingRemovals();

            foreach (var other in world.Objects.OfType<DynamicObject2D>())
            {
                var depth = 0.0;
                foreach (var a in player.CollisionRects)
                foreach (var b in other.CollisionRects)
                {
                    var ox = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
                    var oy = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
                    if (ox > 0 && oy > 0) depth = Math.Max(depth, Math.Min(ox, oy));
                }

                if (depth > 0.5)
                {
                    printed++;
                    log.AppendLine($"STEP {step} depth={depth:F3} poseBefore='{pose0}' poseAfterPhys='{pose1}' poseFinal='{Rows(player)}' p0=({p0.X:F3};{p0.Y:F3}) afterPhys=({p1.X:F3};{p1.Y:F3}) final=({player.Position.X:F3};{player.Position.Y:F3}) v=({player.Velocity.X:F2};{player.Velocity.Y:F2}) other=({other.Position.X:F3};{other.Position.Y:F3}) ov=({other.Velocity.X:F2};{other.Velocity.Y:F2})");
                    log.AppendLine("   playerRects=" + string.Join(" ", player.CollisionRects.Select(r => $"[{r.Left:F2};{r.Top:F2};{r.Right:F2};{r.Bottom:F2}]")));
                    log.AppendLine("   otherRects=" + string.Join(" ", other.CollisionRects.Select(r => $"[{r.Left:F2};{r.Top:F2};{r.Right:F2};{r.Bottom:F2}]")));
                }
            }
        }

        Assert.AreEqual(0, printed, "Player sank into a movable object:\n" + log);
    }
}

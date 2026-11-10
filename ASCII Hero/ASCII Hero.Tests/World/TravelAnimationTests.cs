using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Browser;
using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.Physics;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.World;

/// <summary>
/// Travel-driven animation (<c>CellsPerFrame</c>): frames keep pace with distance travelled
/// relative to what the body moves against, instead of with time.
/// </summary>
[TestClass]
public sealed class TravelAnimationTests
{
    private const string Folder = "Assets/Global/Sprites/Thing";

    private sealed class MemoryAssetFileProvider(Dictionary<string, string> files) : IAssetFileProvider
    {
        public Task<string?> TryReadTextAsync(string relativePath) =>
            Task.FromResult(files.TryGetValue(relativePath, out var content) ? content : null);
    }

    private static Task<SpriteAsset> LoadAsync(string settings) =>
        new SpriteLoader(new MemoryAssetFileProvider(new Dictionary<string, string>
        {
            [$"{Folder}/Thing_settings.ini"] = settings,
            [$"{Folder}/Thing_run_right_characters.txt"] = "a\n//end\nb\n//end\nc",
            [$"{Folder}/Thing_run_up_characters.txt"] = "a\n//end\nb\n//end\nc",
            [$"{Folder}/Thing_spin_characters.txt"] = "a\n//end\nb\n//end\nc",
        })).LoadAsync("Thing", ["run_right", "run_up", "spin"], worldName: null);

    private static int FrameIndexOf(Body2D body) => body.Clip.Frames.ToList().IndexOf(body.Frame);

    [TestMethod]
    public async Task Loader_ResolvesCellsPerFramePerClip_AndFacingFromSuffix()
    {
        var sprite = await LoadAsync("[Animation]\nCellsPerFrame = 2\n\n[Animation.run_up]\nCellsPerFrame = 0.5\n\n[Animation.spin]\nCellsPerFrame = -1");

        Assert.AreEqual(2.0, sprite.GetClip("run_right").CellsPerFrame);
        Assert.AreEqual(0.5, sprite.GetClip("run_up").CellsPerFrame);
        Assert.IsNull(sprite.GetClip("spin").CellsPerFrame, "A non-positive value means the clip is not travel-driven.");
        Assert.AreEqual(Facing.Right, sprite.GetClip("run_right").Facing);
        Assert.AreEqual(Facing.Up, sprite.GetClip("run_up").Facing);
        Assert.AreEqual(Facing.Idle, sprite.GetClip("spin").Facing);
    }

    [TestMethod]
    public async Task RunningSurface_StepsOncePerCellTheSurfaceRuns()
    {
        var sprite = await LoadAsync("[Animation]\nCellsPerFrame = 1\nFrameDurationSeconds = 0.05");
        var belt = new StaticObject2D();
        belt.Spawn(sprite, "run_right", frameIndex: 0, position: new Vector2D(0, 0));
        belt.ConveyorVelocity = new Vector2D(3, 0);

        belt.AdvanceAnimation(0.3);
        Assert.AreEqual(0, FrameIndexOf(belt), "0.9 cells run: not yet one step.");
        belt.AdvanceAnimation(0.05);
        Assert.AreEqual(1, FrameIndexOf(belt), "1.05 cells run: one step.");
        belt.AdvanceAnimation(1.0 / 3.0);
        Assert.AreEqual(2, FrameIndexOf(belt));
    }

    [TestMethod]
    public async Task TravelClip_WithoutTravelSource_FallsBackToFrameDuration()
    {
        var sprite = await LoadAsync("[Animation]\nCellsPerFrame = 1\nFrameDurationSeconds = 0.25");
        var still = new StaticObject2D();
        still.Spawn(sprite, "run_right", frameIndex: 0, position: new Vector2D(0, 0));

        Assert.IsNull(still.GetAnimationTravelVelocity());
        still.AdvanceAnimation(0.3);
        Assert.AreEqual(1, FrameIndexOf(still));
    }

    [TestMethod]
    public async Task MovingBody_StepsWithItsOwnTravelAlongTheClipAxis_AndHoldsWhenStopped()
    {
        var sprite = await LoadAsync("[Animation]\nCellsPerFrame = 2\nFrameDurationSeconds = 0.01");
        var walker = new DynamicObject2D();
        walker.Spawn(sprite, "run_right", frameIndex: 0, position: new Vector2D(0, 0), velocity: new Vector2D(0, 0), gravityAffected: false);

        walker.AdvanceAnimation(10.0);
        Assert.AreEqual(0, FrameIndexOf(walker), "A stopped (e.g. blocked) body holds its frame.");

        walker.Velocity = new Vector2D(0, 50);
        walker.AdvanceAnimation(1.0);
        Assert.AreEqual(0, FrameIndexOf(walker), "Vertical travel does not drive a _right clip.");

        walker.Velocity = new Vector2D(-4, 0);
        walker.AdvanceAnimation(0.5);
        Assert.AreEqual(1, FrameIndexOf(walker), "2 cells travelled (either direction): one step.");
    }

    [TestMethod]
    public async Task MovingBody_InFlowingMedium_MeasuresTravelRelativeToTheFlow()
    {
        var sprite = await LoadAsync("[Animation]\nCellsPerFrame = 1");
        var floater = new DynamicObject2D();
        floater.Spawn(sprite, "run_right", frameIndex: 0, position: new Vector2D(0, 0), velocity: new Vector2D(12, 0), gravityAffected: false);
        floater.CurrentFlow = new Vector2D(12, 0);

        floater.AdvanceAnimation(1.0);

        Assert.AreEqual(0, FrameIndexOf(floater), "Drifting along with the current is not travel of its own.");
    }

    [TestMethod]
    public async Task PlayerHangingStillOnRunningRope_HasNoTravelOfItsOwn()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "Ropes");
        var rope = world.Objects.OfType<StaticObject2D>().First(o => o.Sprite.Name == "Rope" && o.ConveyorVelocity.X > 0);
        var player = world.Player;
        var physics = new PhysicsSystem();
        var collision = new CollisionSystem();
        var input = new InputState();
        physics.Reset(input);

        player.Position = new Vector2D(rope.Position.X + 2, rope.Position.Y);
        player.Velocity = new Vector2D(0, 0);
        input.KeyDown("ArrowUp");
        for (var i = 0; i < 10; i++)
        {
            physics.Step(world, input, PhysicsConstants.FixedPhysicsStepSeconds);
            collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        }
        input.KeyUp("ArrowUp");
        for (var i = 0; i < 30; i++)
        {
            physics.Step(world, input, PhysicsConstants.FixedPhysicsStepSeconds);
            collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);
        }

        Assert.IsTrue(player.IsHanging, "The player should be hanging from the running rope.");
        Assert.AreEqual(rope.ConveyorVelocity.X, player.Velocity.X, 0.1, "The rope carries the player along.");
        Assert.AreEqual(0.0, player.GetAnimationTravelVelocity()!.Value.X, 0.1, "Being carried is not the player's own travel.");
    }

    [TestMethod]
    public async Task ShippedMovingClips_AreTravelDriven()
    {
        var loader = new SpriteLoader(new DiskAssetFileProvider());
        var expectations = new (string Asset, string[] Clips, double CellsPerFrame)[]
        {
            ("Player", ["walk_left", "walk_right", "crawl_left", "crawl_right", "climb_up", "climb_down", "hang_left", "hang_right", "clamber_left", "clamber_right"], 2.4),
            ("Player", ["swim_left", "swim_right", "swim_up", "swim_down"], 5.4),
            ("Rope", ["horizontal_left", "horizontal_right", "vertical_up", "vertical_down"], 1.0),
            ("Conveyor", ["horizontal_left", "horizontal_right", "vertical_up", "vertical_down"], 1.0),
            ("WaterSurface", ["horizontal_left", "horizontal_right"], 3.0),
            ("WaterSurface", ["vertical_down"], 1.0),
        };

        foreach (var (asset, clips, cellsPerFrame) in expectations)
        {
            var sprite = await loader.LoadAsync(asset, clips, worldName: null);
            foreach (var clip in clips)
            {
                Assert.AreEqual(cellsPerFrame, sprite.GetClip(clip).CellsPerFrame, $"{asset}.{clip}");
                if (asset != "Player")
                {
                    Assert.HasCount(3, sprite.GetClip(clip).Frames, $"{asset}.{clip} should be a 3-cell unit shifting one cell per frame.");
                }
            }
        }
    }
}

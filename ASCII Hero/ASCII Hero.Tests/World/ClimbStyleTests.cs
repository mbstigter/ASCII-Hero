using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Physics;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.World;

/// <summary>ClimbStyle is a visual-only choice of climbing pose: parsing, validation and pose resolution.</summary>
[TestClass]
public sealed class ClimbStyleTests
{
    private const string ShimmyPoseLine = "Shimmy = climb_shimmy_idle, climb_shimmy_up, climb_shimmy_down";

    /// <summary>Serves shipped assets, optionally adding lines to the Crates light crate and/or dropping the Player's Shimmy pose.</summary>
    private sealed class OverrideProvider(string crateLines = "", bool dropShimmyPose = false) : IAssetFileProvider
    {
        private readonly DiskAssetFileProvider _disk = new();

        public async Task<string?> TryReadTextAsync(string relativePath)
        {
            var text = await _disk.TryReadTextAsync(relativePath);
            if (text is null)
            {
                return null;
            }

            if (relativePath.EndsWith("Crates_objects.ini", StringComparison.Ordinal) && crateLines.Length > 0)
            {
                return text.Replace("[Crate]", $"[Crate]\n{crateLines}", StringComparison.Ordinal);
            }

            if (dropShimmyPose && relativePath.EndsWith("Player_settings.ini", StringComparison.Ordinal))
            {
                return text.Replace(ShimmyPoseLine, string.Empty, StringComparison.Ordinal);
            }

            return text;
        }
    }

    [TestMethod]
    [DataRow("ClimbStyle = Shimmy")]
    [DataRow("Climbable = true\nClimbStyle = Rope")]
    public async Task InvalidClimbStyle_IsRejected(string extraLines)
    {
        await Assert.ThrowsExactlyAsync<FormatException>(() => World2D.LoadAsync(new OverrideProvider(extraLines), "Crates"));
    }

    [TestMethod]
    [DataRow("shimmy", ClimbStyle.Shimmy)]
    [DataRow("Climb", ClimbStyle.Climb)]
    public async Task ClimbStyle_IsParsed(string value, ClimbStyle expected)
    {
        var world = await World2D.LoadAsync(new OverrideProvider($"Climbable = true\nClimbStyle = {value}"), "Crates");

        Assert.IsTrue(world.Objects.Any(body => body.IsClimbable && body.ClimbStyle == expected));
    }

    [TestMethod]
    public async Task AncientWorld_PipeAndRopeAreShimmy_LadderIsClimb()
    {
        var world = await World2D.LoadAsync(new DiskAssetFileProvider(), "AncientWorld");

        var climbables = world.Objects.Where(body => body.IsClimbable).ToList();
        Assert.IsTrue(climbables.Any(body => body.ClimbStyle == ClimbStyle.Shimmy));
        Assert.IsTrue(climbables.Any(body => body.ClimbStyle == ClimbStyle.Climb));
    }

    private static void Grab(Player2D player, ClimbStyle style)
    {
        player.IsClimbing = true;
        player.AddContact(ContactType.Climbable, new StaticObject2D { IsClimbable = true, ClimbStyle = style });
        player.UpdatePose();
    }

    [TestMethod]
    public async Task ShimmySurface_ShowsShimmyPose()
    {
        var player = (await World2D.LoadAsync(new DiskAssetFileProvider(), "PhysicsTest1")).Player;

        Grab(player, ClimbStyle.Shimmy);

        Assert.AreEqual("Shimmy", player.ResolvedPose);
    }

    [TestMethod]
    public async Task ClimbSurface_ShowsClimbPose()
    {
        var player = (await World2D.LoadAsync(new DiskAssetFileProvider(), "PhysicsTest1")).Player;

        Grab(player, ClimbStyle.Climb);

        Assert.AreEqual("Climb", player.ResolvedPose);
    }

    [TestMethod]
    public async Task StyleOfFirstGrabbedSurface_IsKeptUntilClimbEnds()
    {
        var player = (await World2D.LoadAsync(new DiskAssetFileProvider(), "PhysicsTest1")).Player;
        Grab(player, ClimbStyle.Shimmy);

        player.RemoveContact(ContactType.Climbable);
        player.AddContact(ContactType.Climbable, new StaticObject2D { IsClimbable = true, ClimbStyle = ClimbStyle.Climb });
        player.UpdatePose();
        Assert.AreEqual("Shimmy", player.ResolvedPose);

        player.IsClimbing = false;
        player.UpdatePose();
        Grab(player, ClimbStyle.Climb);
        Assert.AreEqual("Climb", player.ResolvedPose);
    }

    [TestMethod]
    public async Task MissingShimmyPose_FallsBackToClimb()
    {
        var player = (await World2D.LoadAsync(new OverrideProvider(dropShimmyPose: true), "PhysicsTest1")).Player;

        Grab(player, ClimbStyle.Shimmy);

        Assert.AreEqual("Climb", player.ResolvedPose);
    }
}

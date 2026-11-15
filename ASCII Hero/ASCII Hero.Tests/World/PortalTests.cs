using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.Physics;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.World;

/// <summary>Portals and named player entries: parsing, validation, arrival and the edge-triggered firing.</summary>
[TestClass]
public sealed class PortalTests
{
    /// <summary>Serves shipped assets, appending lines to the end of the main world's object definitions.</summary>
    private sealed class AppendProvider(string extraIni) : IAssetFileProvider
    {
        private readonly DiskAssetFileProvider _disk = new();

        public async Task<string?> TryReadTextAsync(string relativePath)
        {
            var text = await _disk.TryReadTextAsync(relativePath);
            return text is not null && relativePath.EndsWith("AncientWorld_objects.ini", StringComparison.Ordinal)
                ? text + "\n" + extraIni + "\n"
                : text;
        }
    }

    private static Task<World2D> LoadMain() => World2D.LoadAsync(new DiskAssetFileProvider(), "AncientWorld");

    [TestMethod]
    public async Task AncientWorld_MainAndSubWorld_DeclareMatchingPortalsAndEntries()
    {
        var main = await LoadMain();
        var sub = await World2D.LoadAsync(new DiskAssetFileProvider(), "AncientWorld", subWorld: 1);

        var toSub = main.Objects.OfType<Portal2D>().Single();
        var toMain = sub.Objects.OfType<Portal2D>().Single();

        Assert.AreEqual(1, toSub.TargetWorld);
        Assert.AreEqual(0, toMain.TargetWorld);
        Assert.IsTrue(sub.Entries.ContainsKey(toSub.TargetEntry!));
        Assert.IsTrue(main.Entries.ContainsKey(toMain.TargetEntry!));
        Assert.AreEqual(1, sub.SubWorld);
        Assert.AreEqual(0, main.SubWorld);
    }

    [TestMethod]
    public async Task SubWorld_MissingFiles_Throws()
    {
        await Assert.ThrowsExactlyAsync<FileNotFoundException>(() =>
            World2D.LoadAsync(new DiskAssetFileProvider(), "AncientWorld", subWorld: 99));
    }

    [TestMethod]
    public async Task NegativeSubWorld_Throws()
    {
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            World2D.LoadAsync(new DiskAssetFileProvider(), "AncientWorld", subWorld: -1));
    }

    [TestMethod]
    public async Task EnterAt_NamedEntry_MovesPlayerAndSetsRespawn()
    {
        var sub = await World2D.LoadAsync(new DiskAssetFileProvider(), "AncientWorld", subWorld: 1);
        var entry = sub.Entries["FromMain"];

        sub.EnterAt("FromMain");

        Assert.AreEqual(entry, sub.Player.Position);
        Assert.AreEqual(entry, sub.PlayerSpawnPosition);
    }

    [TestMethod]
    public async Task EnterAt_NoEntry_UsesTheUnnamedPlayerPlacement()
    {
        var sub = await World2D.LoadAsync(new DiskAssetFileProvider(), "AncientWorld", subWorld: 1);
        var start = sub.Player.Position;
        sub.EnterAt("FromMain");

        sub.EnterAt(null);

        Assert.AreEqual(start, sub.Player.Position);
    }

    [TestMethod]
    public async Task EnterAt_UnknownEntry_ListsAvailableNames()
    {
        var sub = await World2D.LoadAsync(new DiskAssetFileProvider(), "AncientWorld", subWorld: 1);

        var error = Assert.ThrowsExactly<FormatException>(() => sub.EnterAt("Nowhere"));

        Assert.Contains("FromMain", error.Message);
    }

    [TestMethod]
    public async Task CarryOverFrom_CopiesProgress()
    {
        var main = await LoadMain();
        var sub = await World2D.LoadAsync(new DiskAssetFileProvider(), "AncientWorld", subWorld: 1);
        main.Player.Score = 120;
        main.Player.Lives = 7;

        sub.Player.CarryOverFrom(main.Player);

        Assert.AreEqual(120, sub.Player.Score);
        Assert.AreEqual(7, sub.Player.Lives);
    }

    [TestMethod]
    [DataRow("[Bad]\nAsset = Hatch\nClip = ancient_closed\nKind = Portal")]
    [DataRow("[Bad]\nAsset = Hatch\nClip = ancient_closed\nKind = Portal\nTargetWorld = -1")]
    [DataRow("[Bad]\nAsset = Hatch\nClip = ancient_closed\nKind = StaticObject\nTargetWorld = 1")]
    [DataRow("[Bad]\nAsset = Hatch\nClip = ancient_closed\nKind = StaticObject\nEntry = X")]
    [DataRow("[PlayerFromSubWorld2]\nAsset = Player\nClip = walk_idle\nKind = Player\nEntry = FromSubWorld")]
    public async Task InvalidPortalOrEntry_IsRejectedWhenPlaced(string extraIni)
    {
        var code = extraIni.StartsWith("[Bad]", StringComparison.Ordinal) ? "Bad" : "PlayerFromSubWorld2";
        var provider = new AppendProvider($"{extraIni}\n[ObjectCodes]\n9 = {code}");

        // The extra section must also be placed on the grid to be validated.
        var files = new PlacingProvider(provider, '9');
        await Assert.ThrowsExactlyAsync<FormatException>(() => World2D.LoadAsync(files, "AncientWorld"));
    }

    /// <summary>Puts <paramref name="code"/> in the grid's top-left cell so a new definition gets placed.</summary>
    private sealed class PlacingProvider(IAssetFileProvider inner, char code) : IAssetFileProvider
    {
        public async Task<string?> TryReadTextAsync(string relativePath)
        {
            var text = await inner.TryReadTextAsync(relativePath);
            if (text is null || !relativePath.EndsWith("AncientWorld_objects.txt", StringComparison.Ordinal))
            {
                return text;
            }

            var firstLineEnd = text.IndexOf('\n');
            var line = text[..firstLineEnd].TrimEnd('\r');
            var replaced = code + line[1..];
            return replaced + text[firstLineEnd..];
        }
    }

    [TestMethod]
    public async Task Portal_FiresOnceOnEntry_NotWhileStayingAndRearmsAfterLeaving()
    {
        var world = await LoadMain();
        var portal = world.Objects.OfType<Portal2D>().Single();
        var away = world.Player.Position;
        var collision = new CollisionSystem();
        var step = PhysicsConstants.FixedPhysicsStepSeconds;

        world.Player.Position = portal.Position;
        collision.Resolve(world, step);
        Assert.AreEqual(PendingTransition.EnterWorld, world.PendingTransition);
        Assert.AreSame(portal, world.PendingPortal);

        world.PendingTransition = PendingTransition.None;
        world.PendingPortal = null;
        collision.Resolve(world, step);
        Assert.AreEqual(PendingTransition.None, world.PendingTransition);

        world.Player.Position = away;
        collision.Resolve(world, step);
        world.Player.Position = portal.Position;
        collision.Resolve(world, step);
        Assert.AreEqual(PendingTransition.EnterWorld, world.PendingTransition);
    }

    [TestMethod]
    public async Task Portal_WithPlayerPortalEffect_SpawnsEffectOnEntry()
    {
        var world = await LoadMain();
        var portal = world.Objects.OfType<Portal2D>().Single();
        Assert.IsNotNull(world.Player.PortalEnteredEffectClipName);

        world.Player.Position = portal.Position;
        new CollisionSystem().Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);

        Assert.IsTrue(world.Objects.OfType<EffectInstance2D>().Any());
    }

    [TestMethod]
    public async Task EnterAtWithArrivalEffect_FreezesPlayAndSpawnsEffect()
    {
        var sub = await World2D.LoadAsync(new DiskAssetFileProvider(), "AncientWorld", subWorld: 1);
        Assert.IsNotNull(sub.Player.PortalArrivedEffectClipName);
        sub.EnterAt("FromMain");

        InteractionHandler.OnPortalArrived(sub, sub.Player);

        Assert.AreEqual(PendingTransition.Arrive, sub.PendingTransition);
        Assert.IsTrue(sub.Objects.OfType<EffectInstance2D>().Any());
    }

    [TestMethod]
    public async Task ArmPortals_PreventsFiringForAPlayerArrivingOnAPortal()
    {
        var world = await LoadMain();
        var portal = world.Objects.OfType<Portal2D>().Single();
        var collision = new CollisionSystem();
        world.Player.Position = portal.Position;

        collision.ArmPortals(world);
        collision.Resolve(world, PhysicsConstants.FixedPhysicsStepSeconds);

        Assert.AreEqual(PendingTransition.None, world.PendingTransition);
    }
}

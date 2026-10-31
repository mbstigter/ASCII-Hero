using ASCII_Hero.Client.Game.Assets;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.Assets;

[TestClass]
public sealed class SpritePoseContractTests
{
    private const string Folder = "Assets/Global/Sprites/Thing";

    private sealed class MemoryAssetFileProvider(Dictionary<string, string> files) : IAssetFileProvider
    {
        public Task<string?> TryReadTextAsync(string relativePath) =>
            Task.FromResult(files.TryGetValue(relativePath, out var content) ? content : null);
    }

    private static Task<SpriteAsset> LoadAsync(string settings, params string[] clipNames)
    {
        var files = new Dictionary<string, string> { [$"{Folder}/Thing_settings.ini"] = settings };
        foreach (var clip in clipNames)
        {
            files[$"{Folder}/Thing_{clip}_characters.txt"] = "#";
        }

        return new SpriteLoader(new MemoryAssetFileProvider(files)).LoadAsync("Thing", ["idle"], worldName: null);
    }

    [TestMethod]
    public async Task NoPoses_LoadsAsSingleClipAsset()
    {
        var sprite = await LoadAsync("", "idle");

        Assert.IsNull(sprite.Poses);
        Assert.IsNull(sprite.DefaultPose);
    }

    [TestMethod]
    public async Task MissingDefault_Throws()
    {
        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => LoadAsync("[Poses]\nMove = move_idle, move_left", "idle", "move_idle", "move_left"));
    }

    [TestMethod]
    public async Task UnknownDefault_Throws()
    {
        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => LoadAsync("[Poses]\nDefault = Fly\nMove = move_idle, move_left", "idle", "move_idle", "move_left"));
    }

    [TestMethod]
    public async Task ValidPoses_MissingFacingFallsBackToIdleClip()
    {
        var sprite = await LoadAsync("[Poses]\nDefault = Move\nMove = move_idle, move_left", "idle", "move_idle", "move_left");

        Assert.AreEqual("Move", sprite.DefaultPose);
        Assert.AreEqual("move_idle", sprite.Poses!["Move"].GetClipName(Facing.Right));
        Assert.AreEqual("move_left", sprite.Poses!["Move"].GetClipName(Facing.Left));
    }

    [TestMethod]
    public async Task SetPose_UnknownPose_Throws()
    {
        var sprite = await LoadAsync("[Poses]\nDefault = Move\nMove = move_idle", "idle", "move_idle");
        var body = new Player2D();

        Assert.ThrowsExactly<InvalidOperationException>(() => body.SetPose(sprite, "Fly", Facing.Idle));
    }

    [TestMethod]
    public async Task SetPose_NoPoseSystem_NoOps()
    {
        var sprite = await LoadAsync("", "idle");
        var body = new Player2D();

        body.SetPose(sprite, "Move", Facing.Idle);

        Assert.IsNull(body.Clip);
    }

    [TestMethod]
    public async Task PlayerSpawn_MissingRequiredPoses_Throws()
    {
        var sprite = await LoadAsync("[Poses]\nDefault = Walk\nWalk = walk_idle", "idle", "walk_idle");

        Assert.ThrowsExactly<InvalidDataException>(() => new Player2D().Spawn(sprite));
    }
}

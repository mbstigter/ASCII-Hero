namespace ASCII_Hero.Client.Game.World;

/// <summary>Holds all entities that make up the current game state.</summary>
public class World2D
{
    public Player2D Player { get; } = new();

    public List<StaticObject2D> Platforms { get; } = [];

    /// <summary>Gravity acceleration, in world cells per second squared.</summary>
    public double Gravity { get; } = 40;

    public World2D()
    {
        Player.Position = new Vector2D(5, 5);

        // Ground strip and a couple of floating platforms.
        Platforms.Add(new StaticObject2D(0, 15, 60, 2));
        Platforms.Add(new StaticObject2D(10, 11, 6, 1));
        Platforms.Add(new StaticObject2D(20, 8, 6, 1));
        Platforms.Add(new StaticObject2D(30, 12, 8, 1));
    }
}

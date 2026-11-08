using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.Physics;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.World;

/// <summary>
/// A patrolling dynamic body that is blocked short of its patrol bound keeps pushing for
/// <see cref="DynamicObject2D.PatrolBlockedTurnTime"/>, then turns around - unless it makes progress.
/// </summary>
[TestClass]
public sealed class PatrolBlockedTurnTests
{
    private const double Gravity = 60.0;
    private const double Step = PhysicsConstants.FixedPhysicsStepSeconds;

    private static DynamicObject2D CreateWalkerHeadingRight(double blockedTurnTime = GameDefaults.DefaultPatrolBlockedTurnTime)
    {
        var walker = new DynamicObject2D { Position = new Vector2D(10, 5) };
        walker.SetPatrol(
            patrolMinX: 0, patrolMaxX: 40, cruiseSpeedX: GameDefaults.PatrolCruiseSpeed,
            patrolMinY: null, patrolMaxY: null, cruiseSpeedY: GameDefaults.PatrolCruiseSpeed,
            initialDirectionRight: true,
            blockedTurnTime: blockedTurnTime);
        return walker;
    }

    private static void RunFor(DynamicObject2D body, double seconds)
    {
        var steps = (int)Math.Round(seconds / Step);
        for (var i = 0; i < steps; i++)
        {
            body.UpdatePatrolDirection(Gravity, Step);
        }
    }

    [TestMethod]
    public void StalledAgainstObstacle_TurnsAroundAfterBlockedTurnTime()
    {
        var walker = CreateWalkerHeadingRight();
        walker.AddContact(ContactType.SurfaceRight, new StaticObject2D());

        RunFor(walker, GameDefaults.DefaultPatrolBlockedTurnTime - 0.1);
        Assert.AreEqual(1.0, walker.MoveIntentX, "should keep pushing before the time is up");

        RunFor(walker, 0.2);
        Assert.AreEqual(-1.0, walker.MoveIntentX, "should have turned around");
    }

    [TestMethod]
    public void PushingObstacleAlong_KeepsHeading()
    {
        var walker = CreateWalkerHeadingRight();
        walker.AddContact(ContactType.SurfaceRight, new DynamicObject2D());
        walker.Velocity = new Vector2D(GameDefaults.PatrolCruiseSpeed * 0.5, 0);

        RunFor(walker, GameDefaults.DefaultPatrolBlockedTurnTime * 3);

        Assert.AreEqual(1.0, walker.MoveIntentX);
    }

    [TestMethod]
    public void StalledWithoutContact_KeepsHeading()
    {
        var walker = CreateWalkerHeadingRight();

        RunFor(walker, GameDefaults.DefaultPatrolBlockedTurnTime * 3);

        Assert.AreEqual(1.0, walker.MoveIntentX);
    }

    [TestMethod]
    public void ContactOnlyBehind_KeepsHeading()
    {
        var walker = CreateWalkerHeadingRight();
        walker.AddContact(ContactType.SurfaceLeft, new StaticObject2D());

        RunFor(walker, GameDefaults.DefaultPatrolBlockedTurnTime * 3);

        Assert.AreEqual(1.0, walker.MoveIntentX);
    }

    [TestMethod]
    public void ProgressInBetween_RestartsTimer()
    {
        var walker = CreateWalkerHeadingRight();
        walker.AddContact(ContactType.SurfaceRight, new StaticObject2D());

        RunFor(walker, GameDefaults.DefaultPatrolBlockedTurnTime * 0.75);
        walker.Velocity = new Vector2D(GameDefaults.PatrolCruiseSpeed, 0);
        walker.UpdatePatrolDirection(Gravity, Step);
        walker.Velocity = new Vector2D(0, 0);
        RunFor(walker, GameDefaults.DefaultPatrolBlockedTurnTime * 0.75);

        Assert.AreEqual(1.0, walker.MoveIntentX);
    }

    [TestMethod]
    public void InfiniteBlockedTurnTime_NeverGivesUp()
    {
        var walker = CreateWalkerHeadingRight(blockedTurnTime: double.PositiveInfinity);
        walker.AddContact(ContactType.SurfaceRight, new StaticObject2D());

        RunFor(walker, 10.0);

        Assert.AreEqual(1.0, walker.MoveIntentX);
    }

    [TestMethod]
    public void FlierBlockedFromBelow_TurnsUp()
    {
        var flier = new DynamicObject2D { Position = new Vector2D(10, 10) };
        flier.SetPatrol(
            patrolMinX: null, patrolMaxX: null, cruiseSpeedX: GameDefaults.PatrolCruiseSpeed,
            patrolMinY: 0, patrolMaxY: 40, cruiseSpeedY: GameDefaults.PatrolCruiseSpeed,
            initialDirectionDown: true,
            maxForceY: 2.0);
        flier.AddContact(ContactType.SurfaceBottom, new StaticObject2D());

        flier.UpdatePatrolDirection(Gravity, Step);
        Assert.IsGreaterThan(0.0, flier.PatrolForce.Y, "should push down toward the floor at first");

        RunFor(flier, GameDefaults.DefaultPatrolBlockedTurnTime + 0.1);
        Assert.IsLessThan(0.0, flier.PatrolForce.Y, "should push up after giving up");
    }

    [TestMethod]
    public void WalkerOnFloorWithoutVerticalRange_IsNotAffectedByFloorContact()
    {
        var walker = CreateWalkerHeadingRight();
        walker.Velocity = new Vector2D(GameDefaults.PatrolCruiseSpeed, 0);
        walker.AddContact(ContactType.SurfaceBottom, new StaticObject2D());

        RunFor(walker, GameDefaults.DefaultPatrolBlockedTurnTime * 3);

        Assert.AreEqual(1.0, walker.MoveIntentX);
        Assert.AreEqual(0.0, walker.PatrolForce.Y);
    }
}

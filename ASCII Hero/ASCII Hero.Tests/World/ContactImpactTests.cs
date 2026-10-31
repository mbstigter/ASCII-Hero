using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.Physics;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.World;

[TestClass]
public sealed class ContactImpactTests
{
    [TestMethod]
    public void AddContact_KeepsHighestImpactSpeedPerBody()
    {
        var body = new StaticHazard2D();
        var other = new StaticHazard2D();

        body.AddContact(ContactType.SurfaceBottom, other, impactSpeed: 7.0);
        body.AddContact(ContactType.SurfaceLeft, other, impactSpeed: 2.0);

        Assert.IsTrue(body.HasContactWith(other));
        Assert.AreEqual(7.0, body.GetContactImpactSpeed(other));
    }

    [TestMethod]
    public void SnapshotContactsForNextFrame_ClearsContactsAndImpactSpeeds()
    {
        var body = new StaticHazard2D();
        var other = new StaticHazard2D();
        body.AddContact(ContactType.SurfaceBottom, other, impactSpeed: 7.0);

        body.SnapshotContactsForNextFrame(PhysicsConstants.FixedPhysicsStepSeconds);

        Assert.IsFalse(body.HasContactWith(other));
        Assert.AreEqual(0.0, body.GetContactImpactSpeed(other));
    }

    [TestMethod]
    public void Hazard_DefaultsToDefaultKillImpactSpeed()
    {
        Assert.AreEqual(GameDefaults.DefaultKillImpactSpeed, new DynamicHazard2D().KillImpactSpeed);
    }
}

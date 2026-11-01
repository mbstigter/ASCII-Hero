using ASCII_Hero.Client.Game.Constants;
using ASCII_Hero.Client.Game.Physics;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.World;

[TestClass]
public sealed class ContactImpactTests
{
    [TestMethod]
    public void AddContact_KeepsImpactSpeedPerSide()
    {
        var body = new StaticHazard2D();
        var other = new StaticHazard2D();

        body.AddContact(ContactType.SurfaceLeft, other, impactSpeed: 9.0);
        body.AddContact(ContactType.SurfaceBottom, other, impactSpeed: 2.0);

        Assert.AreEqual(2.0, body.GetContactImpactSpeed(ContactType.SurfaceBottom, other));
        Assert.AreEqual(9.0, body.GetContactImpactSpeed(ContactType.SurfaceLeft, other));
    }

    [TestMethod]
    public void AddContact_KeepsHighestImpactSpeedForSameSide()
    {
        var body = new StaticHazard2D();
        var other = new StaticHazard2D();

        body.AddContact(ContactType.SurfaceBottom, other, impactSpeed: 7.0);
        body.AddContact(ContactType.SurfaceBottom, other, impactSpeed: 3.0);

        Assert.AreEqual(7.0, body.GetContactImpactSpeed(ContactType.SurfaceBottom, other));
    }

    [TestMethod]
    public void HasSurfaceContactWith_IgnoresGripContacts()
    {
        var body = new StaticHazard2D();
        var other = new StaticHazard2D();

        body.AddContact(ContactType.Climbable, other);
        body.AddContact(ContactType.Hangable, other);
        Assert.IsFalse(body.HasSurfaceContactWith(other));

        body.AddContact(ContactType.SurfaceRight, other);
        Assert.IsTrue(body.HasSurfaceContactWith(other));
    }

    [TestMethod]
    public void RemoveContact_ClearsThatSidesImpactSpeed()
    {
        var body = new StaticHazard2D();
        var other = new StaticHazard2D();
        body.AddContact(ContactType.SurfaceBottom, other, impactSpeed: 7.0);

        body.RemoveContact(ContactType.SurfaceBottom);

        Assert.AreEqual(0.0, body.GetContactImpactSpeed(ContactType.SurfaceBottom, other));
    }

    [TestMethod]
    public void ClearContactsForNewStep_ClearsContactsAndImpactSpeeds()
    {
        var body = new StaticHazard2D();
        var other = new StaticHazard2D();
        body.AddContact(ContactType.SurfaceBottom, other, impactSpeed: 7.0);

        body.ClearContactsForNewStep(PhysicsConstants.FixedPhysicsStepSeconds);

        Assert.IsFalse(body.HasSurfaceContactWith(other));
        Assert.AreEqual(0.0, body.GetContactImpactSpeed(ContactType.SurfaceBottom, other));
    }

    [TestMethod]
    public void Hazard_DefaultsToDefaultKillImpactSpeed()
    {
        Assert.AreEqual(GameDefaults.DefaultKillImpactSpeed, new DynamicHazard2D().KillImpactSpeed);
    }
}

using ASCII_Hero.Client.Game.Browser;
using ASCII_Hero.Client.Game.Menu;
using ASCII_Hero.Client.Game.World;

namespace ASCII_Hero.Tests.Menu;

[TestClass]
public sealed class GearRingScreenTests
{
    private static readonly Gear2D Rock = new("Rock", '●');
    private static readonly Gear2D Tomato = new("Tomato", 'o');
    private static readonly Gear2D Grenade = new("Grenade", '♦');
    private static readonly IReadOnlyList<Gear2D> Slots = [Rock, Tomato, Grenade];

    private static void Press(GearRingScreen ring, InputState input, string code)
    {
        input.KeyDown(code);
        ring.Update(input, 0.01);
        input.KeyUp(code);
        ring.Update(input, 0.01);
    }

    /// <summary>Opens the ring with the gear ring key held, as in play, and lets it open fully.</summary>
    private static GearRingScreen OpenFully(InputState input, Gear2D? selected = null)
    {
        input.KeyDown("Tab");
        var ring = new GearRingScreen();
        ring.Open(Slots, selected, input);
        ring.Update(input, GearRingScreen.OpenSeconds);
        return ring;
    }

    [TestMethod]
    public void Open_HighlightsTheCurrentSelection()
    {
        var ring = OpenFully(new InputState(), Tomato);

        Assert.AreSame(Tomato, ring.SelectedSlot);
    }

    [TestMethod]
    public void Open_GrowsFromClosedToFullyOpen()
    {
        var input = new InputState();
        input.KeyDown("Tab");
        var ring = new GearRingScreen();
        ring.Open(Slots, null, input);

        Assert.AreEqual(0.0, ring.Openness);
        Assert.IsFalse(ring.IsFullyOpen);

        ring.Update(input, GearRingScreen.OpenSeconds / 2);
        Assert.IsGreaterThan(0.0, ring.Openness);
        Assert.IsLessThan(1.0, ring.Openness);

        ring.Update(input, GearRingScreen.OpenSeconds / 2);
        Assert.AreEqual(1.0, ring.Openness, 1e-9);
        Assert.IsTrue(ring.IsFullyOpen);
    }

    [TestMethod]
    public void RightAndLeft_MoveAroundTheRingAndWrap()
    {
        var input = new InputState();
        var ring = OpenFully(input, Grenade);

        Press(ring, input, "ArrowRight");
        Assert.AreSame(Rock, ring.SelectedSlot);

        Press(ring, input, "ArrowLeft");
        Press(ring, input, "ArrowLeft");
        Assert.AreSame(Tomato, ring.SelectedSlot);
    }

    [TestMethod]
    public void HeldArrow_MovesOnlyOncePerPress()
    {
        var input = new InputState();
        var ring = OpenFully(input);

        input.KeyDown("ArrowRight");
        ring.Update(input, 0.01);
        ring.Update(input, 0.01);

        Assert.AreSame(Tomato, ring.SelectedSlot);
    }

    [TestMethod]
    public void HeldTab_KeepsTheRingOpen()
    {
        var input = new InputState();
        var ring = OpenFully(input);

        ring.Update(input, 1.0);

        Assert.IsFalse(ring.IsClosing);
    }

    [TestMethod]
    public void ReleasingTab_ConfirmsAndClosesAfterTheCloseAnimation()
    {
        var input = new InputState();
        var ring = OpenFully(input);
        Press(ring, input, "ArrowRight");

        input.KeyUp("Tab");
        ring.Update(input, 0.01);

        Assert.IsTrue(ring.IsClosing);
        Assert.IsTrue(ring.Confirmed);
        Assert.IsFalse(ring.IsClosed);
        Assert.AreSame(Tomato, ring.SelectedSlot);

        ring.Update(input, GearRingScreen.CloseSeconds);
        Assert.IsTrue(ring.IsClosed);
        Assert.AreEqual(0.0, ring.Openness, 1e-9);
    }

    [TestMethod]
    public void ActionKey_DoesNotConfirm()
    {
        var input = new InputState();
        var ring = OpenFully(input);

        Press(ring, input, "Enter");

        Assert.IsFalse(ring.IsClosing);
    }

    [TestMethod]
    public void Escape_CancelsWithoutConfirming()
    {
        var input = new InputState();
        var ring = OpenFully(input);
        Press(ring, input, "ArrowRight");

        Press(ring, input, "Escape");

        Assert.IsTrue(ring.IsClosing);
        Assert.IsFalse(ring.Confirmed);
    }

    [TestMethod]
    public void WhileClosing_ArrowsNoLongerMoveTheSelection()
    {
        var input = new InputState();
        var ring = OpenFully(input);
        input.KeyUp("Tab");
        ring.Update(input, 0.01);

        Press(ring, input, "ArrowRight");

        Assert.AreSame(Rock, ring.SelectedSlot);
    }

    [TestMethod]
    public void ClosingWhileStillOpening_ShrinksFromWhereItWas()
    {
        var input = new InputState();
        input.KeyDown("Tab");
        var ring = new GearRingScreen();
        ring.Open(Slots, null, input);
        ring.Update(input, GearRingScreen.OpenSeconds / 2);
        var openness = ring.Openness;

        input.KeyDown("Escape");
        ring.Update(input, 0.0);

        Assert.AreEqual(openness, ring.Openness, 1e-9);
    }

    [TestMethod]
    public void StillSeconds_CountsOnlyOnceFullyOpen()
    {
        var input = new InputState();
        input.KeyDown("Tab");
        var ring = new GearRingScreen();
        ring.Open(Slots, null, input);

        ring.Update(input, GearRingScreen.OpenSeconds / 2);
        Assert.AreEqual(0.0, ring.StillSeconds);

        ring.Update(input, GearRingScreen.OpenSeconds / 2 + 0.25);
        Assert.AreEqual(0.25, ring.StillSeconds, 1e-9);
    }

    [TestMethod]
    public void StillSeconds_RestartsWhenTheHighlightMoves()
    {
        var input = new InputState();
        var ring = OpenFully(input);
        ring.Update(input, 1.0);

        input.KeyDown("ArrowRight");
        ring.Update(input, 0.01);

        Assert.AreEqual(0.0, ring.StillSeconds);
    }

    [TestMethod]
    public void StillSeconds_IsZeroWhileClosing()
    {
        var input = new InputState();
        var ring = OpenFully(input);
        ring.Update(input, 1.0);

        input.KeyDown("Escape");
        ring.Update(input, 0.01);

        Assert.AreEqual(0.0, ring.StillSeconds);
    }
}

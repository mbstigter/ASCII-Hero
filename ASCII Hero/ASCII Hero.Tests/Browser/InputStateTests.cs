using ASCII_Hero.Client.Game.Browser;

namespace ASCII_Hero.Tests.Browser;

[TestClass]
public sealed class InputStateTests
{
    [TestMethod]
    public void IsJumpKeyDown_StaysTrueWhileHeld()
    {
        var input = new InputState();
        input.KeyDown("Space");

        Assert.IsTrue(input.IsJumpKeyDown);
        Assert.IsTrue(input.IsJumpKeyDown);
    }

    [TestMethod]
    public void IsJumpKeyDown_FalseAfterKeyUp()
    {
        var input = new InputState();
        input.KeyDown("Space");
        input.KeyUp("Space");

        Assert.IsFalse(input.IsJumpKeyDown);
    }

    [TestMethod]
    public void IsUpKeyDown_NeverIncludesJumpKeys()
    {
        var input = new InputState();
        input.KeyDown("Space");
        input.KeyDown("ControlLeft");

        Assert.IsFalse(input.IsUpKeyDown);
    }

    [TestMethod]
    public void IsLeftKeyDown_ArrowAndWasdBothWork()
    {
        var arrow = new InputState();
        arrow.KeyDown("ArrowLeft");
        var wasd = new InputState();
        wasd.KeyDown("KeyA");

        Assert.IsTrue(arrow.IsLeftKeyDown);
        Assert.IsTrue(wasd.IsLeftKeyDown);
    }

    [TestMethod]
    public void IsConfirmKeyDown_ActionOrJumpKeys()
    {
        foreach (var code in new[] { "Enter", "KeyE", "Space", "ControlLeft" })
        {
            var input = new InputState();
            input.KeyDown(code);
            Assert.IsTrue(input.IsConfirmKeyDown, code);
        }
    }
}

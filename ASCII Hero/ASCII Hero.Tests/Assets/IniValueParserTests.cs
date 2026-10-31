using ASCII_Hero.Client.Game.Assets;

namespace ASCII_Hero.Tests.Assets;

[TestClass]
public sealed class IniValueParserTests
{
    [TestMethod]
    public void ParseDoubleOrDefault_ValidNumber_ReturnsNumber()
    {
        Assert.AreEqual(1.5, IniValueParser.ParseDoubleOrDefault("1.5"));
    }

    [TestMethod]
    public void ParseDoubleOrDefault_DecimalPoint_IsReadCultureInvariant()
    {
        Assert.AreEqual(1000.25, IniValueParser.ParseDoubleOrDefault("1000.25"));
    }

    [TestMethod]
    public void ParseDoubleOrDefault_MissingValue_ReturnsZero()
    {
        Assert.AreEqual(0.0, IniValueParser.ParseDoubleOrDefault(null));
    }

    [TestMethod]
    public void ParseDoubleOrDefault_UnreadableValue_ReturnsZero()
    {
        Assert.AreEqual(0.0, IniValueParser.ParseDoubleOrDefault("abc"));
    }

    [TestMethod]
    public void TryParseDouble_ValidNumber_ReturnsTrueAndValue()
    {
        bool success = IniValueParser.TryParseDouble("2.25", out double value);

        Assert.IsTrue(success);
        Assert.AreEqual(2.25, value);
    }

    [TestMethod]
    public void TryParseDouble_UnreadableValue_ReturnsFalse()
    {
        Assert.IsFalse(IniValueParser.TryParseDouble("abc", out _));
    }

    [TestMethod]
    public void ParseEmptyChar_MissingValue_ReturnsSpace()
    {
        Assert.AreEqual(' ', IniValueParser.ParseEmptyChar(null));
    }

    [TestMethod]
    public void ParseEmptyChar_Value_ReturnsFirstCharacter()
    {
        Assert.AreEqual('.', IniValueParser.ParseEmptyChar(".x"));
    }

    [TestMethod]
    public void ParseColorCode_MissingValue_ReturnsNull()
    {
        Assert.IsNull(IniValueParser.ParseColorCode(""));
    }

    [TestMethod]
    public void ParseColorCode_Value_ReturnsFirstCharacter()
    {
        Assert.AreEqual('R', IniValueParser.ParseColorCode("R"));
    }
}

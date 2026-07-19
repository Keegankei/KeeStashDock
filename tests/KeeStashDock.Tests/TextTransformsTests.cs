using KeeStashDock.Core.Services;

namespace KeeStashDock.Tests;

[TestClass]
public sealed class TextTransformsTests
{
    [TestMethod]
    public void CollapseWhitespace_TrimsAndCollapsesEveryRun()
    {
        const string input = "  alpha\t beta\r\n gamma  ";

        string result = TextTransforms.CollapseWhitespace(input);

        Assert.AreEqual("alpha beta gamma", result);
    }

    [TestMethod]
    public void FormatJson_PrettyPrintsValidJson()
    {
        string? result = TextTransforms.FormatJson("{\"answer\":42,\"ok\":true}");

        Assert.IsNotNull(result);
        StringAssert.Contains(result, "\n");
        StringAssert.Contains(result, "\"answer\": 42");
    }

    [TestMethod]
    public void FormatJson_ReturnsNullForInvalidJson()
    {
        Assert.IsNull(TextTransforms.FormatJson("{not-json}"));
    }

    [TestMethod]
    public void Base64_RoundTripsUnicodeText()
    {
        const string input = "KeeStashDock / Unicode \u2713";

        string encoded = TextTransforms.Base64Encode(input);
        string? decoded = TextTransforms.Base64Decode(encoded);

        Assert.AreEqual(input, decoded);
    }

    [TestMethod]
    public void Base64Decode_ReturnsNullForInvalidInput()
    {
        Assert.IsNull(TextTransforms.Base64Decode("not valid base64"));
    }

    [TestMethod]
    public void Count_ReturnsCharactersWordsAndLines()
    {
        var result = TextTransforms.Count("one two\nthree");

        Assert.AreEqual(13, result.Chars);
        Assert.AreEqual(11, result.NonWs);
        Assert.AreEqual(3, result.Words);
        Assert.AreEqual(2, result.Lines);
    }
}

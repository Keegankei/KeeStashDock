using KeeStashDock.Core.Services;
using KeeStashDock.Domain.Entities;

namespace KeeStashDock.Tests;

[TestClass]
public sealed class TypeDetectorTests
{
    [DataTestMethod]
    [DataRow("https://example.com/path?q=1")]
    [DataRow("HTTP://EXAMPLE.COM")]
    public void Detect_RecognizesSingleLineHttpLinks(string content)
    {
        Assert.AreEqual(SnippetType.Link, TypeDetector.Detect(content));
    }

    [DataTestMethod]
    [DataRow("# Heading\nBody")]
    [DataRow("1. First item")]
    [DataRow("- [ ] Task")]
    public void Detect_RecognizesMarkdownSignals(string content)
    {
        Assert.AreEqual(SnippetType.Markdown, TypeDetector.Detect(content));
    }

    [DataTestMethod]
    [DataRow("public class Sample { private int value; }")]
    [DataRow("def run():\n    return 42")]
    public void Detect_RecognizesCodeSignals(string content)
    {
        Assert.AreEqual(SnippetType.Code, TypeDetector.Detect(content));
    }

    [TestMethod]
    public void Detect_DefaultsToPlainText()
    {
        Assert.AreEqual(SnippetType.PlainText, TypeDetector.Detect("A normal sentence."));
    }
}

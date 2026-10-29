using KeeStashDock.Core.Services;
using KeeStashDock.Domain.Entities;

namespace KeeStashDock.Tests;

[TestClass]
public sealed class TemplateParserTests
{
    [TestMethod]
    public void NamedFields_RespectOrderUnicodeCaseAndSingleTimeSnapshot()
    {
        var document = TemplateParser.Parse("{{name}} {{\u9879\u76ee}} {{name}} {{Name}} {{date}} {{time}} {{date}}");
        CollectionAssert.AreEqual(new[] { "name", "\u9879\u76ee", "Name" }, document.Variables.ToArray());
        Assert.AreEqual("Ada \u4e2d\u6587 Ada Other 2026-10-22 23:59 2026-10-22",
            document.Render(new Dictionary<string, string> { ["name"] = "Ada", ["\u9879\u76ee"] = "\u4e2d\u6587", ["Name"] = "Other" },
                new DateTime(2026, 10, 22, 23, 59, 59)));
    }

    [TestMethod]
    public void EscapedBracesAndInputValues_AreNeverRecursivelyEvaluated()
    {
        var document = TemplateParser.Parse("{{{{name}}}} / {{name}} / {{empty}}");
        Assert.AreEqual("{{name}} / {{date}} / ", document.Render(
            new Dictionary<string, string> { ["name"] = "{{date}}", ["empty"] = "" }, DateTime.Now));
    }

    [TestMethod]
    [DataRow("{{name", 0)]
    [DataRow("A {{1name}}", 2)]
    [DataRow("{{name:format}}", 0)]
    [DataRow("{{ name }}", 0)]
    [DataRow("{{name={{other}}}}", 0)]
    [DataRow("x }}", 2)]
    public void InvalidSyntax_ReportsItsOriginalPosition(string text, int offset)
    {
        var error = Assert.ThrowsExactly<TemplateParseException>(() => TemplateParser.Parse(text));
        Assert.AreEqual(offset, error.Position);
    }

    [TestMethod]
    public void MissingFieldsAndLimits_AreExplicitFailures()
    {
        Assert.ThrowsExactly<TemplateParseException>(() => TemplateParser.Parse("{{missing}}").Render(new Dictionary<string, string>(), DateTime.Now));
        Assert.ThrowsExactly<TemplateParseException>(() => TemplateParser.Parse(string.Concat(Enumerable.Range(0, 65).Select(i => "{{field" + i + "}}"))));
        Assert.ThrowsExactly<TemplateParseException>(() => TemplateParser.Parse("{{v}}{{v}}").Render(
            new Dictionary<string, string> { ["v"] = new string('x', TemplateParser.MaxTextLength) }, DateTime.Now));
    }

    [TestMethod]
    public async Task TemplateFlag_ParticipatesInEditorDraftAndDiscard()
    {
        var session = new WorkbenchEditorSession();
        session.Load(new WorkbenchEditSnapshot(1, null, "{{name}}", ExpirePolicy.Never, []));
        session.EditIsTemplate = true;
        Assert.IsTrue(session.IsDirty);
        Assert.IsTrue(session.CreateSaveSnapshot().IsTemplate);
        await session.ResolveUnsavedChangesAsync(_ => Task.FromResult(WorkbenchUnsavedChoice.Discard), (_, _) => Task.CompletedTask);
        Assert.IsFalse(session.EditIsTemplate);
        Assert.IsFalse(session.IsDirty);
    }
}

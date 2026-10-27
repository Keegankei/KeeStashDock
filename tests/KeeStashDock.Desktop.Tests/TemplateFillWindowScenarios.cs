using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using KeeStashDock.Core.Services;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Desktop.Views;
using KeeStashDock.Domain.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

internal static class TemplateFillWindowScenarios
{
    internal static void Verify()
    {
        string language = LocalizationService.Instance.CurrentLanguage;
        try
        {
            LocalizationService.Instance.SetLanguage("en-US");
            var ordinary = new Snippet { Content = "{{name}}", IsTemplate = false };
            Assert.AreEqual("{{name}}", TemplateUseCoordinator.PrepareAsync(ordinary).GetAwaiter().GetResult(),
                "Ordinary text never prompts for variables or expands braces.");

            var window = new TemplateFillWindow(TemplateParser.Parse("{{name}} — {{date}} {{time}} — {{name}}"));
            try
            {
                Dictionary<string, TextBox> fields = Field<Dictionary<string, TextBox>>(window, "_fields");
                Assert.AreEqual(1, fields.Count, "Repeated names share one input.");
                string? first = window.ResultText;
                fields["name"].Text = "{{date}} & <plain text>";
                Assert.IsTrue(window.ResultText!.StartsWith("{{date}} & <plain text> — ", StringComparison.Ordinal));
                Assert.IsTrue(window.ResultText.EndsWith(" — {{date}} & <plain text>", StringComparison.Ordinal));
                string timePart = first!.Split(" — ")[1];
                Assert.AreEqual(timePart, window.ResultText.Split(" — ")[1], "Editing fields preserves one date/time snapshot.");
                Assert.AreEqual(window.ResultText, Field<TextBox>(window, "_preview").Text);
                Assert.IsTrue(Field<Button>(window, "_accept").IsEnabled);
                Assert.IsNull(window.DialogResult, "Previewing does not accept or copy anything.");
            }
            finally { window.Close(); }

            var empty = new TemplateFillWindow(TemplateParser.Parse("{{value}}"));
            try
            {
                Assert.IsFalse(Field<Button>(empty, "_accept").IsEnabled, "Empty output cannot be copied.");
                Field<Dictionary<string, TextBox>>(empty, "_fields")["value"].Text = "ready";
                Assert.IsTrue(Field<Button>(empty, "_accept").IsEnabled);
            }
            finally { empty.Close(); }

            var large = new TemplateFillWindow(TemplateParser.Parse(string.Concat(Enumerable.Repeat("{{value}}", 11))));
            try
            {
                Field<Dictionary<string, TextBox>>(large, "_fields")["value"].Text = new string('x', 100_000);
                Assert.IsNull(large.ResultText, "Oversized expansion has no usable output.");
                Assert.IsFalse(Field<Button>(large, "_accept").IsEnabled);
                Assert.AreEqual(string.Empty, Field<TextBox>(large, "_preview").Text);
            }
            finally { large.Close(); }
        }
        finally { LocalizationService.Instance.SetLanguage(language); }
    }

    private static T Field<T>(object instance, string name) => (T)instance.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
}

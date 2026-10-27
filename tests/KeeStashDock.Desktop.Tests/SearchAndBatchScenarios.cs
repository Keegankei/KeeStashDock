using System.IO;
using System.Windows.Threading;
using KeeStashDock.Core.Services;
using KeeStashDock.Desktop.ViewModels;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Logging;
using KeeStashDock.Infrastructure.Repositories;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

/// <summary>UI state regressions using an isolated library and the existing STA dispatcher.</summary>
internal static class SearchAndBatchScenarios
{
    internal static void Verify(string isolatedRoot)
    {
        string root = Path.Combine(isolatedRoot, "search-and-batch");
        Directory.CreateDirectory(root);
        using var log = new FileLogger(Path.Combine(root, "logs"));
        var db = new Db(Path.Combine(root, "library.db"), new DatabaseMaintenanceCoordinator());
        new MigrationRunner(db, log).Run();
        var repository = new SnippetRepository(db);
        var snippets = new SnippetService(repository, log);
        var search = new SearchService(repository);
        var categories = new CategoryRepository(db);
        var tags = new TagRepository(db);
        long writing = categories.Insert(new Category { Name = "Writing" });
        for (int index = 0; index < 125; index++)
        {
            long id = snippets.Add($"Search sample {index:D3}", SnippetSource.Manual,
                categoryId: index < 65 ? writing : null);
            if (index < 10) tags.SetForSnippet(id, ["review", "keep"]);
        }
        long templateId = snippets.Add("Hello {{name}}, prepared on {{date}}.", SnippetSource.Manual, isTemplate: true);

        using (var panel = new QuickPanelViewModel(snippets, search, categories))
        {
            panel.ReloadCategories();
            panel.Reload();
            panel.QuickInput = "A collection draft stays separate from search";
            panel.GroupIndex = 1;
            panel.ExpireIndex = (int)ExpirePolicy.SevenDays;
            panel.BeginSearch();
            Complete(panel.SearchCompletion);
            Assert.AreEqual(50, panel.Items.Count);
            Assert.IsTrue(panel.HasMoreSearchResults);
            Complete(panel.LoadMoreSearchCommand.ExecuteAsync(null));
            Assert.AreEqual(65, panel.Items.Count, "Searching the selected group must page beyond the first 50 entries.");
            Assert.IsFalse(panel.HasMoreSearchResults);
            panel.SearchGroupIndex = 0;
            Complete(panel.SearchCompletion);
            Complete(panel.LoadMoreSearchCommand.ExecuteAsync(null));
            Complete(panel.LoadMoreSearchCommand.ExecuteAsync(null));
            Assert.AreEqual(126, panel.Items.Count, "Search must include older rows beyond the quick panel's recent 100.");
            Assert.AreEqual(126, panel.Items.Select(item => item.Id).Distinct().Count());
            panel.Filter = "sample 000";
            Task stale = panel.SearchCompletion;
            panel.Filter = "sample 124";
            Complete(panel.SearchCompletion);
            Complete(stale);
            Assert.AreEqual(1, panel.Items.Count);
            Assert.AreEqual("Search sample 124", panel.Items[0].Model.Content,
                "A superseded debounce/query must never replace the latest result.");
            panel.Filter = string.Empty;
            panel.SearchTag = "review";
            Complete(panel.SearchCompletion);
            Assert.AreEqual(10, panel.Items.Count);
            panel.SearchTag = string.Empty;
            panel.SearchKindIndex = (int)SnippetKindFilter.Template;
            Complete(panel.SearchCompletion);
            Assert.AreEqual(templateId, panel.Items.Single().Id);
            panel.QuickAddCommand.Execute(null);
            Assert.AreEqual(126, search.Query(new()).TotalCount, "Enter/search actions must not collect the search query or draft.");
            panel.EndSearch();
            Assert.AreEqual("A collection draft stays separate from search", panel.QuickInput);
            Assert.AreEqual(writing, panel.SelectedCategoryId);
            Assert.AreEqual((int)ExpirePolicy.SevenDays, panel.ExpireIndex);
            Assert.AreEqual(65, panel.Items.Count);
        }

        WorkbenchUnsavedChoice choice = WorkbenchUnsavedChoice.Cancel;
        var workbench = new WorkbenchViewModel(snippets, search, categories, tags,
            _ => Task.FromResult(choice));
        Assert.AreEqual(50, workbench.Items.Count);
        long? editorId = workbench.SelectedItem?.Id;
        workbench.SetBatchSelection(workbench.Items.Take(3));
        Assert.AreEqual(3, workbench.BatchSelectedCount);
        Assert.AreEqual(editorId, workbench.SelectedItem?.Id, "Batch selection must not move the editor.");
        Complete(workbench.LoadMoreCommand.ExecuteAsync(null));
        Assert.AreEqual(100, workbench.Items.Count);
        Assert.AreEqual(3, workbench.BatchSelectedCount, "Loading another page retains the explicit selection.");
        workbench.EditContent += " unsaved draft";
        long[] selected = workbench.Items.Take(3).Select(item => item.Id).ToArray();
        Complete(workbench.ApplyBatchAsync(BatchAction.Trash));
        Assert.IsTrue(workbench.IsDirty);
        Assert.IsTrue(selected.All(id => !snippets.GetById(id)!.IsDeleted), "Cancelling the dirty editor prompt cancels the whole batch.");
        choice = WorkbenchUnsavedChoice.Discard;
        Complete(workbench.ApplyBatchAsync(BatchAction.AddTags, tags: ["organized"]));
        Assert.IsFalse(workbench.IsDirty);
        Assert.IsTrue(selected.All(id => tags.GetForSnippet(id).Contains("organized")));
        Assert.AreEqual(0, workbench.BatchSelectedCount);
        workbench.SetBatchSelection(workbench.Items.Where(item => selected.Contains(item.Id)));
        Complete(workbench.ApplyBatchAsync(BatchAction.Trash));
        Assert.IsTrue(selected.All(id => snippets.GetById(id)!.IsDeleted));
        Assert.IsTrue(workbench.CanUndoBatch);
        Complete(workbench.UndoBatchCommand.ExecuteAsync(null));
        Assert.IsTrue(selected.All(id => !snippets.GetById(id)!.IsDeleted));
        Assert.IsFalse(workbench.CanUndoBatch);

        workbench.SetBatchSelection(workbench.Items.Take(2));
        long externallyChanged = workbench.Items[0].Id;
        // An external tag write deliberately bypasses Changed so the captured
        // revision remains old, as it could during a background import.
        tags.SetForSnippet(externallyChanged, ["external"]);
        Task<BatchConflictReason?> conflict = workbench.ApplyBatchAsync(BatchAction.Pin);
        Complete(conflict);
        Assert.AreEqual(BatchConflictReason.ChangedItem, conflict.Result);
        Assert.IsFalse(snippets.GetById(externallyChanged)!.IsPinned);
        workbench.SearchText = "sample 024";
        Assert.AreEqual(0, workbench.BatchSelectedCount, "Changing the query clears the previous batch scope.");
        Complete(workbench.QueryCompletion);
        Assert.AreEqual("Search sample 024", workbench.Items.Single().Model.Content);
        workbench.FilterTag = "review";
        Complete(workbench.QueryCompletion);
        Assert.AreEqual(0, workbench.Items.Count, "The tag filter must combine with text in the database query.");
        workbench.FilterTag = string.Empty;
        workbench.SearchText = string.Empty;
        workbench.FilterKindIndex = (int)SnippetKindFilter.Template;
        Complete(workbench.QueryCompletion);
        Assert.AreEqual(templateId, workbench.Items.Single().Id);
        Assert.IsTrue(workbench.EditIsTemplate);
        workbench.SearchText = "no matching template";
        Complete(workbench.QueryCompletion);
        Complete(workbench.NewTemplateCommand.ExecuteAsync(null));
        Assert.IsTrue(workbench.EditIsTemplate, "Creating a template must open its editor even when the previous filter hid templates.");
        Assert.AreEqual(string.Empty, workbench.SearchText);
    }

    private static void Complete(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            timer.Tick += (_, _) => frame.Continue = !task.IsCompleted && DateTime.UtcNow < deadline;
            timer.Start();
            try { Dispatcher.PushFrame(frame); }
            finally { timer.Stop(); }
        }
        Assert.IsTrue(task.IsCompleted, "The asynchronous operation must finish without blocking the UI dispatcher.");
        task.GetAwaiter().GetResult();
    }
}

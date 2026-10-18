using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Repositories;

namespace KeeStashDock.Core.Services;

/// <summary>Explicit text-only persistence for temporary OCR/translation results.</summary>
public sealed class ResultTextSaveService(SnippetService snippets, CategoryRepository categories)
{
    public long Save(string text, long? categoryId)
    {
        if (categoryId is long group && !categories.GetAll().Any(category => category.Id == group))
            throw new InvalidOperationException("The selected group no longer exists.");
        long id = snippets.Add(text, SnippetSource.Manual, categoryId: categoryId,
            explicitType: SnippetType.PlainText, placeInCategoryOnDuplicate: true);
        Snippet? saved = snippets.GetById(id);
        if (saved is null || saved.IsDeleted || saved.IsExpired(DateTime.UtcNow)
            || saved.Content != text.TrimEnd('\r', '\n') || saved.CategoryId != categoryId)
            throw new InvalidOperationException("The result entry could not be verified.");
        return id;
    }
}

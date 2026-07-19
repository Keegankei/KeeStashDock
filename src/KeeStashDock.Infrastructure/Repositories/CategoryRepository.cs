using Microsoft.Data.Sqlite;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Database;

namespace KeeStashDock.Infrastructure.Repositories;

/// <summary>CRUD for categories. Deleting a category detaches its snippets
/// (FK is ON DELETE SET NULL) so no content is ever lost by group removal.</summary>
public sealed class CategoryRepository
{
    private readonly Db _db;
    public CategoryRepository(Db db) => _db = db;

    /// <summary>Raised after every category mutation (insert / rename /
    /// delete - including group imports, which insert through this repo).
    /// The quick panel's group combo and the workbench sidebar subscribe so
    /// group edits HOT-apply everywhere without re-summoning any window.</summary>
    public event Action? Changed;

    public List<Category> GetAll()
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM categories ORDER BY sort_order, name";
        using var rd = cmd.ExecuteReader();
        var list = new List<Category>();
        while (rd.Read())
            list.Add(new Category
            {
                Id        = rd.GetInt64(rd.GetOrdinal("id")),
                Name      = rd.GetString(rd.GetOrdinal("name")),
                Icon      = rd.GetString(rd.GetOrdinal("icon")),
                Color     = rd.GetString(rd.GetOrdinal("color")),
                SortOrder = rd.GetInt32(rd.GetOrdinal("sort_order")),
            });
        return list;
    }

    public long Insert(Category c)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO categories (name, icon, color, sort_order)
            VALUES ($n, $i, $c, $s); SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$n", c.Name);
        cmd.Parameters.AddWithValue("$i", c.Icon);
        cmd.Parameters.AddWithValue("$c", c.Color);
        cmd.Parameters.AddWithValue("$s", c.SortOrder);
        long id = (long)cmd.ExecuteScalar()!;
        Changed?.Invoke();
        return id;
    }

    public void Rename(long id, string name)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE categories SET name = $n WHERE id = $id";
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        Changed?.Invoke();
    }

    public void Delete(long id)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM categories WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        Changed?.Invoke();
    }
}

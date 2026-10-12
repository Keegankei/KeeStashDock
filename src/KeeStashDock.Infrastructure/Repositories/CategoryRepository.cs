using Microsoft.Data.Sqlite;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Database;

namespace KeeStashDock.Infrastructure.Repositories;

/// <summary>CRUD for categories. Deleting a category detaches its snippets
/// (FK is ON DELETE SET NULL) so no content is ever lost by group removal.</summary>
public sealed partial class CategoryRepository
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
            list.Add(Read(rd));
        return list;
    }

    public Category? FindByName(string name, long? excludingId = null)
    {
        string key = CategoryName.GetKey(name);
        using var conn = _db.Open();
        return FindByKey(conn, key, excludingId);
    }

    /// <summary>Returns a trimmed available name, adding (2), (3), etc. as needed.
    /// The eventual insert still checks uniqueness in case another writer intervenes.</summary>
    public string GetUniqueName(string baseName)
    {
        string name = CategoryName.Normalize(baseName);
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name_key FROM categories";
        using var rd = cmd.ExecuteReader();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        while (rd.Read()) keys.Add(rd.GetString(0));
        return CategoryName.GetUnique(name, keys);
    }

    public long Insert(Category c)
    {
        ArgumentNullException.ThrowIfNull(c);
        string name = CategoryName.Normalize(c.Name);
        string key = CategoryName.GetKey(name);
        long id;
        using (var conn = _db.Open())
        using (var tx = conn.BeginTransaction())
        {
            ThrowIfDuplicate(conn, tx, name, key);
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO categories (name, name_key, icon, color, sort_order)
                VALUES ($n, $key, $i, $c, $s); SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$n", name);
            cmd.Parameters.AddWithValue("$key", key);
            cmd.Parameters.AddWithValue("$i", c.Icon);
            cmd.Parameters.AddWithValue("$c", c.Color);
            cmd.Parameters.AddWithValue("$s", c.SortOrder);
            id = (long)cmd.ExecuteScalar()!;
            tx.Commit();
        }
        c.Name = name;
        Changed?.Invoke();
        return id;
    }

    public void Rename(long id, string name)
    {
        name = CategoryName.Normalize(name);
        string key = CategoryName.GetKey(name);
        using (var conn = _db.Open())
        using (var tx = conn.BeginTransaction())
        {
            ThrowIfDuplicate(conn, tx, name, key, id);
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE categories SET name = $n, name_key = $key WHERE id = $id";
            cmd.Parameters.AddWithValue("$n", name);
            cmd.Parameters.AddWithValue("$key", key);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
        Changed?.Invoke();
    }

    public void Delete(long id)
    {
        using (var conn = _db.Open())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM categories WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        Changed?.Invoke();
    }

    private static Category? FindByKey(SqliteConnection conn, string key,
        long? excludingId = null, SqliteTransaction? transaction = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            SELECT * FROM categories WHERE name_key = $key
                AND ($excluded IS NULL OR id <> $excluded) LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$excluded", (object?)excludingId ?? DBNull.Value);
        using var rd = cmd.ExecuteReader();
        return rd.Read() ? Read(rd) : null;
    }

    private static void ThrowIfDuplicate(SqliteConnection conn, SqliteTransaction transaction,
        string name, string key, long? excludingId = null)
    {
        if (FindByKey(conn, key, excludingId, transaction) is { } existing)
            throw new DuplicateCategoryNameException(name, existing.Id);
    }

    private static Category Read(SqliteDataReader rd) => new()
    {
        Id        = rd.GetInt64(rd.GetOrdinal("id")),
        Name      = rd.GetString(rd.GetOrdinal("name")),
        Icon      = rd.GetString(rd.GetOrdinal("icon")),
        Color     = rd.GetString(rd.GetOrdinal("color")),
        SortOrder = rd.GetInt32(rd.GetOrdinal("sort_order")),
    };
}

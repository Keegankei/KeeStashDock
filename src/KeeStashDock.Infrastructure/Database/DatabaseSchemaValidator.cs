using KeeStashDock.Domain.Entities;
using Microsoft.Data.Sqlite;

namespace KeeStashDock.Infrastructure.Database;

/// <summary>Performs read-only integrity and application-schema validation.</summary>
public sealed class DatabaseSchemaValidator
{
    private static readonly IReadOnlyDictionary<string, string[]> RequiredColumns =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["schema_version"] = ["version"],
            ["categories"] = ["id", "name", "icon", "color", "sort_order"],
            ["snippets"] =
            [
                "id", "title", "content", "content_hash", "type", "source",
                "category_id", "is_pinned", "expire_policy", "expire_at",
                "created_at", "updated_at", "deleted_at", "sort_weight", "use_count",
            ],
            ["tags"] = ["id", "name"],
            ["snippet_tags"] = ["snippet_id", "tag_id"],
            ["snippets_fts"] = ["title", "content"],
        };

    private static readonly string[] RequiredIndexes =
        ["idx_snippets_live", "idx_snippets_hash", "idx_snippets_cat"];

    private static readonly string[] RequiredFtsTriggers =
        ["trg_snip_ai", "trg_snip_ad", "trg_snip_au"];

    public DatabaseValidationResult Validate(
        string databaseFile,
        bool requireCurrentUserVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseFile);
        string path = Path.GetFullPath(databaseFile);
        if (!File.Exists(path))
        {
            throw new DatabaseValidationException("The selected database file does not exist.");
        }

        if (new FileInfo(path).Length == 0)
        {
            throw new DatabaseValidationException("The selected database file is empty.");
        }

        try
        {
            using SqliteConnection connection = OpenReadOnly(path);
            ValidateQuickCheck(connection);

            int userVersion = ReadInt(connection, "PRAGMA user_version");
            if (userVersion > MigrationRunner.LatestSupportedVersion)
            {
                throw new DatabaseValidationException(
                    $"This database was created by a newer KeeStashDock version " +
                    $"(schema {userVersion}); this version supports up to " +
                    $"{MigrationRunner.LatestSupportedVersion}.");
            }

            foreach ((string table, string[] columns) in RequiredColumns)
            {
                ValidateColumns(connection, table, columns);
            }

            int schemaVersion = ReadInt(
                connection,
                "SELECT version FROM schema_version LIMIT 1");
            if (schemaVersion > MigrationRunner.LatestSupportedVersion)
            {
                throw new DatabaseValidationException(
                    $"This database was created by a newer KeeStashDock version " +
                    $"(schema {schemaVersion}); this version supports up to " +
                    $"{MigrationRunner.LatestSupportedVersion}.");
            }

            if (schemaVersion < 1)
            {
                throw new DatabaseValidationException(
                    "The selected database has an unsupported legacy schema.");
            }

            if (schemaVersion >= 2)
            {
                ValidateColumns(connection, "snippets", ["last_used_at"]);
            }
            if (schemaVersion >= 3)
            {
                ValidateCategoryNames(connection);
            }

            if (requireCurrentUserVersion &&
                (userVersion != MigrationRunner.LatestSupportedVersion ||
                 schemaVersion != MigrationRunner.LatestSupportedVersion))
            {
                throw new DatabaseValidationException(
                    $"Database migration did not reach schema " +
                    $"{MigrationRunner.LatestSupportedVersion}.");
            }

            ValidateNamedObjects(connection, "index", RequiredIndexes);
            ValidateNamedObjects(connection, "trigger", RequiredFtsTriggers);
            ValidateFtsDefinition(connection);
            ValidateForeignKeys(connection);

            return new DatabaseValidationResult(userVersion, schemaVersion);
        }
        catch (DatabaseValidationException)
        {
            throw;
        }
        catch (SqliteException exception)
        {
            throw new DatabaseValidationException(
                "The selected file is not a valid, readable KeeStashDock database.",
                exception);
        }
    }

    private static void ValidateCategoryNames(SqliteConnection connection)
    {
        ValidateColumns(connection, "categories", ["name_key"]);
        ValidateNamedObjects(connection, "trigger",
            ["trg_category_name_insert", "trg_category_name_update"]);

        bool uniqueIndex = false;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA index_list(categories)";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetString(1) == "idx_categories_name_key")
                    uniqueIndex = reader.GetInt32(2) == 1 && reader.GetInt32(4) == 0;
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA index_xinfo(idx_categories_name_key)";
            using var reader = command.ExecuteReader();
            int keyColumns = 0;
            while (reader.Read())
            {
                if (reader.GetInt32(5) == 0) continue;
                keyColumns++;
                uniqueIndex &= !reader.IsDBNull(2) && reader.GetString(2) == "name_key" &&
                    reader.GetString(4) == "BINARY";
            }
            uniqueIndex &= keyColumns == 1;
        }
        if (!uniqueIndex)
            throw new DatabaseValidationException("The selected database has no valid unique group-name index.");

        using var readNames = connection.CreateCommand();
        readNames.CommandText = "SELECT name, name_key FROM categories";
        using var names = readNames.ExecuteReader();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        while (names.Read())
        {
            if (names.IsDBNull(0) || names.IsDBNull(1))
                throw new DatabaseValidationException("The selected database contains an invalid group name.");
            string name = names.GetString(0);
            string key = names.GetString(1);
            if (string.IsNullOrWhiteSpace(name) || name != name.Trim() ||
                key != CategoryName.GetKey(name) || !keys.Add(key))
                throw new DatabaseValidationException("The selected database contains an invalid or duplicate group name.");
        }
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
        connection.Open();
        return connection;
    }

    private static void ValidateQuickCheck(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check";
        using SqliteDataReader reader = command.ExecuteReader();
        var results = new List<string>();
        while (reader.Read())
        {
            results.Add(reader.GetString(0));
        }

        if (results.Count != 1 ||
            !string.Equals(results[0], "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new DatabaseValidationException(
                $"Database integrity check failed: {string.Join("; ", results)}");
        }
    }

    private static void ValidateColumns(
        SqliteConnection connection,
        string table,
        IEnumerable<string> required)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info([{table}])";
        using SqliteDataReader reader = command.ExecuteReader();
        var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            actual.Add(reader.GetString(reader.GetOrdinal("name")));
        }

        if (actual.Count == 0)
        {
            throw new DatabaseValidationException(
                $"The selected database is missing required table '{table}'.");
        }

        string[] missing = required.Where(column => !actual.Contains(column)).ToArray();
        if (missing.Length > 0)
        {
            throw new DatabaseValidationException(
                $"Table '{table}' is missing required column(s): " +
                string.Join(", ", missing));
        }
    }

    private static void ValidateNamedObjects(
        SqliteConnection connection,
        string type,
        IEnumerable<string> names)
    {
        foreach (string name in names)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type = $type AND name = $name";
            command.Parameters.AddWithValue("$type", type);
            command.Parameters.AddWithValue("$name", name);
            if (Convert.ToInt32(command.ExecuteScalar()) != 1)
            {
                throw new DatabaseValidationException(
                    $"The selected database is missing required {type} '{name}'.");
            }
        }
    }

    private static void ValidateFtsDefinition(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'snippets_fts'";
        string definition = Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
        if (!definition.Contains("USING fts5", StringComparison.OrdinalIgnoreCase) ||
            !definition.Contains("content='snippets'", StringComparison.OrdinalIgnoreCase))
        {
            throw new DatabaseValidationException(
                "The selected database has an incompatible snippets_fts definition.");
        }
    }

    private static void ValidateForeignKeys(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check";
        using SqliteDataReader reader = command.ExecuteReader();
        if (reader.Read())
        {
            throw new DatabaseValidationException(
                "The selected database contains invalid foreign-key references.");
        }
    }

    private static int ReadInt(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        if (value is null || value is DBNull)
        {
            throw new DatabaseValidationException(
                "The selected database does not contain a valid schema version.");
        }

        return Convert.ToInt32(value);
    }
}

public sealed record DatabaseValidationResult(int UserVersion, int SchemaVersion);

public sealed class DatabaseValidationException : Exception
{
    public DatabaseValidationException(string message) : base(message)
    {
    }

    public DatabaseValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

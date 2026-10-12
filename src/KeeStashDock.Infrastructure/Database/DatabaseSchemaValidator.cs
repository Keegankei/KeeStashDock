using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Storage;
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

            if (schemaVersion >= 4) ValidateImages(connection);

            if (requireCurrentUserVersion &&
                (userVersion != MigrationRunner.LatestSupportedVersion ||
                 schemaVersion != MigrationRunner.LatestSupportedVersion))
            {
                throw new DatabaseValidationException(
                    $"Database migration did not reach schema " +
                    $"{MigrationRunner.LatestSupportedVersion}.");
            }

            // Legacy migrations repair historical timestamp formats first. Validate the
            // resulting values before installing a package that the application must read.
            if (requireCurrentUserVersion) ValidateEntryValues(connection);

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
        catch (Exception exception) when (exception is SqliteException or InvalidCastException or OverflowException or FormatException)
        {
            throw new DatabaseValidationException(
                "The selected file is not a valid, readable KeeStashDock database.",
                exception);
        }
    }

    private static void ValidateEntryValues(SqliteConnection connection)
    {
        using (var invalid = connection.CreateCommand())
        {
            invalid.CommandText = """
                SELECT COUNT(*) FROM snippets WHERE
                    typeof(content) != 'text' OR typeof(content_hash) != 'text' OR
                    (title IS NOT NULL AND typeof(title) != 'text') OR
                    typeof(type) != 'integer' OR type NOT BETWEEN 0 AND 5 OR
                    typeof(source) != 'integer' OR source NOT BETWEEN 0 AND 5 OR
                    typeof(expire_policy) != 'integer' OR expire_policy NOT BETWEEN 0 AND 3 OR
                    typeof(is_pinned) != 'integer' OR is_pinned NOT IN (0, 1) OR
                    typeof(sort_weight) != 'integer' OR sort_weight NOT BETWEEN -2147483648 AND 2147483647 OR
                    typeof(use_count) != 'integer' OR use_count NOT BETWEEN 0 AND 2147483647
                """;
            if (Convert.ToInt64(invalid.ExecuteScalar()) != 0)
                throw new DatabaseValidationException("The database contains unreadable entry values.");
            invalid.CommandText = """
                SELECT COUNT(*) FROM categories WHERE
                    typeof(icon) != 'text' OR typeof(color) != 'text' OR
                    typeof(sort_order) != 'integer' OR sort_order NOT BETWEEN -2147483648 AND 2147483647
                """;
            if (Convert.ToInt64(invalid.ExecuteScalar()) != 0)
                throw new DatabaseValidationException("The database contains unreadable group values.");
        }
        using var timestamps = connection.CreateCommand();
        timestamps.CommandText = "SELECT created_at, updated_at, expire_at, deleted_at, last_used_at FROM snippets";
        using var rows = timestamps.ExecuteReader();
        while (rows.Read())
        {
            for (int column = 0; column < rows.FieldCount; column++)
            {
                if (column >= 2 && rows.IsDBNull(column)) continue;
                if (rows.IsDBNull(column) || rows.GetValue(column) is not string value ||
                    !UtcTimestamp.TryNormalize(value, out _))
                    throw new DatabaseValidationException("The database contains an unreadable entry timestamp.");
            }
        }
    }

    private static void ValidateImages(SqliteConnection connection)
    {
        ValidateColumns(connection, "snippet_images", ["snippet_id", "png", "thumbnail_png", "width", "height"]);
        using (var foreignKey = connection.CreateCommand())
        {
            foreignKey.CommandText = "PRAGMA foreign_key_list(snippet_images)";
            using var rows = foreignKey.ExecuteReader();
            bool valid = false;
            while (rows.Read())
                valid |= rows.GetString(2) == "snippets" && rows.GetString(3) == "snippet_id" &&
                         rows.GetString(4) == "id" && rows.GetString(6).Equals("CASCADE", StringComparison.OrdinalIgnoreCase);
            if (!valid) throw new DatabaseValidationException("Image assets lack cascading snippet ownership.");
        }
        using (var missing = connection.CreateCommand())
        {
            missing.CommandText = """
                SELECT COUNT(*) FROM snippets s LEFT JOIN snippet_images i ON i.snippet_id = s.id
                WHERE (s.type = 5 AND i.snippet_id IS NULL) OR (s.type != 5 AND i.snippet_id IS NOT NULL)
                """;
            if (Convert.ToInt64(missing.ExecuteScalar()) != 0)
                throw new DatabaseValidationException("An image entry is missing its original image or has an incompatible type.");
        }
        using (var lengths = connection.CreateCommand())
        {
            lengths.CommandText = """
                SELECT COUNT(*) FROM snippet_images WHERE
                  typeof(png) != 'blob' OR typeof(thumbnail_png) != 'blob' OR
                  length(png) NOT BETWEEN 1 AND 50331648 OR
                  length(thumbnail_png) NOT BETWEEN 1 AND 1048576
                """;
            if (Convert.ToInt64(lengths.ExecuteScalar()) != 0)
                throw new DatabaseValidationException("An image asset exceeds the supported size or has an invalid format.");
        }
        using var read = connection.CreateCommand();
        read.CommandText = """
            SELECT i.png, i.thumbnail_png, i.width, i.height, s.content_hash
            FROM snippet_images i JOIN snippets s ON s.id = i.snippet_id
            """;
        using var images = read.ExecuteReader();
        while (images.Read())
        {
            try
            {
                byte[] png = (byte[])images.GetValue(0);
                var dimensions = PngImageValidator.Validate(png);
                PngImageValidator.Validate((byte[])images.GetValue(1), thumbnail: true);
                if (dimensions.Width != images.GetInt32(2) || dimensions.Height != images.GetInt32(3) ||
                    PngImageValidator.ImageHash(png) != images.GetString(4))
                    throw new InvalidDataException("Image dimensions or identity do not match the original PNG.");
            }
            catch (Exception exception) when (exception is InvalidDataException or InvalidCastException or OverflowException)
            {
                throw new DatabaseValidationException("The database contains a damaged or unsupported image asset.", exception);
            }
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

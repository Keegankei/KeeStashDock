using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeeStashDock.Infrastructure.Updates;

public sealed record UpdatePackageFile(string Path, long Length, string Sha256);

/// <summary>Files shipped by the application, excluding the manifest itself.</summary>
public sealed record UpdatePackageManifest(int Format, string Version, IReadOnlyList<UpdatePackageFile> Files)
{
    public const string FileName = "update-manifest.json";
    public const long MaximumExpandedBytes = 2L * 1024 * 1024 * 1024;
    public const long MaximumFileBytes = 512L * 1024 * 1024;
    public const int MaximumFileCount = 20_000;
    public const int MaximumManifestBytes = 8 * 1024 * 1024;
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 12,
    };

    public static UpdatePackageManifest Load(string packageDirectory)
    {
        string path = UpdatePathPolicy.ResolveUnderRoot(packageDirectory, FileName);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 or > MaximumManifestBytes)
            throw new InvalidDataException("The package ownership manifest is missing or exceeds its size limit.");
        UpdatePackageManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<UpdatePackageManifest>(File.ReadAllBytes(path), JsonOptions)
                ?? throw new InvalidDataException("The package ownership manifest is empty.");
        }
        catch (JsonException ex) { throw new InvalidDataException("The package ownership manifest is invalid.", ex); }
        manifest.Validate();
        return manifest;
    }

    public void Validate()
    {
        if (Format != 1) throw new InvalidDataException("Unsupported package ownership manifest format.");
        ReleaseUpdateService.ParseVersion(Version);
        if (Files is null || Files.Count is < 1 or > MaximumFileCount)
            throw new InvalidDataException("The package ownership file count is invalid.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (UpdatePackageFile file in Files)
        {
            if (file is null) throw new InvalidDataException("The manifest contains an empty file record.");
            string path = UpdatePathPolicy.ValidateRelativePath(file.Path);
            if (path.Equals(FileName, StringComparison.OrdinalIgnoreCase) || !paths.Add(path))
                throw new InvalidDataException("The manifest contains a duplicate or self-referential path.");
            if (file.Length < 0 || file.Length > MaximumFileBytes ||
                file.Sha256 is null || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("The manifest contains an invalid file length or SHA-256 digest.");
            total = checked(total + file.Length);
            if (total > MaximumExpandedBytes) throw new InvalidDataException("The expanded package exceeds its size limit.");
        }
        if (!paths.Contains("KeeStashDock.exe"))
            throw new InvalidDataException("The package does not contain KeeStashDock.exe.");
        foreach (string path in paths)
        {
            int separator = path.IndexOf('/');
            while (separator >= 0)
            {
                if (paths.Contains(path[..separator]))
                    throw new InvalidDataException("The manifest contains conflicting file and directory paths.");
                separator = path.IndexOf('/', separator + 1);
            }
        }
    }

    public void VerifyFiles(string packageDirectory, CancellationToken cancellationToken = default)
    {
        Validate();
        UpdatePathPolicy.EnsureNoReparsePoints(packageDirectory);
        foreach (UpdatePackageFile file in Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = UpdatePathPolicy.ResolveUnderRoot(packageDirectory, file.Path);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != file.Length)
                throw new InvalidDataException($"The package file has an unexpected length: {file.Path}");
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[128 * 1024];
            int count;
            while ((count = input.Read(buffer)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                hash.AppendData(buffer, 0, count);
            }
            string digest = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (!digest.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The package file failed SHA-256 verification: {file.Path}");
        }
    }
}

/// <summary>One Windows path policy shared by staging and installation.</summary>
public static class UpdatePathPolicy
{
    private static readonly HashSet<string> ProtectedRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "Data", "AppData", "Logs", "Backups", "Drafts", "Screenshots",
        "settings.json", "credentials.json", "secrets.json", "draft.txt",
        "keestashdock.db", "keestashdock.db-wal", "keestashdock.db-shm",
        "snippets.db", "snippets.db-wal", "snippets.db-shm",
        ".git", ".codex", ".agents", ".aws", ".ssh", ".env",
    };

    public static string ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 512 || path.Contains('\\') ||
            path.StartsWith('/') || path.Any(character => character < 32 || "<>:\"|?*".Contains(character)))
            throw new InvalidDataException("An update path is not a safe relative Windows path.");
        string[] segments = path.Split('/');
        if (ProtectedRoots.Contains(segments[0]))
            throw new InvalidDataException("An update package must not own user data or repository metadata.");
        foreach (string segment in segments)
        {
            if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' '))
                throw new InvalidDataException("An update path contains an ambiguous or traversing segment.");
            string stem = segment.Split('.')[0];
            if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) || stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase) ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                                     stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                 (char.IsDigit(stem[3]) || stem[3] is '¹' or '²' or '³')))
                throw new InvalidDataException("An update path contains a reserved Windows device name.");
        }
        return path;
    }

    public static bool IsPreservedAsset(string relative)
    {
        string path = ValidateRelativePath(relative);
        return path.Equals("x64/tesseract.exe", StringComparison.OrdinalIgnoreCase) ||
               path.Equals("x64/tesseract55.dll", StringComparison.OrdinalIgnoreCase) ||
               path.Equals("x64/leptonica-1.85.0.dll", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("OfflineTranslation/runtime/", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("OfflineTranslation/models/", StringComparison.OrdinalIgnoreCase) ||
               (path.StartsWith("OcrModels/", StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith(".traineddata", StringComparison.OrdinalIgnoreCase));
    }

    public static string ResolveUnderRoot(string root, string relative)
    {
        string path = ValidateRelativePath(relative);
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string resolved = Path.GetFullPath(Path.Combine(fullRoot, path.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("An update path escapes its selected directory.");
        EnsureNoReparsePoints(resolved);
        return resolved;
    }

    public static void EnsureNoReparsePoints(string path)
    {
        string? current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Update directories and files must not contain symbolic links or junctions.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            current = Path.GetDirectoryName(current);
        }
    }
}

using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KeeStashDock.Infrastructure.Updates;

public sealed record ReleaseUpdateInfo(string Version, string Notes, Uri ReleaseUrl,
    Uri ArchiveUrl, Uri ChecksumUrl, long? ArchiveSize);
public sealed record StagedUpdate(string PackageDirectory, UpdatePackageManifest Manifest);

public enum UpdateFailure
{
    Network, RateLimited, MissingRelease, InvalidMetadata, MissingAssets,
    UnsupportedVersion, ChecksumMismatch, UnsafePackage, InsufficientSpace,
}

public sealed class UpdateException(UpdateFailure failure, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public UpdateFailure Failure { get; } = failure;
}

/// <summary>
/// User-requested checks against one official release source. Discovery and
/// staging never modify the installed application or start an installer.
/// </summary>
public sealed class ReleaseUpdateService : IDisposable
{
    public const string RepositoryUrl = "https://github.com/Keegankei/KeeStashDock";
    public const string ReleaseApiUrl = "https://api.github.com/repos/Keegankei/KeeStashDock/releases/latest";
    public const long MaximumArchiveBytes = 512L * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly TimeSpan _downloadTimeout;

    public ReleaseUpdateService(HttpClient? httpClient = null, TimeSpan? downloadTimeout = null)
    {
        _downloadTimeout = downloadTimeout ?? TimeSpan.FromMinutes(15);
        if (_downloadTimeout <= TimeSpan.Zero || _downloadTimeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(downloadTimeout));
        _ownsHttp = httpClient is null;
        _http = httpClient ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromMinutes(15) };
    }

    public async Task<ReleaseUpdateInfo?> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        Version installed = ParseVersion(currentVersion);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            using HttpResponseMessage response = await SendAsync(new Uri(ReleaseApiUrl), metadata: true, deadline.Token).ConfigureAwait(false);
            byte[] payload = await ReadBoundedAsync(response, 2 * 1024 * 1024, deadline.Token).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 32 });
            JsonElement root = document.RootElement;
            if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
            string tag = RequiredString(root, "tag_name");
            string version = tag.StartsWith('v') ? tag[1..] : tag;
            Version available = ParseVersion(version);
            if (available <= installed) return null;
            string archiveName = ArchiveName(version);
            string prefix = RepositoryUrl + "/releases/download/" + Uri.EscapeDataString(tag) + "/";
            JsonElement[] archives = root.GetProperty("assets").EnumerateArray()
                .Where(asset => RequiredString(asset, "name") == archiveName).ToArray();
            JsonElement[] checksums = root.GetProperty("assets").EnumerateArray()
                .Where(asset => RequiredString(asset, "name") == archiveName + ".sha256").ToArray();
            if (archives.Length != 1 || checksums.Length != 1)
                throw new UpdateException(UpdateFailure.MissingAssets, "The release does not contain one Windows x64 archive and its matching SHA-256 file.");
            Uri archive = ExactUri(RequiredString(archives[0], "browser_download_url"), prefix + archiveName);
            Uri checksum = ExactUri(RequiredString(checksums[0], "browser_download_url"), prefix + archiveName + ".sha256");
            Uri releasePage = ExactUri(RequiredString(root, "html_url"), RepositoryUrl + "/releases/tag/" + Uri.EscapeDataString(tag));
            long? size = archives[0].TryGetProperty("size", out JsonElement sizeElement) && sizeElement.TryGetInt64(out long length)
                ? length : null;
            if (size is <= 0 or > MaximumArchiveBytes)
                throw new UpdateException(UpdateFailure.InvalidMetadata, "The release archive size is invalid or exceeds the supported limit.");
            string notes = root.TryGetProperty("body", out JsonElement body) && body.ValueKind == JsonValueKind.String
                ? body.GetString() ?? "" : "";
            return new(version, notes, releasePage, archive, checksum, size);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        { throw new UpdateException(UpdateFailure.Network, "The update check timed out. Retry or open the release page.", ex); }
        catch (HttpRequestException ex)
        { throw new UpdateException(UpdateFailure.Network, "The official release source could not be reached.", ex); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { throw new UpdateException(UpdateFailure.InvalidMetadata, "The release metadata is not a supported GitHub release response.", ex); }
    }

    public async Task<StagedUpdate> DownloadAsync(ReleaseUpdateInfo info, string stagingRoot,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        // ResponseHeadersRead completes SendAsync after headers; HttpClient's
        // timeout alone does not cover a stalled response body or extraction.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_downloadTimeout);
        CancellationToken downloadToken = deadline.Token;
        ParseVersion(info.Version);
        string archiveName = ArchiveName(info.Version);
        // Revalidate caller-provided metadata instead of trusting a deserialized
        // or cached ReleaseUpdateInfo to choose an arbitrary download source.
        string tag = info.ReleaseUrl.AbsolutePath.Split('/').Last();
        string expectedVersion = tag.StartsWith('v') ? tag[1..] : tag;
        if (expectedVersion != info.Version)
            throw new UpdateException(UpdateFailure.InvalidMetadata, "The release tag does not match the requested version.");
        ExactUri(info.ReleaseUrl.AbsoluteUri, RepositoryUrl + "/releases/tag/" + Uri.EscapeDataString(tag));
        string assetPrefix = RepositoryUrl + "/releases/download/" + Uri.EscapeDataString(tag) + "/";
        ExactUri(info.ArchiveUrl.AbsoluteUri, assetPrefix + archiveName);
        ExactUri(info.ChecksumUrl.AbsoluteUri, assetPrefix + archiveName + ".sha256");
        if (info.ArchiveSize is <= 0 or > MaximumArchiveBytes)
            throw new UpdateException(UpdateFailure.InvalidMetadata, "The update archive size is outside supported limits.");

        string root = Path.GetFullPath(stagingRoot);
        UpdatePathPolicy.EnsureNoReparsePoints(root);
        Directory.CreateDirectory(root);
        string work = Path.Combine(root, "stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        string archivePath = Path.Combine(work, "download.zip");
        try
        {
            downloadToken.ThrowIfCancellationRequested();
            progress?.Report(0);
            string expectedDigest;
            using (HttpResponseMessage checksumResponse = await SendAsync(info.ChecksumUrl, metadata: false, downloadToken).ConfigureAwait(false))
            {
                byte[] payload = await ReadBoundedAsync(checksumResponse, 4096, downloadToken).ConfigureAwait(false);
                expectedDigest = ParseChecksum(Encoding.UTF8.GetString(payload), archiveName);
            }
            EnsureSpace(work, (info.ArchiveSize ?? MaximumArchiveBytes) + 16 * 1024 * 1024);
            using (HttpResponseMessage response = await SendAsync(info.ArchiveUrl, metadata: false, downloadToken).ConfigureAwait(false))
            {
                long? declaredLength = response.Content.Headers.ContentLength;
                if (declaredLength is <= 0 or > MaximumArchiveBytes ||
                    (declaredLength.HasValue && info.ArchiveSize.HasValue && declaredLength.Value != info.ArchiveSize.Value))
                    throw new UpdateException(UpdateFailure.InvalidMetadata, "The downloaded archive length differs from the release metadata.");
                await using Stream input = await response.Content.ReadAsStreamAsync(downloadToken).ConfigureAwait(false);
                await using var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[128 * 1024];
                long total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, downloadToken).ConfigureAwait(false)) > 0)
                {
                    total += count;
                    if (total > MaximumArchiveBytes || (info.ArchiveSize.HasValue && total > info.ArchiveSize.Value))
                        throw new UpdateException(UpdateFailure.InvalidMetadata, "The download exceeded its permitted size.");
                    await output.WriteAsync(buffer.AsMemory(0, count), downloadToken).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, count);
                    if (info.ArchiveSize is > 0) progress?.Report(0.8 * total / info.ArchiveSize.Value);
                }
                if ((info.ArchiveSize.HasValue && total != info.ArchiveSize.Value) ||
                    (declaredLength.HasValue && total != declaredLength.Value))
                    throw new UpdateException(UpdateFailure.InvalidMetadata, "The update archive download was truncated.");
                if (!Convert.ToHexStringLower(hash.GetHashAndReset()).Equals(expectedDigest, StringComparison.OrdinalIgnoreCase))
                    throw new UpdateException(UpdateFailure.ChecksumMismatch, "The archive failed SHA-256 verification. No installed files were changed.");
            }
            string package = Path.Combine(work, "package");
            Directory.CreateDirectory(package);
            UpdatePackageManifest manifest = await Task.Run(() => ExtractPackage(archivePath, package, info.Version, progress, downloadToken),
                downloadToken).ConfigureAwait(false);
            File.Delete(archivePath);
            progress?.Report(1);
            return new(package, manifest);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            DeleteFailedStage(root, work);
            throw new UpdateException(UpdateFailure.Network, "The update download timed out. Retry or open the release page.", ex);
        }
        catch (HttpRequestException ex)
        {
            DeleteFailedStage(root, work);
            throw new UpdateException(UpdateFailure.Network, "The update download failed. Retry or open the release page.", ex);
        }
        catch
        {
            DeleteFailedStage(root, work);
            throw;
        }
    }

    /// <summary>
    /// Removes an unused, unchanged package created by this downloader. Callers
    /// must retain stages once handed to an installer, including failed starts.
    /// Unknown, changed, linked, or misplaced content is deliberately preserved.
    /// </summary>
    public static bool DiscardStage(StagedUpdate stage, string stagingRoot)
    {
        try
        {
            string root = Path.GetFullPath(stagingRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string package = Path.GetFullPath(stage.PackageDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string? work = Path.GetDirectoryName(package);
            if (work is null || !Path.GetFileName(package).Equals("package", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetDirectoryName(work), root, StringComparison.OrdinalIgnoreCase)) return false;
            string name = Path.GetFileName(work);
            if (!name.StartsWith("stage-", StringComparison.Ordinal) || !Guid.TryParseExact(name[6..], "N", out _)) return false;
            UpdatePathPolicy.EnsureNoReparsePoints(package);
            if (!Directory.Exists(work)) return true;
            if (!Directory.Exists(package)) return false;
            string[] workEntries = Directory.GetFileSystemEntries(work);
            if (workEntries.Length != 1 || !workEntries[0].Equals(package, StringComparison.OrdinalIgnoreCase)) return false;

            stage.Manifest.Validate();
            UpdatePackageManifest actual = UpdatePackageManifest.Load(package);
            if (actual.Format != stage.Manifest.Format || actual.Version != stage.Manifest.Version ||
                actual.Files.Count != stage.Manifest.Files.Count) return false;
            var expected = stage.Manifest.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
            if (actual.Files.Any(file => !expected.TryGetValue(file.Path, out UpdatePackageFile? original) ||
                original.Length != file.Length || !original.Sha256.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))) return false;

            var files = new HashSet<string>(expected.Keys, StringComparer.OrdinalIgnoreCase) { UpdatePackageManifest.FileName };
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in expected.Keys)
            {
                int slash = file.LastIndexOf('/');
                while (slash >= 0)
                {
                    directories.Add(file[..slash]);
                    slash = file.LastIndexOf('/', slash - 1);
                }
            }
            var pending = new Stack<string>(); pending.Push(package);
            var visitedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visitedDirectories = new List<string>();
            while (pending.TryPop(out string? directory))
            {
                UpdatePathPolicy.EnsureNoReparsePoints(directory);
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    UpdatePathPolicy.EnsureNoReparsePoints(entry);
                    string relative = Path.GetRelativePath(package, entry).Replace(Path.DirectorySeparatorChar, '/');
                    if (Directory.Exists(entry))
                    {
                        if (!directories.Contains(relative)) return false;
                        visitedDirectories.Add(entry); pending.Push(entry);
                    }
                    else if (!files.Contains(relative) || !visitedFiles.Add(relative)) return false;
                }
            }
            if (!files.SetEquals(visitedFiles)) return false;
            actual.VerifyFiles(package);
            // Never recurse during deletion. A newly added file or directory
            // makes the final empty-directory deletion fail and is retained.
            foreach (string relative in visitedFiles)
                File.Delete(UpdatePathPolicy.ResolveUnderRoot(package, relative));
            foreach (string directory in visitedDirectories.OrderByDescending(path => path.Length))
            {
                UpdatePathPolicy.EnsureNoReparsePoints(directory);
                Directory.Delete(directory);
            }
            UpdatePathPolicy.EnsureNoReparsePoints(package);
            Directory.Delete(package);
            UpdatePathPolicy.EnsureNoReparsePoints(work);
            Directory.Delete(work);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
            or UpdateException or ArgumentException or OverflowException)
        { return false; }
    }

    private static UpdatePackageManifest ExtractPackage(string archivePath, string package,
        string version, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            if (archive.Entries.Count is < 2 or > UpdatePackageManifest.MaximumFileCount + 512)
                throw new InvalidDataException("The archive entry count is invalid.");
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool directory = entry.FullName.EndsWith('/');
                string name = UpdatePathPolicy.ValidateRelativePath(directory ? entry.FullName[..^1] : entry.FullName);
                if (!entries.TryAdd(name, entry)) throw new InvalidDataException("The archive contains duplicate paths.");
                int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
                if ((entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 ||
                    (unixType != 0 && unixType != (directory ? 0x4000 : 0x8000)))
                    throw new InvalidDataException("The archive contains a link or unsupported entry type.");
                if (directory && entry.Length != 0) throw new InvalidDataException("An archive directory contains file content.");
                if (entry.Length < 0 || entry.Length > UpdatePackageManifest.MaximumFileBytes)
                    throw new InvalidDataException("An archive entry exceeds the supported size limit.");
                total = checked(total + entry.Length);
                if (total > UpdatePackageManifest.MaximumExpandedBytes)
                    throw new InvalidDataException("The expanded archive exceeds the supported size limit.");
            }
            foreach (string name in entries.Keys)
            {
                int separator = name.IndexOf('/');
                while (separator >= 0)
                {
                    if (entries.TryGetValue(name[..separator], out ZipArchiveEntry? parent) && !parent.FullName.EndsWith('/'))
                        throw new InvalidDataException("The archive contains conflicting file and directory paths.");
                    separator = name.IndexOf('/', separator + 1);
                }
            }
            if (!entries.TryGetValue(UpdatePackageManifest.FileName, out ZipArchiveEntry? manifestEntry) ||
                manifestEntry.Length is <= 0 or > UpdatePackageManifest.MaximumManifestBytes)
                throw new InvalidDataException("The archive ownership manifest is missing or too large.");
            EnsureSpace(package, total + 16 * 1024 * 1024);
            ExtractFile(manifestEntry, UpdatePathPolicy.ResolveUnderRoot(package, UpdatePackageManifest.FileName), cancellationToken);
            UpdatePackageManifest manifest = UpdatePackageManifest.Load(package);
            if (manifest.Version != version) throw new InvalidDataException("The manifest version differs from the release version.");
            var declared = new HashSet<string>(manifest.Files.Select(file => file.Path), StringComparer.OrdinalIgnoreCase);
            foreach ((string name, ZipArchiveEntry entry) in entries)
            {
                if (!entry.FullName.EndsWith('/') && name != UpdatePackageManifest.FileName && !declared.Contains(name))
                    throw new InvalidDataException("The archive contains a file not declared in its ownership manifest.");
            }
            int extracted = 0;
            foreach (UpdatePackageFile file in manifest.Files)
            {
                if (!entries.TryGetValue(file.Path, out ZipArchiveEntry? entry) || entry.FullName.EndsWith('/') || entry.Length != file.Length)
                    throw new InvalidDataException("The archive does not match its ownership manifest.");
                string destination = UpdatePathPolicy.ResolveUnderRoot(package, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                ExtractFile(entry, destination, cancellationToken);
                progress?.Report(0.8 + 0.15 * ++extracted / manifest.Files.Count);
            }
            manifest.VerifyFiles(package, cancellationToken);
            return manifest;
        }
        catch (Exception ex) when (ex is InvalidDataException or OverflowException)
        { throw new UpdateException(UpdateFailure.UnsafePackage, "The update package failed structure or ownership verification. No installed files were changed.", ex); }
    }

    private static void ExtractFile(ZipArchiveEntry entry, string destination, CancellationToken cancellationToken)
    {
        using Stream input = entry.Open();
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        byte[] buffer = new byte[128 * 1024];
        long total = 0;
        int count;
        while ((count = input.Read(buffer)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            total += count;
            if (total > entry.Length) throw new InvalidDataException("An archive stream exceeds its declared length.");
            output.Write(buffer, 0, count);
        }
        if (total != entry.Length) throw new InvalidDataException("An archive entry is truncated.");
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, bool metadata, CancellationToken cancellationToken)
    {
        for (int redirect = 0; redirect <= 5; redirect++)
        {
            ValidateTransportUri(uri, metadata);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("KeeStashDock-Updater/1.2.2");
            request.Headers.Accept.ParseAdd(metadata ? "application/vnd.github+json" : "application/octet-stream");
            HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            try
            {
                // Also reject an injected client's unexpected automatic redirect.
                if (response.RequestMessage?.RequestUri is { } finalUri) ValidateTransportUri(finalUri, metadata);
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
                {
                    Uri? location = response.Headers.Location;
                    if (location is null || redirect == 5) throw new UpdateException(UpdateFailure.Network, "The release download has too many or invalid redirects.");
                    uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                    response.Dispose();
                    continue;
                }
                if (response.StatusCode == HttpStatusCode.TooManyRequests ||
                    (response.StatusCode == HttpStatusCode.Forbidden && response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.Contains("0")))
                    throw new UpdateException(UpdateFailure.RateLimited, "GitHub temporarily limited update requests. Retry later or open the release page.");
                if (metadata && response.StatusCode == HttpStatusCode.NotFound)
                    throw new UpdateException(UpdateFailure.MissingRelease, "The official source has no available stable release.");
                response.EnsureSuccessStatusCode();
                return response;
            }
            catch { response.Dispose(); throw; }
        }
        throw new UpdateException(UpdateFailure.Network, "The release download could not be resolved.");
    }

    private static void ValidateTransportUri(Uri uri, bool metadata)
    {
        bool allowed = uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 &&
            uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;
        if (metadata) allowed &= uri.AbsoluteUri == ReleaseApiUrl;
        else allowed &= (uri.Host == "github.com" && uri.AbsolutePath.StartsWith("/Keegankei/KeeStashDock/releases/download/", StringComparison.Ordinal)) ||
                        uri.Host is "release-assets.githubusercontent.com" or "objects.githubusercontent.com";
        if (!allowed) throw new UpdateException(UpdateFailure.InvalidMetadata, "An update URL is outside the official HTTPS release source.");
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, int maximumBytes, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > maximumBytes)
            throw new UpdateException(UpdateFailure.InvalidMetadata, "The release response exceeds its size limit.");
        using var output = new MemoryStream();
        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        byte[] buffer = new byte[16 * 1024];
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (output.Length + count > maximumBytes)
                throw new UpdateException(UpdateFailure.InvalidMetadata, "The release response exceeds its size limit.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private static string ParseChecksum(string content, string archiveName)
    {
        string line = content.Trim().TrimStart('\uFEFF');
        Match match = Regex.Match(line, @"\A([a-fA-F0-9]{64})[ \t]+\*?([^\r\n]+)\z", RegexOptions.CultureInvariant);
        if (!match.Success || match.Groups[2].Value != archiveName)
            throw new UpdateException(UpdateFailure.InvalidMetadata, "The SHA-256 file does not name the expected archive.");
        return match.Groups[1].Value;
    }

    public static Version ParseVersion(string value)
    {
        string normalized = (value ?? "").Split('+')[0];
        if (normalized.StartsWith('v')) normalized = normalized[1..];
        if (!Regex.IsMatch(normalized, @"\A(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:\.(0|[1-9]\d*))?\z", RegexOptions.CultureInvariant) ||
            !System.Version.TryParse(normalized, out Version? parsed))
            throw new UpdateException(UpdateFailure.UnsupportedVersion, "The version is not a supported stable release version.");
        return new Version(parsed.Major, parsed.Minor, parsed.Build, Math.Max(0, parsed.Revision));
    }

    private static string ArchiveName(string version) => $"KeeStashDock-{version}-win-x64.zip";
    private static Uri ExactUri(string candidate, string expected)
    {
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) || uri.AbsoluteUri != expected)
            throw new UpdateException(UpdateFailure.InvalidMetadata, "A release link does not match the official versioned asset.");
        return uri;
    }
    private static string RequiredString(JsonElement element, string property)
        => element.GetProperty(property).GetString() ?? throw new JsonException($"Missing release field: {property}");

    private static void EnsureSpace(string directory, long bytes)
    {
        string? drive = Path.GetPathRoot(Path.GetFullPath(directory));
        if (drive is not null && new DriveInfo(drive).AvailableFreeSpace < bytes)
            throw new UpdateException(UpdateFailure.InsufficientSpace, "There is not enough free space to stage this update.");
    }

    private static void DeleteFailedStage(string root, string work)
    {
        try
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            string fullWork = Path.GetFullPath(work);
            if (!fullWork.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
            UpdatePathPolicy.EnsureNoReparsePoints(fullWork);
            if (Directory.Exists(fullWork)) Directory.Delete(fullWork, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { /* A locked staging copy is retained; the installed app remains untouched. */ }
    }

    public void Dispose() { if (_ownsHttp) _http.Dispose(); }
}

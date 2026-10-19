using System.ComponentModel;
using System.IO;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Updates;

namespace KeeStashDock.Desktop;

public sealed partial class ComponentInstallService
{
    private const string DirectoryMarker = ".keestashdock-component";
    private AppSettings? Configuration => _settings?.Current ?? _configuration;

    /// <summary>Installation destination, without creating it. OCR stores its model and engine subfolders here.</summary>
    public string GetInstallDirectory(ComponentKind component) => component switch
    {
        ComponentKind.Ocr => GetCustomDirectory(component) ?? _localDataDirectory,
        ComponentKind.OfflineTranslation => OfflineRuntimeDirectory,
        _ => throw new ArgumentOutOfRangeException(nameof(component)),
    };

    /// <summary>Allows Settings to show an invalid stored value and offer Reset without failing to open.</summary>
    public bool TryGetInstallDirectory(ComponentKind component, out string directory)
    {
        try { directory = GetInstallDirectory(component); return true; }
        catch (ArgumentException)
        {
            directory = (component == ComponentKind.Ocr ? Configuration?.OcrInstallDirectory
                : Configuration?.OfflineTranslationInstallDirectory) ?? string.Empty;
            return false;
        }
    }

    internal string GetOcrModelsDirectory() => GetCustomDirectory(ComponentKind.Ocr) is { } custom
        ? Path.Combine(custom, "OcrModels")
        : ResolveOcrModelsDirectory(_baseDirectory, _localDataDirectory, _hasOcrModels);

    internal string GetOcrEngineDirectory() => GetCustomDirectory(ComponentKind.Ocr) is { } custom
        ? Path.Combine(custom, "OcrEngine")
        : ResolveOcrEngineDirectory(_baseDirectory, _localDataDirectory, _hasOcrEngine);

    private string? GetCustomDirectory(ComponentKind component)
    {
        string? configured = component == ComponentKind.Ocr
            ? Configuration?.OcrInstallDirectory : Configuration?.OfflineTranslationInstallDirectory;
        if (!TryNormalizeInstallDirectory(configured, out string? normalized))
            throw new ArgumentException("Choose an absolute local folder for optional components.");
        return normalized;
    }

    /// <summary>Blank resets the location. Relative, network, device and root paths are not executable runtime locations.</summary>
    public static bool TryNormalizeInstallDirectory(string? value, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        try
        {
            string path = value.Trim().Replace('/', '\\');
            if (path.Length < 4 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\' ||
                !Path.IsPathFullyQualified(path)) return false;
            string[] segments = path[3..].TrimEnd('\\').Split('\\');
            foreach (string segment in segments)
            {
                if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ') ||
                    segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
                string stem = segment.Split('.')[0].ToUpperInvariant();
                if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
                    stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                    (char.IsDigit(stem[3]) || stem[3] is '\u00B9' or '\u00B2' or '\u00B3')) return false;
            }
            path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (File.Exists(path)) return false;
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(windows) && IsWithin(path, windows)) return false;
            normalized = path;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        { return false; }
    }

    /// <summary>
    /// Selects a destination without moving or deleting old files. Stop the old managed translation
    /// process before committing its new location. The caller's UI context owns SettingsStore events.
    /// </summary>
    public async Task<ComponentInstallResult> ChangeInstallDirectoryAsync(ComponentKind component, string? directory,
        CancellationToken cancellationToken = default)
    {
        if (_settings is null || component is not (ComponentKind.Ocr or ComponentKind.OfflineTranslation) ||
            !TryNormalizeInstallDirectory(directory, out string? normalized))
            return new(ComponentInstallFailure.Failed, "Choose an absolute local folder for optional components.");
        bool entered = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            await OperationGate.WaitAsync(deadline.Token);
            entered = true;
            string? oldSetting = component == ComponentKind.Ocr
                ? _settings.Current.OcrInstallDirectory : _settings.Current.OfflineTranslationInstallDirectory;
            if (string.Equals(oldSetting, normalized, StringComparison.OrdinalIgnoreCase))
                return new(ComponentInstallFailure.None);
            if (normalized is not null)
                await Task.Run(() =>
                {
                    ValidateCustomDirectory(component, normalized, deadline.Token);
                    ProbeWritableParent(normalized, deadline.Token);
                }, deadline.Token);
            if (component == ComponentKind.OfflineTranslation && TryNormalizeInstallDirectory(oldSetting, out _))
            {
                string previous = OfflineRuntimeDirectory;
                if (IsOfflineInstalled(previous) || File.Exists(Path.Combine(previous, "service.json")))
                {
                    ComponentInstallResult stopped = await RunScriptAsync("OfflineTranslation", "Stop.ps1",
                        ["-RuntimeDirectory", previous], null, deadline.Token);
                    if (!stopped.IsSuccess) return stopped;
                }
            }
            deadline.Token.ThrowIfCancellationRequested();
            SetPath(normalized);
            try { _settings.Save(); }
            catch { SetPath(oldSetting); throw; }
            return new(ComponentInstallFailure.None);

            void SetPath(string? path)
            {
                if (component == ComponentKind.Ocr) _settings.Current.OcrInstallDirectory = path;
                else _settings.Current.OfflineTranslationInstallDirectory = path;
            }
        }
        catch (OperationCanceledException)
        { return new(cancellationToken.IsCancellationRequested ? ComponentInstallFailure.Cancelled : ComponentInstallFailure.Timeout); }
        catch (UnauthorizedAccessException) { return new(ComponentInstallFailure.AccessDenied); }
        catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException or Win32Exception)
        { return new(ComponentInstallFailure.Failed, exception.Message); }
        finally { if (entered) OperationGate.Release(); }
    }

    private static bool IsOfflineInstalled(string directory)
        => File.Exists(Path.Combine(directory, "installed.json")) && File.Exists(Path.Combine(directory, "python", "pythonw.exe"));

    private Task PrepareCustomDirectoryAsync(ComponentKind component, CancellationToken token)
    {
        if (GetCustomDirectory(component) is not { } directory) return Task.CompletedTask;
        return Task.Run(() =>
        {
            ValidateCustomDirectory(component, directory, token);
            ProbeWritableParent(directory, token);
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(directory);
            string marker = Path.Combine(directory, DirectoryMarker);
            if (!File.Exists(marker)) File.WriteAllText(marker, component.ToString());
        }, token);
    }

    private void ValidateCustomDirectory(ComponentKind component, string directory, CancellationToken token)
    {
        // Never allow selecting the application directory or a parent as a component container.
        if (IsWithin(_baseDirectory, directory))
            throw new IOException("Choose a separate folder for optional components.");
        UpdatePathPolicy.EnsureNoReparsePoints(directory);
        if (!Directory.Exists(directory)) return;
        bool empty = !Directory.EnumerateFileSystemEntries(directory).Any();
        string marker = Path.Combine(directory, DirectoryMarker);
        if (!empty)
        {
            if (File.Exists(marker))
            {
                UpdatePathPolicy.EnsureNoReparsePoints(marker);
                if (new FileInfo(marker).Length > 64 || File.ReadAllText(marker) != component.ToString())
                    throw new IOException("This folder belongs to a different component.");
            }
            else
            {
                // Reject unrelated folders before walking their contents.
                bool installed = component == ComponentKind.Ocr
                    ? _hasOcrModels(Path.Combine(directory, "OcrModels")) && _hasOcrEngine(Path.Combine(directory, "OcrEngine"))
                    : IsOfflineInstalled(directory);
                if (!installed) throw new IOException("Choose an empty folder or an existing installation of this component. Existing files will not be overwritten.");
            }
        }
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.TryPop(out string? current))
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(current))
            {
                token.ThrowIfCancellationRequested();
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Component folders must not contain symbolic links or junctions.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
        }
    }

    private static void ProbeWritableParent(string directory, CancellationToken token)
    {
        string? existing = directory;
        while (existing is not null && !Directory.Exists(existing)) existing = Path.GetDirectoryName(existing);
        if (existing is null) throw new IOException("The selected drive or folder is unavailable.");
        token.ThrowIfCancellationRequested();
        string probe = Path.Combine(existing, ".keestashdock-write-" + Guid.NewGuid().ToString("N"));
        // DeleteOnClose removes only this uniquely created probe, including on cancellation.
        using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
        {
            token.ThrowIfCancellationRequested();
        }
    }

    private static bool IsWithin(string path, string root)
    {
        string normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return path.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(normalized + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

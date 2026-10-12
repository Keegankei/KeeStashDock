using CommunityToolkit.Mvvm.ComponentModel;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Desktop.Localization;
using System.Windows.Media;

namespace KeeStashDock.Desktop.ViewModels;

/// <summary>
/// Presentation wrapper around a <see cref="Snippet"/> entity.
/// Owns display-only concerns (preview trimming, badge text, relative time)
/// so entity classes stay UI-free and cards stay cheap to virtualize.
/// </summary>
public sealed partial class SnippetItemViewModel : ObservableObject
{
    public Snippet Model { get; }

    private readonly Func<long, byte[]?>? _thumbnailLoader;
    private ImageSource? _thumbnail;
    private bool _thumbnailLoaded;

    public SnippetItemViewModel(Snippet model, Func<long, byte[]?>? thumbnailLoader = null)
    { Model = model; _thumbnailLoader = thumbnailLoader; }

    public bool IsImage => Model.Type == SnippetType.Image;
    public ImageSource? Thumbnail
    {
        get
        {
            if (_thumbnailLoaded || !IsImage) return _thumbnail;
            _thumbnailLoaded = true;
            try
            {
                if (_thumbnailLoader?.Invoke(Id) is byte[] png)
                    _thumbnail = ImagePresentation.Decode(png);
            }
            catch (Exception ex) when (ex is System.IO.IOException or NotSupportedException or ArgumentException)
            { /* An unavailable preview must not hide the entry. */ }
            return _thumbnail;
        }
    }

    public long Id => Model.Id;

    /// <summary>Card headline: explicit title, or first line of content.</summary>
    public string DisplayTitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Model.Title)) return Model.Title!;
            if (IsImage && string.IsNullOrWhiteSpace(Model.Content))
                return LocalizationService.Instance["Screenshot.Title"];
            string firstLine = Model.Content.Split('\n')[0].Trim();
            return firstLine.Length <= 60 ? firstLine : firstLine[..60] + "…";
        }
    }

    /// <summary>Two-to-three line body preview (full content lives in the workbench editor).</summary>
    public string Preview
        => Model.Content.Length <= 160 ? Model.Content : Model.Content[..160] + "…";

    /// <summary>Localized type badge, resolved live via the localization indexer.</summary>
    public string TypeBadge => LocalizationService.Instance[$"Type.{Model.Type}"];

    public bool IsPinned
    {
        get => Model.IsPinned;
        set { Model.IsPinned = value; OnPropertyChanged(); }
    }

    /// <summary>The pin toggle mutates the MODEL directly (service-side),
    /// bypassing the setter above - callers use this to wake the binding so
    /// the accent pin appears the instant of the click, without waiting for
    /// the debounced list reload.</summary>
    public void NotifyPinChanged() => OnPropertyChanged(nameof(IsPinned));

    /// <summary>Coarse relative timestamp for the card footer (local time).</summary>
    public string RelativeTime
    {
        get
        {
            var delta = DateTime.UtcNow - Model.UpdatedAt;
            if (delta.TotalMinutes < 1) return LocalizationService.Instance["Time.JustNow"];
            if (delta.TotalHours   < 1) return $"{(int)delta.TotalMinutes} min";
            if (delta.TotalDays    < 1) return $"{(int)delta.TotalHours} h";
            return Model.UpdatedAt.ToLocalTime().ToString("d");   // culture-aware short date
        }
    }
}

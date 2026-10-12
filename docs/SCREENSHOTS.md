# Screenshot entries

Screenshots are image entries. OCR adds optional text to the same entry; it does
not replace the original image or start translation automatically.

## Capture and reuse

1. Choose a group, then click the screenshot button in the quick panel or
   workbench. Each button saves to its window's selected group. **Ctrl+Alt+S**
   uses the active workbench's selected group, or the quick panel's group when
   the workbench is not active. Settings lets you change or disable this shortcut independently
   of the panel shortcut. The screenshot button remains available when the
   global shortcut is disabled or unavailable.
2. Drag a region. KeeStashDock hides its windows while capturing. **Esc** or a
   right-click cancels without saving an entry. Monitor coordinates and display
   scaling are handled in physical pixels.
3. Open the saved image card to preview its original image. Use **Copy image** or
   **Save image as** to reuse it as a PNG.
4. Click **Recognize text** when needed. English and Simplified Chinese recognition
   runs locally. Review and edit the result; saved text is searchable with the
   entry. See [model installation and OCR limits](LOCAL_OCR.md).
5. Select recognized text to use the existing action bubble. Enable the selection
   popup in Settings, select its **Translate** action, and choose your existing
   translation provider if you want to translate. OCR itself never uploads an
   image or invokes a translation service.

Recognition can be cancelled. Closing an edited image entry offers Save,
Discard, or Cancel; a failed save retains the editor text. A recognition failure
does not remove the image. Re-running recognition asks before replacing existing
text.

## Storage, backups, and upgrading

The original PNG and thumbnail live inside the SQLite database with their entry.
Database backups and full package exports therefore include both images and OCR
text. Moving an entry to Trash retains its image; restoring returns the image and
text together. Permanent deletion or trash purging removes the associated image.

The database migrates to schema version 4. Existing text entries are retained,
and older supported database packages can still be imported through Settings.
Keep a pre-upgrade backup if you may need to return to an older app version:
older binaries cannot open a schema v4 database.

Group exports use package version 2 to carry images and recognized text. This
version still reads old version 1 text packages; older app versions cannot import
new version 2 packages. A group package is limited to 128 MiB, including at most
80 MiB of original/thumbnail assets before JSON encoding. Use a full database
backup for large collections.

The first version saves one static image per entry. It does not automatically
collect clipboard images, merge multiple screenshots, capture scrolling pages,
or add drawing annotations. Clipboard history continues to collect text only.

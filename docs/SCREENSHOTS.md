# Screenshot entries

Screenshots can become image entries, or be used temporarily to recognize and
translate text. Saving an image, recognizing text, and translating it are separate
choices; translation uses the provider configured in Settings.

## Capture and reuse

1. Choose a group, then click the screenshot button in the quick panel or
   workbench. Each button saves to its window's selected group. **Alt+W**
   uses the active workbench's selected group, or the quick panel's group when
   the workbench is not active. Settings lets you change or disable this shortcut independently
   of the panel shortcut. The screenshot button remains available when the
   global shortcut is disabled or unavailable.
2. Drag a region. KeeStashDock hides its windows while capturing, then shows an
   action toolbar. Drag again to change the region. Choose **Save screenshot**
   (or press **Enter**), **Extract text**, or **Extract and translate**. **Esc** or
   a right-click cancels. Monitor coordinates and display scaling are handled in
   physical pixels. The toolbar stays within the monitor where selection ends.
3. **Save screenshot** stores an image entry in the chosen group. Open the saved
   image card to preview its original image. Use **Copy image** or
   **Save image as** to reuse it as a PNG.
4. Click **Recognize text** when needed. English and Simplified Chinese recognition
   runs locally. Review and edit the result; saved text is searchable with the
   entry. See [model installation and OCR limits](https://github.com/Keegankei/KeeStashDock/blob/main/docs/LOCAL_OCR.md).
   The same guide is included in the portable package at `OcrModels/README.md`.
5. Select recognized text to use the existing action bubble. Enable the selection
   popup in Settings, select **Translate only** or **Copy and translate**, and choose your existing
   translation provider if you want to translate. OCR itself never uploads an
   image or invokes a translation service.

Recognition can be cancelled. Closing an edited image entry offers Save,
Discard, or Cancel; a failed save retains the editor text. A recognition failure
does not remove the image. Re-running recognition asks before replacing existing
text.

## Recognize without saving

**Extract text** restores the application windows and runs local OCR on the
selected pixels. The result card shows the text; click the text to copy it.
**Extract and translate** recognizes the text first, then passes only that text
to the configured translation provider. It does not upload the screenshot.
Offline translation does not fall back to a cloud service.

These two actions do not create a database entry, retain a temporary image file,
or write to the clipboard automatically. Explicit copies are excluded from
clipboard-history capture. Translation results retain a separate **Copy source
text** action, including when the provider fails; **Retry translation** uses the
same recognized text. Close or click outside the result to dismiss it. A new
capture, selection, or translation request cancels the previous request.

Recognition progress, empty results, missing components, and failures have
separate messages. The component button opens the existing in-app installation
and repair flow for missing OCR or offline translation components.

In 1.2.3, temporary results also offer a group selector and explicit **Save source**,
**Save translation**, and **Save both** actions. The last action creates one
labeled text entry. It does not save the captured image or change the clipboard.
An existing duplicate is reused in the chosen group. A failed save leaves the
result available for retry. The translation target can be changed for this
result without changing the default target in Settings; retry always uses the
retained source text. Unsupported local model directions remain an error and
never trigger cloud fallback.

<p align="center"><img src="https://raw.githubusercontent.com/Keegankei/KeeStashDock/e4a6d71a4d35c0ee5df5cf1ea3a7a0639f59aaef/docs/images/translation-result-tools.png" width="420" alt="The English 1.2.3 result window with a target language selector, group selector and save actions"></p>

This preview uses prefilled example text in the actual 1.2.3 result window;
no OCR or translation service was run to produce it. In **Settings > Capture &
translation**, independently show or hide the target-language selector and
save-to-group controls. Both are on by default. Hiding them keeps the result
and copy actions available. See the [compact result preview](https://github.com/Keegankei/KeeStashDock/blob/e4a6d71a4d35c0ee5df5cf1ea3a7a0639f59aaef/docs/images/translation-result-compact.png)
and [image sources](https://github.com/Keegankei/KeeStashDock/blob/e4a6d71a4d35c0ee5df5cf1ea3a7a0639f59aaef/docs/images/README.md) for the optional layout and reproduction details.

## Storage, backups, and upgrading

The original PNG and thumbnail live inside the SQLite database with their entry.
Database backups and full package exports therefore include both images and OCR
text. Moving an entry to Trash retains its image; restoring returns the image and
text together. Permanent deletion or trash purging removes the associated image.

Version 1.3.0 migrates the database to schema version 5 after creating and
verifying a pre-upgrade SQLite snapshot. Existing entries are retained, and older
supported database packages can still be imported through Settings. Keep the
complete data folder and pre-upgrade snapshot: version 1.2.3 and earlier cannot
open a schema v5 database.

Group exports use package version 3 to preserve text templates as well as images
and recognized text. Version 1.3.0 still imports formats 1 and 2; importing format
3 requires version 1.3.0 or newer. A group package is limited to 128 MiB, including at most
80 MiB of original/thumbnail assets before JSON encoding. Use a full database
backup for large collections.

The first version saves one static image per entry. It does not automatically
collect clipboard images, merge multiple screenshots, capture scrolling pages,
or add drawing annotations. Clipboard history continues to collect text only.

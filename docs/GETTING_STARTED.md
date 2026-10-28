# KeeStashDock 1.3.0

Windows x64 portable package. Extract the entire ZIP into a writable folder and
run `KeeStashDock.exe`. The .NET runtime is bundled in the executable. Extract the
complete package; copying only the executable omits languages and component setup tools.

```text
KeeStashDock.exe         Application entry point
StartHere.md            This guide
update-manifest.json    Managed application files for verified upgrades
Docs/                   Screenshot and upgrade guides
Licenses/               Project license, notices, and dependency licenses
Locales/                Editable interface language packs
OcrModels/              OCR installation scripts and guide; no preinstalled engine or models
NativeRuntime/          Optional Microsoft prerequisite setup
OfflineTranslation/     Optional local translation setup and service scripts
theme/builtin/          Built-in complete light and dark theme assets
theme/custom/           Imported themes; created only when needed
Data/                   Your library and settings; created on first use
```

The .NET runtime manages its own native-library extraction cache automatically;
no manual environment setup is needed. Keep the application entry point and
`update-manifest.json` at the root of this folder.

## First use

- A fresh installation starts in **English**. Choose another interface language
  in Settings. An upgrade that keeps existing data also keeps its saved language.
- Press **Alt+Q** to show or hide the quick panel. The tray menu opens the workbench
  and Settings. Launching the executable again brings back the panel.
- Type a text entry and press **Enter** to save it. **Alt+Enter** or **Shift+Enter**
  inserts a new line. Click an entry to copy it.
- Use **Alt+W** or a screenshot button, select a region, then choose **Save screenshot**, **Extract text**, or **Extract and translate**. **Enter** saves; **Esc** or right-click cancels. Extracting text alone creates no library entry and does not automatically copy text.
- Open a saved image entry and use **Recognize text** to add editable, searchable text. OCR runs locally. Translation uses only your configured provider when explicitly requested; source text remains available after failure.
- In **Settings > Appearance**, choose **Standard** or **Spatial** panel mode. Spatial keeps the same controls with perspective, layered cards and a thin panel edge. Choose **Fixed** or **Drag to inspect**. Hold the right mouse button on a card body or panel background and drag to adjust the view; release to keep the angle. Settings can select the middle button or a custom button/modifier combination. Windows accessibility preferences can disable animation.
- **Hide at desktop edge** is enabled by default to tuck the panel into a small handle when dragged to an edge. Hover over or click the handle to restore it. Ordinary collapse keeps only the six toolbar buttons.
- Select **Copy and translate** for two independent selection-bubble buttons. Copy saves the selection to the dock; Translate uses the same source text without adding an entry.
- Choose **Translation target language** independently of the interface language. The default is **Follow interface language**. A target selected in a result window applies only to that result; retry uses the retained source text.
- In a temporary OCR or translation result, choose a group and **Save source**, **Save translation**, or **Save both**. Saving both creates one text entry. Saving is explicit and does not copy text or save the screenshot.
- Under **Settings > Capture & translation**, independently show or hide the result's target selector and save controls. They appear after recognition or translation finishes. Hidden controls retain their saved values.
- If a shortcut cannot be registered, click the tray notification to open Settings. Each shortcut has a persistent status there; tray actions remain usable. A failed replacement keeps the previous working shortcut.

## Search, organize and reuse

Press **Ctrl+F** while the quick panel is active, or use its search button. Search has its own input, so the capture draft and selected capture group are preserved. Narrow results by group, tag or text/image/template type. Results load in pages of 50; choose **Load more** for the next page. Escape returns to capture. Image searching uses already stored OCR text and does not start recognition.

In the workbench, select items with checkboxes, Ctrl-click or Shift-click. Ctrl+A selects the currently loaded rows when focus is on the list, not every entry in the library. **Organize selected** moves groups, adds/removes tags, changes pinning or moves entries to Trash. The editor remains independent and unsaved changes are confirmed. Restore is available in Trash; restoring expired entries asks before changing them to Never expire. Batch operations validate the whole selection and either apply together or leave it unchanged. Undo is available for the most recent move to Trash during this session if those entries have not changed.

Choose **New text template**, or mark a text entry as a template in the editor. `{{date}}` and `{{time}}` use one local date/time snapshot, while `{{name}}` prompts for a value. Names are case-sensitive and support letters, digits and underscores; they start with a letter or underscore. `{{{{name}}}}` produces literal `{{name}}`. Fill and copy opens a preview; cancel does not use or burn the entry. Values are never scripts or nested templates. File/image entries cannot be templates.

Under **Settings > Appearance**, adjust text size or select a complete theme. Preview both Light and Dark before applying; cancel restores your appearance. The optional presets are **Sunlit Gallery**, **Twilight Study**, **Wisteria Atelier**, and **Wisteria Reverie**, in that order. Each contains matching day/night portrait and landscape backgrounds with complete Light/Dark palettes; System follows Windows app appearance. Theme import installs a validated ZIP without automatically applying it. Export includes both appearances and referenced images; it excludes your entries and credentials. Personal wallpaper requires confirmation before inclusion. The standard appearance remains the initial default.

**Settings surface opacity** adjusts the backgrounds of Settings cards, navigation
and inputs from **10% to 100%**, without fading text or icons. With theme artwork,
enable **Frosted settings background** and adjust its **1–18 px** radius to soften the
image behind those controls. Settings stays opaque when there is no artwork or
Windows high contrast is active. Settings frosting is off by default.
Under **Bubbles & notifications**, choose the bubble style and opacity.

## Fresh-install defaults

These defaults apply to a new configuration in 1.3.0. Upgrades preserve saved
preferences, including language and shortcuts.

| Setting | Default |
| --- | --- |
| Interface language | English |
| Global / screenshot shortcuts | Alt+Q / Alt+W |
| OCR / offline translation | Not preinstalled; explicit in-app installation |
| Launch at Windows startup | Off |
| Selection bubble / action / translator | On / Copy only / Google |
| Translation target language | Follow interface language |
| Result target selector / save controls | On / On |
| Bubble style | Default |
| Automatic clipboard collection / direct paste | Off / Off |
| Auto-hide when dragged to an edge | On |
| Collapse / expand delay | 2 s / 0.1 s |
| Theme / panel mode | Follow system / Standard |
| Complete theme / text size | Default, no artwork / 100% (adjustable from 90% to 150%) |
| Spatial inspection / button | Drag to inspect / Right mouse button |
| Panel text colors / controls opacity | Auto / 90% |
| Background image / overlay opacity | None / 20% |
| Panel frosted glass | Off; stored radius 4 px |
| Settings surface opacity / frosted glass | 100% / Off; stored radius 8 px |
| Scheduled automatic backup | On, every 24 hours |

## Optional local components

Open **Settings > Local components**. Click **Install / repair**
next to **Text recognition (Chinese / English)** or
**Offline translation (Chinese / English)**. Setup runs inside the app with
progress and cancellation; no terminal command is needed.
The standard package does not preinstall OCR or offline translation engines,
models, or runtimes. Their files download only when you explicitly select
**Install / repair**. A previous installation may already have usable components.
Use repair if a required file or native runtime is missing.

Offline translation requires an initial internet download of its runtime and
English/Chinese models. After setup, choose **Local offline (Argos / LibreTranslate)** under
**Translation engine**. The installed service starts automatically when needed;
**Start service** and **Stop service** are also available under **Local components**. It translates locally once the needed model is
installed. Setup may request Windows administrator permission to install the
Microsoft Visual C++ prerequisite, and Windows may request a restart.

Cloud translation providers have their own availability, credentials, quotas,
and billing terms. Their use sends selected text to that provider. Offline
translation and OCR do not fall back to a cloud provider on failure.

Each component has **Choose folder**, **Open folder**, and **Use default** actions.
Choose an empty, writable local folder or a valid existing component installation.
Switching locations preserves old files; install again if the new folder is empty.
OCR uses `OcrModels` and `OcrEngine` subfolders. Offline translation uses its selected
folder directly and stops the previous service before changing locations.

## Data and upgrades

**Settings > Backup & data** shows the last backup attempt and success, next scheduled
check, current status, location and snapshot list. **Back up now** creates a
manual snapshot; restoring a listed snapshot uses the same guarded import flow.
Automatic intervals include **15, 30 and 60 minutes**, 6, 12, 24 and 48 hours,
and 7 days. The default remains 24 hours; existing hourly settings migrate.

Use **Choose folder** to store future database backups elsewhere. The list and
retention policy apply to the selected folder. Old files stay in their original
location; switch back or import a backup to restore one. **Use default** returns
to `Backups` beside the active database. Full update-recovery copies still use
`Data/Backups/BeforeUpdate-*`. These choices do not relocate the active library.

Scheduling runs only while the app is open. Startup or resume catches up once,
without replaying every missed interval. Every successful run creates a complete
database snapshot, including stored images and OCR text. These snapshots do not
back up settings, downloaded components, backgrounds or desktop layout. Shorter
intervals use more storage: seven snapshots at 15-minute intervals span about
90 minutes from oldest to newest. Retention removes only owned automatic
snapshots after a successful backup; manual and recovery copies are preserved.

Your library and settings are under `Data` beside the program when the folder is
writable; otherwise they are under `%AppData%\KeeStashDock`. Back up the active
data folder before upgrading, close KeeStashDock, extract the new package into a
fresh folder, and copy the complete old data folder to the new `Data` folder.
Keep the old backup separately. Never overwrite a live database.

If **Start with Windows** was enabled and you moved the app to a new folder,
turn that setting off and on again in the new application to register its new
executable path. Otherwise the next Windows sign-in may launch the old copy.
Update any desktop or taskbar shortcuts too. Candidates with the same numeric
version as the final package require manual replacement; Check for updates
only offers a newer version. This includes earlier **1.3.0-r1–r5** packages:
their numeric version is already 1.3.0. Use the complete final ZIP in a fresh
folder, retain the old data and theme exports, and follow `Docs/Updating.md`.

From 1.2.2 onward, use **Check for updates** in the tray menu or **Settings > About**. The app checks official GitHub Releases only on request. Download and verify first, then confirm **Restart and upgrade**. Same-path upgrades preserve user data and installed components, keep a rollback copy, and check successful startup. See `Docs/Updating.md` for recovery and first-upgrade instructions.

This release upgrades the database to schema v5 after making a verified
pre-upgrade snapshot. Version 1.2.3 and earlier cannot open a v5 library. Keep
the pre-upgrade snapshot for use with a separate older installation; application
rollback never downgrades the library. Images and templates are included in
database backups, while themes must be exported separately.

## Help and license

- Documentation and translated guides: https://github.com/Keegankei/KeeStashDock
- Downloads: https://github.com/Keegankei/KeeStashDock/releases/latest
- Report a problem: https://github.com/Keegankei/KeeStashDock/issues
- Screenshot details: `Docs/Screenshots.md`
- Local setup details: `OcrModels/README.md` and `OfflineTranslation/README.md`

KeeStashDock is licensed under Apache-2.0. See `Licenses/LICENSE`,
`Licenses/NOTICE`, and `Licenses/THIRD_PARTY_NOTICES.md`; dependency licenses are
in the corresponding `Licenses` subfolders.

## Included theme presets

Under **Appearance > Complete themes**, the built-in order is **Sunlit Gallery**,
**Twilight Study**, **Wisteria Atelier**, then **Wisteria Reverie**. The first two
contain scenery without people; the fourth uses a refined anime illustration.
Each has portrait/landscape day and night artwork. **System** follows Windows
app appearance, switching the corresponding palette and background rather than
scheduling by clock time. Preview before applying; Cancel restores the previous
appearance. Fresh installs remain image-free. Export themes separately from
database backups.

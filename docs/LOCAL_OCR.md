# Local screenshot text recognition

Screenshots remain image entries. Recognition runs only when requested, keeps the
original image, and returns editable text. It never translates or uploads the
image. Select recognized text to use the existing translation bubble and the
configured translation provider.

## Models

The first version supports printed English and Simplified Chinese using official
Tesseract `tessdata_fast` models. Handwriting, rotated text, tiny fonts, complex
tables, and Traditional Chinese are not guaranteed. Always review recognized text.

The standard 1.2.2 package does not preinstall the OCR engine or language
models. Before first recognition, open **Settings > Optional components** and
choose **Install / repair** for text recognition. This explicit setup downloads
and verifies the required engine and models, shows progress and failures, and
can be cancelled. Recognition itself never downloads components.

The downloaded engine lives in `%LOCALAPPDATA%\KeeStashDock\OcrEngine` and the
models in `%LOCALAPPDATA%\KeeStashDock\OcrModels`, so a read-only application
directory does not prevent installation. Existing portable `x64` engines and
`OcrModels` assets remain supported. Recognition prefers a complete valid user
model set, then a valid portable model set.

For maintainers, install both engine and models into isolated diagnostic folders:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ocr/Setup.ps1 `
  -Destination artifacts/ocr-check/OcrModels `
  -EngineDirectory artifacts/ocr-check/x64
```

The script downloads `eng.traineddata`, `chi_sim.traineddata`, and their Apache 2.0
license from the official `tesseract-ocr/tessdata_fast` repository at fixed commit
`87416418657359cb625c412a48b6e1d6d41c29bd`, checks each SHA256, and installs them
alongside the separately verified native engine. Without explicit destinations,
setup uses the per-user folders described above. Installed components and local
test caches are not included in a standard publish.
OCR setup scripts ship in `OcrModels` for diagnostics; ordinary users should use
the Settings installation button, which handles both engine and model setup.
NuGet and GitHub access is needed only for this explicit setup step;
recognition works offline afterwards. The Settings installer also checks the native
engine and, when needed, downloads Microsoft's signed Visual C++ x64 runtime.
Windows may request administrator permission; a required restart is reported
before setup continues. No Windows language packs or global Python
installation are required.

Cancelling setup or reaching its timeout stops the app's setup worker and ordinary
child processes. A Microsoft runtime installation already authorized through
Windows may continue with administrator privileges; cancellation does not undo
that installation. Let it finish, and if Settings reports **Restart required**,
restart Windows before retrying component setup. This does not change the
separate OCR recognition cancel button, which stops the recognition process.

## Engine and limits

Explicit installation downloads the pinned `TesseractOCR` 5.5.2 NuGet archive,
verifies its SHA-256, and extracts only allowlisted native files. Its Windows x64
CLI reports Tesseract 5.5.0 and Leptonica 1.85.0. This package's native DLLs require
the Microsoft Visual C++ v14 x64 runtime. If the engine cannot start, use the OCR
**Install / repair** button in Settings; it obtains the verified installer from
[Microsoft](https://aka.ms/vc14/vc_redist.x64.exe) when required.

The app runs one hidden OCR child process at a time with a 45-second deadline.
Images are sent through stdin and UTF-8 results are read from stdout. Cancellation
and timeout terminate that process. Images are not written to temporary files;
recognized text and native diagnostics are not logged. Inputs are bounded to
40 million pixels, 16,384 pixels per side, and 48 MiB of encoded PNG data. These
limits prevent accidental resource exhaustion; they do not promise a large image
will finish within the deadline.

Windows.Media.Ocr is intentionally not used: Microsoft only officially supports
that API for desktop apps with package identity, whereas KeeStashDock remains a
portable unpackaged app.

## Capture behavior

The caller hides app windows before a frozen desktop snapshot is taken. One
selector covers each monitor. Selection coordinates are physical pixels, allowing
negative monitor origins and different display scales. Press Alt+W or a screenshot
button, select a region, then choose Save screenshot, Extract text, or Extract and
translate. Enter saves; Escape or right-click cancels without creating an entry.
Screens are held in memory for selection; only a region explicitly saved becomes
a stored PNG. Recognition-only results remain temporary.

## Sources and licenses

- [Windows OCR package identity requirement](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr)
- [TesseractOCR wrapper and native distribution](https://github.com/Sicos1977/TesseractOCR)
- [Official fast language models](https://github.com/tesseract-ocr/tessdata_fast)

The wrapper, Tesseract, and language models use Apache 2.0. Leptonica uses a BSD
2-Clause license. The package also contains native image codec dependencies; see
the repository's third-party notices for their attribution.

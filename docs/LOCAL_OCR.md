# Local screenshot text recognition

Screenshots remain image entries. Recognition runs only when requested, keeps the
original image, and returns editable text. It never translates or uploads the
image. Select recognized text to use the existing translation bubble and the
configured translation provider.

## Models

The first version supports printed English and Simplified Chinese using official
Tesseract `tessdata_fast` models. Handwriting, rotated text, tiny fonts, complex
tables, and Traditional Chinese are not guaranteed. Always review recognized text.

Models are optional local assets, not committed binaries. Install them explicitly
before building a distributable:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ocr/Setup.ps1
```

The script downloads `eng.traineddata`, `chi_sim.traineddata`, and their Apache 2.0
license from the official `tesseract-ocr/tessdata_fast` repository at fixed commit
`87416418657359cb625c412a48b6e1d6d41c29bd`, checks each SHA256, and places them in
`tools/ocr/models`. Builds and publishes copy these assets to `OcrModels` beside
the executable. A package without models can install them by running its
`OcrModels/Setup.ps1`. GitHub access is needed only for this explicit setup step;
recognition works offline afterwards. No Windows language packs or global Python
installation are required.

## Engine and limits

The pinned `TesseractOCR` NuGet package is version 5.5.2. Its bundled Windows x64
CLI reports Tesseract 5.5.0 and Leptonica 1.85.0. This package's native DLLs require
the Microsoft Visual C++ 2015–2022 x64 runtime; if the engine cannot start, install
the runtime from [Microsoft](https://aka.ms/vs/17/release/vc_redist.x64.exe).

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
negative monitor origins and different display scales. Drag a region to save;
Escape or right-click cancels without creating an entry. Screens are held in
memory only for selection; only the chosen region becomes the saved PNG.

## Sources and licenses

- [Windows OCR package identity requirement](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr)
- [TesseractOCR wrapper and native distribution](https://github.com/Sicos1977/TesseractOCR)
- [Official fast language models](https://github.com/tesseract-ocr/tessdata_fast)

The wrapper, Tesseract, and language models use Apache 2.0. Leptonica uses a BSD
2-Clause license. The package also contains native image codec dependencies; see
the repository's third-party notices for their attribution.

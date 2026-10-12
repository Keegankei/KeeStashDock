# OCR distribution license sources

These unmodified upstream license texts accompany the native Windows x64 engine
bundled in the TesseractOCR 5.5.2 NuGet package. That binary reports Tesseract 5.5.0
and Leptonica 1.85.0, with the codecs listed below. Files were retrieved on
2026-10-12. The application uses the CLI only for local recognition.

TesseractOCR wrapper/distribution attribution:

- Copyright 2012-2019 Charles Weld
- Copyright 2021-2025 Kees van Spelde
- Licensed under Apache License 2.0, reproduced in `tesseract-LICENSE.txt`
- [Upstream source](https://github.com/Sicos1977/TesseractOCR)

License file sources:

| File | Upstream source |
| --- | --- |
| tesseract-LICENSE.txt | https://raw.githubusercontent.com/tesseract-ocr/tesseract/5.5.0/LICENSE |
| tesseract-AUTHORS.txt | https://raw.githubusercontent.com/tesseract-ocr/tesseract/5.5.0/AUTHORS |
| leptonica-LICENSE.txt | https://raw.githubusercontent.com/DanBloomberg/leptonica/1.85.0/leptonica-license.txt |
| giflib-LICENSE.txt | https://android.googlesource.com/platform/external/giflib.git/+/21812042166e8e917577531a5a6c39e3f834d1da/COPYING |
| libjpeg-turbo-LICENSE.txt | https://raw.githubusercontent.com/libjpeg-turbo/libjpeg-turbo/2.0.6/LICENSE.md |
| libjpeg-turbo-README-ijg.txt | https://raw.githubusercontent.com/libjpeg-turbo/libjpeg-turbo/2.0.6/README.ijg |
| libpng-LICENSE.txt | https://raw.githubusercontent.com/pnggroup/libpng/v1.6.37/LICENSE |
| libtiff-LICENSE.txt | https://gitlab.com/libtiff/libtiff/-/raw/v4.3.0/COPYRIGHT |
| zlib-LICENSE.txt | https://raw.githubusercontent.com/madler/zlib/v1.2.11/README |

GIFLIB's original SourceForge/GitLab delivery was unavailable during retrieval;
its unmodified MIT notice is retained from Google's official Android source
distribution. The English and Simplified Chinese model license is downloaded
and hash-verified by `tools/ocr/Setup.ps1`, then stored beside those models.

This software is based in part on the work of the Independent JPEG Group.

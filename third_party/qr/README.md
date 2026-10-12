# QR encoder attribution

The algorithm structure in `src/KeeStashDock.Desktop/QrEncoder.cs` follows
Project Nayuki's QR Code generator. KeeStashDock adapts the implementation to
C# with UTF-8 byte mode, automatic QR versions 1-40, and error correction level M.
The original code is MIT licensed, not public domain.

`Nayuki-LICENSE.txt` preserves the complete license section from the upstream
[README](https://github.com/nayuki/QR-Code-generator/blob/master/Readme.markdown),
reviewed on 2026-10-12. The existing adaptation did not record an upstream commit;
this notice records the verified upstream license without claiming an exact
source revision for that adaptation.

The license is copied to `Licenses/QR` in build and publish outputs.
Project homepage: https://www.nayuki.io/page/qr-code-generator-library

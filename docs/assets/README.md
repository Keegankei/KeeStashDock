# Application artwork

The application icon is AI-generated artwork used by KeeStashDock since 1.2.1.

| Asset | Purpose |
| --- | --- |
| [app-icon.png](app-icon.png) | Documentation image and approved raster artwork |
| [app.ico](../../src/KeeStashDock.Desktop/Assets/app.ico) | Windows application icon derived from the approved artwork |

To regenerate the Windows icon from the repository root on Windows, run:

```powershell
./tools/Export-AppIcon.ps1
```

The exporter resizes the approved artwork into 16, 20, 24, 32, 40, 48, 64, 96,
128, and 256 pixel PNG frames inside an ICO container. It preserves the design
and the source image's transparency; it does not remove or redraw any part of
the image.

## Artwork and license

The icon depicts a flowing ribbon of note paper and a central quill, using
magenta, yellow-green and cyan-blue colors on a transparent background.

These project assets may be redistributed with KeeStashDock under the
repository's [Apache-2.0 license](../../LICENSE), to the extent of rights held
by the project. AI generation does not establish exclusivity or guarantee
copyright eligibility in every jurisdiction. No affiliation or endorsement
by an artist, studio or brand is claimed.

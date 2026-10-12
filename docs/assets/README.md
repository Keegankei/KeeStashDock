# Application artwork

The application icon was created with the built-in AI image generation tool,
through design iterations directed by the project maintainer. The maintainer
approved this version on **2026-10-12** for KeeStashDock 1.2.1.

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

## Design record

The whole ribbon represents a continuous sheet of note paper. Three flowing
lobes suggest a Mobius strip, with a large, central vintage quill connecting the
abstract form to writing and saved notes. There is no separate note badge or
lettering. The palette places magenta above, yellow/lime on the left, and
cyan/blue on the right. After approving the design, the maintainer requested a
transparent background in place of the draft's deep indigo backdrop.

The final generation direction was to preserve the three-lobed silhouette from
the preceding AI-generated draft, remove all text and small surface details,
join the ribbon into a continuous blank paper band, and enlarge the ivory
feather with its warm gold nib. Background removal uses the same built-in image
generation tool. Restrained highlights and a smooth paper finish
keep the main shapes clear at icon sizes. The geometry is an artistic
interpretation, not a claim of mathematically verified Mobius topology.

The maintainer cited M. C. Escher's *Mobius Strip I* for spatial inspiration,
*Manifold Garden* for the arrangement of colors and flowing forms, and Apple
product design for restrained material finish. These references describe design
inspiration only. Reference screenshots and source artworks are not distributed
as project assets. No affiliation or endorsement is implied, and the project
license does not grant rights to those names, trademarks, or source artworks.

This record documents the generation process and the maintainer's approval; it
does not assert exclusive rights or provide a legal clearance guarantee.

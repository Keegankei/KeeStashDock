# Complete appearance themes

KeeStashDock 1.3.0 includes four optional presets. A theme combines light/dark
colors, portrait and landscape artwork, and appearance preferences across the
quick panel, workbench, Settings and supported result windows. It does not change
your entries, hotkeys, component installations or Classic/Spatial mode.

## Included presets

The picker lists these built-in presets in this order. Preview images are the
actual shipped backgrounds, not screenshots of the app or evidence of a default
configuration.

| Preset | Day artwork | Night artwork | Character |
| --- | --- | --- | --- |
| 1. Sunlit Gallery | [Landscape](../theme/builtin/sunlit-gallery/assets/light-landscape.png) / [Portrait](../theme/builtin/sunlit-gallery/assets/light-portrait.png) | [Landscape](../theme/builtin/sunlit-gallery/assets/dark-landscape.png) / [Portrait](../theme/builtin/sunlit-gallery/assets/dark-portrait.png) | Quiet architecture, cream plaster, sage windows and directional light; no people |
| 2. Twilight Study | [Landscape](../theme/builtin/twilight-study/assets/light-landscape.png) / [Portrait](../theme/builtin/twilight-study/assets/light-portrait.png) | [Landscape](../theme/builtin/twilight-study/assets/dark-landscape.png) / [Portrait](../theme/builtin/twilight-study/assets/dark-portrait.png) | Olive and walnut reading room, warm accents and deep shadows; no people |
| 3. Wisteria Atelier | [Landscape](../theme/builtin/wisteria-atelier/assets/light-landscape.png) / [Portrait](../theme/builtin/wisteria-atelier/assets/light-portrait.png) | [Landscape](../theme/builtin/wisteria-atelier/assets/dark-landscape.png) / [Portrait](../theme/builtin/wisteria-atelier/assets/dark-portrait.png) | Anime pianist in a wisteria conservatory |
| 4. Wisteria Reverie | [Landscape](../theme/builtin/wisteria-reverie/assets/light-landscape.png) / [Portrait](../theme/builtin/wisteria-reverie/assets/light-portrait.png) | [Landscape](../theme/builtin/wisteria-reverie/assets/dark-landscape.png) / [Portrait](../theme/builtin/wisteria-reverie/assets/dark-portrait.png) | Anime pianist in a luminous lavender conservatory |

## Preview and apply

1. Open **Settings > Appearance > Complete themes** and select a preset.
2. Choose **Preview and apply**. Select **Light**, **Dark**, or **System**.
3. Choose whether selection bubbles and result windows should follow its colors.
4. Choose **Apply theme** to save, or **Cancel** to restore the previous appearance.

**System** follows Windows app light/dark preference. It switches both palette
and the corresponding day/night image; it does not schedule a change by clock
time. Portrait artwork suits narrow panels; landscape artwork suits larger
windows. A fresh installation uses no preset or background, and an upgrade
preserves saved preferences.

## Personal backgrounds

Open **Settings > Appearance > Background image** and choose **Quick panel** or
**Settings page** under **Customize background**. Select or clear a picture and
adjust that area’s overlay opacity and blur independently. Clearing removes the
image from that area; **Use theme background** restores its preset artwork. Other
areas retain their own backgrounds.

Theme export keeps the two areas separate in both light/dark variants. Including
personal images requires explicit sharing confirmation; private source paths are
not written into the theme package.

## Readability controls

- **Text size:** 90–150%, default 100%; icons keep their intended size.
- **Panel controls opacity:** independent from Settings, default 90%.
- **Settings surface opacity:** 10–100%, default 100%; text and icons stay sharp.
- **Frosted settings background:** optional blur of the Settings background image, 1–18 px,
  off by default with a stored 8 px radius. It does not blur text or controls.
- **Bubble style and opacity:** independent appearance controls for selection and
  result bubbles.

Without artwork, Settings keeps opaque surfaces. Windows high contrast takes
priority over decorative theme effects. The quick panel's existing background
blur is independent and starts off with a stored 4 px radius.

## Import, export and backup

**Import theme** accepts a complete theme ZIP for preview or installation.
**Export current appearance** saves both variants, their images and applicable
appearance overrides. Including personal background images requires an explicit
choice. Inspect the package before sharing: its artwork is included, whereas
your entries, credentials and component paths are not.

Built-in files live under `theme/builtin/` beside the executable. Imported themes
live under `theme/custom/`, which updates preserve. Do not edit built-in files to
keep personal changes; export/import a custom copy. **Database backups do not
include themes**. Export themes separately when moving to another computer.

## Artwork provenance

The bundled backgrounds are AI-generated artwork. Each preset includes
artwork attribution and licensing information:

- [Sunlit Gallery](../theme/builtin/sunlit-gallery/SOURCE.md)
- [Twilight Study](../theme/builtin/twilight-study/SOURCE.md)
- [Wisteria Atelier](../theme/builtin/wisteria-atelier/SOURCE.md)
- [Wisteria Reverie](../theme/builtin/wisteria-reverie/SOURCE.md)

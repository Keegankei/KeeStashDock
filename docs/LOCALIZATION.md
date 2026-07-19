# Localization Guide

## Source language and exceptions

English is the repository source language for code, comments, configuration, automation, issue templates, and general documentation.

The intentional localization exceptions are:

- `README.zh-CN.md`
- `src/KeeStashDock.Desktop/Locales/*.json`

The canonical application language file is `en-US.json`. All other language files must remain structurally compatible with it.

## File format

Each language pack is UTF-8 JSON and contains:

- A `$meta` object
- The same top-level localization keys as `en-US.json`
- Translated string values

Example metadata:

```json
{
  "$meta": {
    "nativeName": "English",
    "direction": "ltr"
  }
}
```

Supported metadata fields include:

- `nativeName`: language name displayed in Settings
- `direction`: `ltr` or `rtl`
- `preferredFont`: optional UI font for the language

The filename is the locale tag used by the application, such as `en-US.json` or `ar-SA.json`.

## Translation contract

1. Do not rename or remove keys.
2. Preserve placeholders exactly. If English contains `{0}`, the translation must also contain `{0}`.
3. Preserve Markdown structure in long help text.
4. Keep accelerator keys, keyboard shortcuts, product names, file extensions, and command names technically accurate.
5. Do not translate JSON property names, file paths, CLI commands, or code identifiers unless the surrounding text clearly requires an explanation.
6. Review layout at normal and high DPI, especially buttons, tabs, and right-to-left windows.
7. Use natural language rather than literal word-for-word translation.

## Adding a new key

1. Add the key and English value to `en-US.json`.
2. Add the same key to every other locale.
3. Preserve any composite-format placeholders.
4. Run the test suite.
5. Verify the affected window with at least English, Simplified Chinese, and Arabic.

## Adding a new language

1. Copy `en-US.json` to a file named with a valid locale tag.
2. Translate `$meta.nativeName` and all values.
3. Set `$meta.direction` correctly.
4. Add `preferredFont` only when the default UI font is unsuitable.
5. Verify live switching and application restart.
6. Check the quick panel, workbench, settings, dialogs, tray menu, selection bubble, translation card, sticky note, and in-application help.

## Validation

`KeeStashDock.Tests` verifies that:

- Every locale file is valid JSON.
- Every locale contains the same flattened key paths as `en-US.json`.
- Composite-format placeholders match the English source.
- No unauthorized non-Latin source text appears outside the approved localization paths.

These checks do not validate translation quality. Native review is still required, particularly for `Help.Content`, which can become semantically stale even while all keys remain valid.

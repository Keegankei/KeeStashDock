# Security Policy

## Supported versions

Security fixes are provided for the latest tagged release only. Users should upgrade to the newest release before requesting a backport.

| Version | Supported |
| --- | --- |
| Latest tagged release | Yes |
| Older releases and untagged builds | No |

## Reporting a vulnerability

Do not open a public issue for a suspected vulnerability.

Use the repository's private **Report a vulnerability** workflow under the Security tab. If private vulnerability reporting is not enabled, contact the maintainer through a private method listed on the maintainer's GitHub profile and ask for a secure reporting channel before sending exploit details or sensitive data.

Include:

- Affected version and Windows version
- Component and configuration involved
- Reproduction conditions and observed impact
- Whether clipboard contents, selected text, files, database data, backups, logs, or API keys may be exposed
- Any temporary mitigation already identified

Do not include real secrets or personal clipboard data. Use synthetic examples.

## Response process

The maintainer will acknowledge a complete report, reproduce and assess the issue, coordinate a fix and release notes, and credit the reporter when requested and appropriate. Exact response times are not guaranteed by this volunteer-maintained project.

## Security-sensitive areas

Review changes carefully when they affect:

- Clipboard monitoring, direct paste, global hooks, or synthetic keystrokes
- Screenshot capture, local OCR child processes, image decoding, and downloaded model integrity
- Selection capture and translation network requests
- Baidu, DeepL, and Alibaba Cloud credential storage (settings are not encrypted at rest)
- Offline translation loopback validation, proxy bypass, model downloads, and local service lifecycle
- Native runtime download signatures, administrator prompts, setup cancellation, and restart handling
- SQLite import, backup replacement, and path handling
- Portable data-directory permissions
- Log content and exception reporting
- Group-package import and JSON parsing
- Dependency or GitHub Actions updates

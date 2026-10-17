# Local offline translation

KeeStashDock supports an optional Windows x64 local translation engine. The standard 1.2.2 package includes setup tools but no preinstalled runtime or models. It uses the official Argos English/Chinese 1.9 models through CTranslate2, with SentencePiece tokenization and py3langid language detection. It provides the small LibreTranslate-compatible API used by KeeStashDock; it is not the full LibreTranslate server.

The initial setup needs internet access to **python.org**, **files.pythonhosted.org** and **argos-net.com**, plus Microsoft download hosts if the shared Visual C++ runtime is missing or damaged. These international download hosts may not be reachable on every network in mainland China. Once setup completes, starting the engine and translating require no internet, account, key or API payment. There is no automatic cloud fallback.

## Install once

Choose **Local offline translation** in Settings, then click **Install / repair
offline translation**. KeeStashDock downloads and verifies the components in the
background, displays the current stage, and starts the engine when installation
finishes. You can cancel the operation, retry a failed setup, or use the Settings
start/stop buttons. No PowerShell command is required for normal use. The app checks the shared native
runtime first and, when necessary, downloads its Microsoft-signed installer.
Windows may ask for administrator permission; a required Windows restart is
reported before engine setup continues. If Settings reports **Restart required**,
restart Windows and then retry component setup.

An installation started through the app uses the same scripts and verified
archives as the manual procedure below. A repair stops the existing managed
engine first. Keep Settings open until the operation completes or cancellation
has finished. Cancellation or timeout stops the app's setup worker and ordinary
child processes. An already authorized Microsoft runtime installation may continue
with administrator privileges; cancellation does not undo it. Let that Windows
installation finish before retrying. A cancelled or failed repair can be retried;
valid downloads are reused.

If your build contains `OfflineTranslation/runtime/installed.json`, the scripts
prefer that preinstalled portable runtime. Otherwise they use the per-user runtime
folder. An explicit `-RuntimeDirectory` takes priority for manual commands.

### Manual setup for diagnostics and maintainers

The setup directory contains `Setup.cmd`, `Start.cmd`, and `Stop.cmd` wrappers.
Alternatively open PowerShell there and run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Setup.ps1
```

In a source checkout, the scripts are in `tools/offline-translation`; published
builds include them in `OfflineTranslation`. Settings also provides an option to
open that folder for troubleshooting.

Setup installs a portable Python 3.12 x64 interpreter and exact, SHA-256-verified Windows packages into `%LOCALAPPDATA%\KeeStashDock\OfflineTranslation`. It does not change PATH, install global Python packages, register a Windows service or touch KeeStashDock's `Data` folder. Allow approximately 1 GB of free disk space and 2 GB of available memory. Initial model loading and language detection can take longer than subsequent requests.

Setup downloads only two translation models: English to Simplified Chinese and Simplified Chinese to English. The language detector recognizes other languages so unsupported text can be rejected instead of being treated as English. Short or mixed-language snippets can still be ambiguous; a complete sentence is more reliable.

## Start and stop

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Start.ps1
```

The engine starts in the background and listens only on `http://127.0.0.1:5000`. Keep that address in KeeStashDock's offline endpoint setting. The startup command reports when it is ready. With the default local address, KeeStashDock can start an already installed engine when a translation is requested; this never downloads components. The engine is not registered as a Windows startup service and keeps running when KeeStashDock closes until stopped from Settings or the helper script.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Stop.ps1
```

Stop verifies the saved process executable and start time before stopping it. For a visible diagnostic run, use `Start.ps1 -Foreground` and press Ctrl+C to stop that foreground process. `Stop.ps1` controls only a background process started by the script.

Use `-Port 5001` with Start if port 5000 is occupied, and change the app endpoint to `http://127.0.0.1:5001`.

## Custom runtime folder

Pass the same absolute folder to all three commands. This is useful for a portable copy or a development validation run:

```powershell
.\Setup.ps1 -RuntimeDirectory 'D:\KeeStashDockOfflineRuntime'
.\Start.ps1 -RuntimeDirectory 'D:\KeeStashDockOfflineRuntime'
.\Stop.ps1 -RuntimeDirectory 'D:\KeeStashDockOfflineRuntime'
```

The runtime can be copied to another Windows x64 computer after stopping it. Copy the complete folder, including `python`, `packages`, `models` and `installed.json`, and use `-RuntimeDirectory` on the destination. No installation download is needed there. Keep package licenses in `.dist-info` folders and model attribution in each model's `README.md` when distributing a copy.

## Limits and troubleshooting

- The models downloaded during setup support English and **Simplified Chinese**, not Traditional Chinese or other translation pairs. Select a compatible app language or a cloud provider for other target languages.
- Translations run on the CPU. Quality, technical terminology and formatting may differ from cloud services. There is no guarantee of correctness for sensitive content.
- Each request accepts at most 5,000 characters. A sentence over 256 model tokens is rejected with an error; input is never silently truncated. Divide long text into shorter sentences.
- Only one request runs at a time. A second simultaneous request receives a busy response; retry after the first finishes.
- A missing native DLL may require the [Microsoft Visual C++ Redistributable x64](https://aka.ms/vc14/vc_redist.x64.exe). Follow Microsoft's installer instructions, then run Start again.
- For startup failures, inspect `service.stderr.log` in the runtime folder. Logs contain startup diagnostics only, not translated text or request bodies.
- If setup cannot reach a download host, retry on a network that can reach the official host, or prepare the complete runtime on another computer and copy it. Valid cached downloads are reused. A checksum mismatch stops installation.
- Setup is the only command that downloads anything. Stop the engine before rerunning Setup. There is no automatic model or dependency update.
- To uninstall, first run Stop, then remove the runtime folder you selected. KeeStashDock's clipboard data is stored separately.

## API and privacy

`GET /languages` advertises `en` and `zh`, with supported targets. `POST /translate` accepts JSON or URL-encoded form fields `q`, `source`, `target`, and `format=text`. `source=auto` uses local language detection; success returns `translatedText` and `detectedLanguage.language`. Missing or unsupported models produce `code: language_model_unavailable`. `GET /health` returns a service identifier and process ID for the startup script.

The service binds IPv4 loopback only, rejects browser-origin requests, exposes no CORS access and validates the Host header. It contains no downloader and installs a Python audit hook that rejects outgoing socket connections and DNS lookups. It does not save input, translations or request logs. Other local applications running under the computer's users can access the loopback port; this is a local helper, not a multi-user authenticated service.

## Pinned components and attribution

- Portable [CPython 3.12.10](https://www.python.org/downloads/release/python-31210/), the last official 3.12 Windows binary release.
- [CTranslate2 4.8.2](https://pypi.org/project/ctranslate2/4.8.2/), [SentencePiece 0.2.2](https://pypi.org/project/sentencepiece/0.2.2/), [NumPy 2.3.5](https://pypi.org/project/numpy/2.3.5/) and [PyYAML 6.0.3](https://pypi.org/project/PyYAML/6.0.3/).
- [py3langid 0.2.2](https://pypi.org/project/py3langid/0.2.2/) performs language detection without network access.
- [Argos package index](https://github.com/argosopentech/argospm-index) publishes the English/Chinese model archives. Their metadata and SHA-256 values are fixed in `setup_runtime.py`.
- The model READMEs attribute the original OPUS-MT models to Jörg Tiedemann and Santhosh Thottingal, *OPUS-MT — Building open translation services for the World*, EAMT 2020, licensed under CC-BY 4.0. Model attribution is retained in the installed model folders.

The Microsoft Visual C++ prerequisite installer is downloaded directly from
Microsoft and remains subject to its own Microsoft license terms. It is not
bundled in the source repository.

The optional downloaded runtimes and models retain their own licenses; they are not relicensed by KeeStashDock's Apache-2.0 license.

## Developer verification

Run `python -B tools/offline-translation/test_runtime.py` and `powershell -NoProfile -ExecutionPolicy Bypass -File tools/offline-translation/test_process.ps1` from a source checkout for the archive, inference, and process identity safety tests; these use only Python's standard library. Release validation should also run Setup, Start and Stop against a clean runtime, exercise a real English/Chinese translation in both directions, and check unsupported languages through the application's translation client. No real clipboard content is needed for these checks.

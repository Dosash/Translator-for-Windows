# Translator for Windows — architecture

Windows port of [Translator for macOS](https://github.com/Dosash/Translator-for-macOS): a free tray translator
with online translation (public Google Translate endpoint, no API keys) and optional on-device translation.

## Stack

- .NET 10, WPF, C# (`net10.0-windows10.0.19041.0`, runs on Windows 10 1809+ and Windows 11).
- No WinForms. Win32 interop via P/Invoke, WinRT APIs via the Windows TFM (speech, accent color, OCR).
- Offline engine: ONNX Runtime + Opus-MT (Marian) models from Hugging Face, English as the pivot language.
- Per-monitor V2 DPI aware (`app.manifest`).

## Solution layout

```text
Translator.slnx
src/Translator/                 WPF app (WinExe, AssemblyName "Translator")
  App.xaml(.cs)                 entry point, CLI modes, single instance
  AppController.cs              port of the macOS AppDelegate: tray, hotkeys, windows
  Core/                         models and services (no UI types except WPF Key/ModifierKeys)
  Platform/                     Win32 integration
  UI/                           windows, controls, themes
  Assets/                       AppIcon.ico
src/Translator.Offline/         offline translation engine (net10.0 class library)
tests/Translator.Tests/         xUnit tests
tools/IconGen/                  renders Assets/AppIcon.ico
tools/OfflineCli/               console harness for the offline engine (status, download, translate, bench)
installer/                      Inno Setup script
```

## Module map

| Module | Paths | Responsibility |
|---|---|---|
| Core | `src/Translator/Core/**` | `AppLanguage`, `L10n`, `SettingsStore`, `TranslatorModel`, `HistoryStore`, `GoogleTranslateEngine`, `UpdateChecker`, `SpeechService`, `DebugLog`, `Hotkey` |
| Platform | `src/Translator/Platform/**` | hotkeys, clipboard, selection grab / paste (chord depends on the target app, see `InputProfile`), selection location (UI Automation / caret / mouse), tray icon, autostart, single instance + IPC, monitors, screen capture (`Capture/`), Windows OCR (`Ocr/`) |
| Offline | `src/Translator.Offline/**` | model catalog, downloads, tokenizer, ONNX inference, language detection |
| UI | `src/Translator/UI/**`, `App.xaml(.cs)`, `AppController.cs` | themes, styles, windows, window effects (DWM backdrop, corners), screen-area selection overlay (`ScreenCapture/`) |
| Build | `tools/**`, `installer/**`, `build.ps1`, `.github/**`, `README.md` | icon, publish, installer, CI/CD, docs |

Tests mirror the module folders: `tests/Translator.Tests/{Core,Platform,Offline,UI}`.

## Conventions

- Language ids everywhere are the Google-style codes from `AppLanguage.All`:
  `ru en es de fr it pt zh-CN ja ko tr uk`; `"auto"` = detect.
- `TranslatorModel` and `SettingsStore` are `ObservableObject`s used from the UI thread only.
  Background work uses `async`/`await`; results are applied on the UI thread.
- Every user-visible string goes through `L10n.T(key)` / `L10n.Format(key, args)` (composite format `{0}`).
- Screen geometry from `Platform` is in **physical pixels** (`PixelRect`). Windows are positioned with
  `SetWindowPos` in pixels, so mixed-DPI multi-monitor setups work. Gap/margin scaling per monitor lives in
  `MonitorMetrics`/`WindowPlacement` and is unit-tested with a 100% + 150% layout (mixed-DPI hardware is
  untested so far). Floating windows never re-anchor while being dragged, so a DPI change mid-drag doesn't
  snap them back; a tray-anchored panel without an icon rect uses the primary taskbar, not the cursor's monitor.
- Privacy: never log or persist anything except settings and the last 10 history entries.
  `DebugLog` gets lengths, states and errors — never the text itself. Screen captures for OCR stay in memory
  and are zeroed right after use; OCR logs sizes, scales, timings, recognizer tags and line counts only.
- Files (all under `%TRANSLATOR_DATA_DIR%` instead when that variable is set, see CLI):
  - `%APPDATA%\Translator\settings.json`, `%APPDATA%\Translator\history.json`
  - `%LOCALAPPDATA%\Translator\models\` — offline models
  - `%LOCALAPPDATA%\Translator\logs\debug.log`

## Behavior (parity with macOS)

| macOS | Windows |
|---|---|
| Menu bar icon, no Dock icon | Notification area (tray) icon, no taskbar button |
| Floating panel under the status item | Borderless panel above the tray icon; closes on outside click / Esc; draggable |
| `⌃⌥T` — translate selection (simulated `⌘C`, Accessibility permission) | `Ctrl+Alt+T` — simulated copy chord, no permission needed; translation bubble next to the selection |
| `⌃⌥C` — translate clipboard | `Ctrl+Alt+C` |
| `⇧⌘E` — macOS Service | `Ctrl+Alt+Space` — show/hide the panel; `Translator.exe --translate "text"` |
| “Replace” in the bubble (simulated `⌘V`) | “Replace”: restore focus to the source window, simulated paste chord |
| Apple Translation offline | Opus-MT via ONNX Runtime, downloaded per language |
| Speech: AVSpeechSynthesizer | `Windows.Media.SpeechSynthesis` |
| Launch at login: SMAppService | `HKCU\...\Run` |
| Themes Calm / Neon / Frost Glass, system accent | Same themes; Mica/Acrylic backdrop on Windows 11, Windows accent color |
| Update check via GitHub Releases | Same (`Dosash/Translator-for-Windows`) |
| — | `Ctrl+Alt+S` — translate a screen area: freeze-frame, drag a rectangle, `Windows.Media.Ocr` on this PC, bubble next to the area |

The copy/paste chord is not always `Ctrl+C`/`Ctrl+V`: `InputProfile.Classify` looks at the target
window's process image name and window class and picks one of three profiles — terminals and
IDEs/editors with an embedded terminal use `Ctrl+Insert`/`Shift+Insert` (a plain `Ctrl+C` with
nothing selected is the interrupt key in a terminal and would kill whatever command is running);
PuTTY/KiTTY copy on selection, so no copy keystroke is sent and the clipboard is read as-is; every
other app keeps `Ctrl+C`/`Ctrl+V`. See `Platform/InputProfile.cs` for the table.

Translation flow (`TranslatorModel.Translate`):

1. Trim input; empty → nothing; longer than 5000 chars → error.
2. Resolve pair: source `auto` → null; if source is auto, target is `ru` and the text has Cyrillic → target `en`.
3. Unless “Offline only”: Google (`sl=auto` or code). On success record history, engine = Google.
   If Google detects the source equal to the target while the source is `auto`, translate again into
   English (or into the UI language when the target is already English).
4. Otherwise / on failure: offline engine when the pair is installed.
5. Errors as on macOS (offline-only without models, no internet, generic failure).

Auto-translate: 700 ms debounce after typing. History keeps the last 10 entries.

## Screen-area translation

Hotkey `Ctrl+Alt+S` (`HotkeyManager.ScreenId` = 4, `SettingsStore.ScreenHotkey`), tray menu item, or
`--translate-screen`. `AppController.TranslateScreenAreaAsync`:

1. Hide the panel and bubble (wait 250 ms if something was visible or the tray menu was used), then freeze-frame
   every monitor with GDI `BitBlt` + `CAPTUREBLT` from the screen DC in physical pixels (`Platform/Capture/ScreenSnapshot`).
2. `UI/ScreenCapture/ScreenAreaSelector` shows one `SelectionOverlayWindow` per monitor: borderless topmost tool
   window whose bounds are forced to the monitor's pixels in `WM_WINDOWPOSCHANGING` (no DIP rounding, unchanged
   by `WM_DPICHANGED`), showing the frozen image dimmed, crosshair cursor, a hint capsule. The drag rectangle is
   undimmed with an accent border and a "W × H" label; it stays on the monitor where it started
   (`SelectionGeometry`, unit-tested with 100% and 150%). Esc, right click, Alt+F4, a click smaller than 8 DIPs,
   or switching to another app cancels. Other global shortcuts are ignored while the overlays are up.
3. The selection is cropped (`BgraImage`) and `Platform/Ocr/ScreenTextRecognizer` runs off the UI thread:
   - `OcrRecognizerChooser.Plan`: an explicit panel source language uses its recognizer, else an installed one for
     the same script, else the bubble reports the missing language. `auto` runs the user-profile recognizer plus
     one per script: Latin (profile or English), Cyrillic (ru, uk), Japanese, Simplified Chinese, Korean.
   - `OcrScaling`: ×3 when the region is under 60 px tall; one retry at ×2–3 when nothing was found or the median
     line is under 20 px; always within `OcrEngine.MaxImageDimension`. Catmull-Rom resampling, then 16 px of
     padding in the dominant edge color (OCR misses glyphs touching the edge), `SoftwareBitmap` Bgra8 premultiplied.
   - `OcrRecognizerChooser.PickBest`: a recognizer applied to another script returns look-alike gibberish
     ("nepeBoA"), so results are scored by letters of the recognizer's own script, with penalties for digits
     inside words, case flips, mixed Latin/Cyrillic and replacement characters.
   - `OcrTextAssembler` rebuilds paragraphs from word boxes: joins lines with spaces (Chinese/Japanese without),
     merges end-of-line hyphenation, starts a new paragraph on a large gap, an indent, a bullet, or after a line
     that ended early. Paragraphs are separated by a newline.
4. The bubble (`BubbleSource.Screen`) is anchored to the selection with `WindowPlacement.PlaceNearAnchor` and shows
   "Recognizing text…", then the usual flow: `TranslatorModel.InputText` + `Translate()` (history as usual).
   There is no "Replace"; "Original" toggles the recognized text (copy and speak act on what is shown); Expand
   opens the panel with the recognized text. No text / no suitable recognizer show a notice with a link to
   `ms-settings:regionlanguage`. Closing the bubble while recognizing drops the result.
5. Pixels are zeroed after use (snapshot, crop, scaled copies, the overlay bitmap, the GDI DIB section).

OCR languages come with Windows language features ("Optical character recognition"); installing one needs
Windows Settings or `Add-WindowsCapability -Online -Name "Language.OCR~~~<tag>~0.0.1.0"` (administrator).

## Offline catalog

One language = a model to English and a model from English (`Catalog/OfflineModelCatalog.cs`).
Files: `onnx/encoder_model_quantized.onnx`, `onnx/decoder_model_merged_quantized.onnx`, SentencePiece
models and `vocab.json`; SHA-256 verified against the Hub, resumable downloads, per-model `manifest.json`.

| Language | → en | en → | Notes |
|---|---|---|---|
| ru es de fr it uk | `Xenova/opus-mt-xx-en` | `Xenova/opus-mt-en-xx` | |
| zh-CN | `Xenova/opus-mt-zh-en` | `Xenova/opus-mt-en-zh` | target token `>>cmn_Hans<<` |
| pt | `Xenova/opus-mt-ROMANCE-en` | `Xenova/opus-mt-en-ROMANCE` | target token `>>pt_BR<<` |
| ja | `Xenova/opus-mt-ja-en` | `Xenova/opus-mt-en-mul` | `>>jpn<<`, basic quality |
| tr | `Xenova/opus-mt-tr-en` | `Xenova/opus-mt-en-mul` | `>>tur<<`, basic quality |
| ko | `Xenova/opus-mt-ko-en` | `noticemkjung/opus-mt-tc-big-en-ko-ONNX` | tokenizer files from `Helsinki-NLP/opus-mt-tc-big-en-ko`; ≈430 MB |

Greedy decoding with KV cache; long input is split into sentences (≤160 source tokens per call).
Up to 3 models stay loaded (≈400 MB each) and are released after 10 idle minutes.

## CLI

| Command | Purpose |
|---|---|
| `Translator.exe` | normal start (shows first-run window once) |
| `Translator.exe --autostart` | start silently at sign-in |
| `Translator.exe --translate "text"` | open the panel and translate (forwards to the running instance) |
| `Translator.exe --translate-screen` | select a screen area and translate its text (forwards to the running instance) |
| `Translator.exe --open panel\|settings\|history\|offline\|firstrun` | open that window (forwards to the running instance, or starts the app and opens it) |
| `Translator.exe --quit` | ask the running instance to exit cleanly and wait until it's gone (exit code 0 also when nothing runs) |
| `Translator.exe --test-translate "text"` | print the Google translation to the console and exit |
| `Translator.exe --test-offline "text" [source] target` | print the offline translation and exit |

`--quit` never starts the app: it checks the single-instance mutex, sends the flag over the pipe and
polls until the mutex is gone (then briefly waits for processes of the same exe). The installer runs it in
`PrepareToInstall` (not for 1.0.0, which predates the flag) and `InitializeUninstall`;
`CloseApplications=force` stays as the fallback.

`TRANSLATOR_DATA_DIR` (environment variable) moves settings, history, models and logs into one directory
(`settings.json`, `history.json`, `models\`, `logs\`) for tests and portable use. The single-instance
mutex/pipe names get a hash of that directory, so such an instance never talks to the default one;
`tools/OfflineCli` honors the variable too.

## Compatibility testing

`.github/workflows/ui-smoke.yml` runs `tests/smoke/ui-smoke.ps1` on hosted runners:

| Runner label | Stands in for | What it exercises |
|---|---|---|
| `windows-2022` (x64) | Windows 10 21H2 generation (build 20348) | no DWM system backdrop, no Segoe Fluent Icons / Segoe UI Variable, installer install/uninstall |
| `windows-2025` (x64) | Windows 11 24H2 generation (build 26100) | Mica/Acrylic path |
| `windows-11-arm` (win-arm64) | Windows 11 on ARM64 | native ARM64 WPF and ONNX Runtime (checked via the PE machine type) |

The hosted ARM64 image keeps its interactive session at the first-sign-in (OOBE) screen: there is no
notification area and full-screen captures show only OOBE. The script therefore also saves each app window
with `PrintWindow` (`*-window.png`), and tray icon removal is only verified where the icon could be added.

The script publishes the app for the runner RID, opens every window in every theme with `--open` under
an isolated `TRANSLATOR_DATA_DIR`, captures the virtual screen, fails on unhandled exceptions in
`debug.log`, checks that every icon glyph exists in Segoe MDL2 Assets, runs `--test-translate` (HTTP 429 /
network errors are warnings) and `--test-offline` after downloading `ru`, and on `windows-2022` installs,
starts and uninstalls the Inno Setup build. Screenshots and logs are uploaded as workflow artifacts.

## Localization keys

`L10n` contains every key below in all 12 UI languages (`ru en es de fr it pt zh ja ko tr uk`).
The English text is the source of truth for meaning.

| Key | English |
|---|---|
| app.title | Translator |
| app.subtitle | 12 languages · online and offline |
| engine.google.privacy | Google online: text was sent to the internet |
| engine.offline.privacy | Offline: text was processed on this PC |
| engine.offline.only | Offline only: Google is disabled |
| engine.pending | Engine will be selected on translation |
| engine.google.label | Google · online |
| engine.offline.label | Offline · on this PC |
| auto | Auto |
| auto.detected | Auto ({0}) |
| translate | Translate |
| input.placeholder | Enter text — or select it in any app and press {0} |
| result.placeholder | Translation will appear here |
| offline.languages | Offline languages… |
| base.language | base language |
| offline.intro | English is used as the base language. Other languages are downloaded once and then work without internet. |
| offline.disk.note | Each offline language takes about 200–250 MB of disk space. Long offline translations are slower and use more CPU. |
| later | Later |
| copy.translation | Copy translation |
| copied | Copied |
| speak.source | Speak source text |
| speak.translation | Speak translation |
| clear | Clear |
| history | Translation history |
| history.subtitle | latest {0} are stored |
| history.empty | Empty for now — translations will appear here |
| replace | Replace |
| replace.help | Insert translation instead of selected text |
| expand.panel | Open in large panel |
| translating | Translating… |
| settings | Settings |
| quit | Quit |
| open.panel | Open translator |
| swap.languages | Swap languages |
| settings.subtitle | hotkeys and behavior |
| section.language | Language |
| section.theme | Theme |
| section.panel | Panel |
| section.hotkeys | Hotkeys |
| section.behavior | Behavior |
| section.updates | Updates |
| language.system | System |
| language.caption | Uses the Windows language automatically or a manual choice |
| hotkey.selection | Translate selected text |
| hotkey.selection.caption | Copies the selection with Ctrl+C and shows the translation next to it |
| hotkey.clipboard | Translate clipboard |
| hotkey.clipboard.caption | Translates what you have already copied |
| hotkey.panel | Show translator panel |
| hotkey.panel.caption | Opens the panel from any app |
| hotkey.screen | Translate screen area |
| hotkey.screen.caption | Select an area on screen: the text is recognized on this PC and translated |
| hotkey.recording | Press a shortcut… |
| hotkey.recorder.help | Click, then press a new shortcut. Esc — cancel, Backspace — turn off. |
| hotkey.none | Off |
| hotkey.taken | {0} is already used by another app. Choose a different shortcut. |
| hotkey.admin.note | Text can't be read from apps running as administrator unless Translator also runs as administrator. |
| auto.translate | Translate automatically while typing |
| offline.only | Offline only |
| offline.only.caption | Google is disabled; text is not sent online |
| launch.login | Launch at Windows sign-in |
| error.autostart | Couldn't change autostart: {0} |
| version.footer | Translator {0} · Google (online) + Opus-MT (offline) |
| check.version | Check version |
| checking | Checking… |
| check | Check |
| update.caption | Manual check against GitHub Releases |
| update.available | Version {0} is available. |
| update.latest | You have the latest version ({0}). |
| update.failed | Couldn't check for updates: {0} |
| update.download | Download |
| panel.compact | Compact |
| panel.standard | Standard |
| panel.wide | Wide |
| panel.compact.caption | quick translation |
| panel.standard.caption | balanced size and reading |
| panel.wide.caption | long phrases and paragraphs |
| theme.calm.caption | soft white glass with a green accent |
| theme.neon.caption | dark glass with a neon glow |
| theme.frost.caption | light frosted glass |
| first.title | First launch |
| first.subtitle | set up the translator for your workflow |
| first.tray.title | Tray icon |
| first.tray.caption | Translator lives in the notification area next to the clock. If the icon is hidden, click ^ and drag it to the taskbar. |
| first.hotkeys.caption | {0} — selected text, {1} — clipboard, {2} — translator panel, {3} — screen area |
| offline.download.caption | Download needed languages for translation without internet |
| open | Open… |
| done | Done |
| downloaded | downloaded |
| unsupported | unsupported |
| download | Download |
| update | Update |
| close | Close |
| offline.remove | Remove |
| offline.cancel | Cancel |
| offline.downloading | Downloading… {0}% |
| offline.download.done | Done — the language is downloaded. |
| offline.download.failed | Couldn't download: {0} |
| offline.size | ≈{0} MB |
| offline.quality.basic | basic quality |
| offline.status.checking | Checking offline translation… |
| offline.status.ready | Offline {0} ↔ {1}: ready |
| offline.status.missing | Offline {0} ↔ {1}: not downloaded |
| offline.update.message | Updates are available for offline languages: {0}. |
| notice.offline.only | Offline only mode: text is not sent to the internet. |
| notice.offline.slow | Offline translation of long texts is slower and uses more CPU. |
| error.too.long | Text is too long: max {0} characters at once, now {1}. Split it into parts. |
| error.clipboard.empty | Clipboard is empty: copy some text first (Ctrl+C). |
| error.selection.empty | Couldn't get the selected text — maybe nothing is selected. |
| error.offline.only.missing | Offline only mode is on, but the languages for this pair aren't downloaded. Open “Offline languages…” and download them. |
| error.no.internet | No internet connection, and offline languages for this pair aren't downloaded. Click “Offline languages…” below. |
| error.translate.failed | Couldn't translate: {0} |
| error.http | The service returned HTTP error {0}. |
| error.bad.response | The service returned an unexpected response. |
| error.offline.timeout | The offline engine didn't respond. |
| error.speech.voice | No voice is installed for {0}. Add one in Windows Settings → Time & language → Speech. |
| error.speech.failed | Couldn't speak the text: {0} |
| error.replace.failed | Couldn't insert the translation. Copy it and paste it manually. |
| ocr.overlay.hint | Drag to select the text to translate · Esc — cancel |
| ocr.recognizing | Recognizing text… |
| ocr.no.text | No text found in the selected area |
| ocr.no.text.hint | If the text is in another language, add text recognition for it in Windows settings. |
| ocr.unavailable | Windows has no text recognition (OCR) installed for the app's languages. |
| ocr.language.missing | Text recognition (OCR) for {0} isn't installed in Windows. |
| ocr.install.hint | Open Language settings, choose the language → Language options and install “Optical character recognition” — or add the language first. |
| ocr.open.settings | Language settings |
| ocr.failed | Couldn't recognize text in the selected area. |
| ocr.original | Original |
| ocr.original.help | Show the recognized text |
| ocr.translation | Translation |
| ocr.translation.help | Show the translation |
| ocr.copy.original | Copy recognized text |
| mode.online | Online |
| mode.offline | Offline |
| mode.online.help | Translate with Google; offline models are used without internet |

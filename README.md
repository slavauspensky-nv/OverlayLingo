# OverlayLingo

<p><img src="icon-preview.png" width="88" height="88" alt="OverlayLingo icon" align="left" hspace="12"></p>

**Live translation of what you type — right under the caret.** Type a phrase in any Windows app and an EN↔RU overlay appears next to it. Click the overlay to copy the translation, or press the button to replace the text in the field with it. No window switching, no copy-pasting into a separate translator.

`Windows 10/11 x64` · `.NET 8` · `MIT` · no account or API key required for Google

> Русская версия — [README.ru.md](README.ru.md)

---

## Features

### Translation from the active field

- **Reads the focused field through UI Automation** (`TextPattern` / `ValuePattern`) and tracks the caret rectangle; a collapsed caret is widened to a full character so the overlay lands precisely.
- **Always-on-top overlay that never takes focus** — keep typing, the caret stays in your field.
- **Click the overlay to copy** the translation to the clipboard.
- **Replace button** puts the translation back into the field and verifies the text actually changed (`replace check: успешно` in the log).
- **130 ms typing pause** (configurable) so requests fire after you stop typing, not on every keystroke.
- **Reopen the last translation** with a hotkey after the overlay was closed.

### Translation services

- **Google without an API key** — two public endpoints plus MyMemory as a fallback if one of them fails.
- **DeepL** with a personal API key (both free and pro keys are supported).
- **Any source language, detected automatically** — Google is called with `sl=auto` and DeepL detects the source itself, so the text can be in any language those services know (Spanish, German, French, Japanese, Chinese, and so on); the result comes out in Russian or English, picked from the script of the text. The settings window has a "Check connection" button.
- **LRU cache of 2000 phrases** with least-recently-used eviction: repeated phrases come back instantly.
- **One request in flight** and a 7-second timeout, so the UI never freezes on a slow network.

### Screen capture translation

- **Ctrl + Alt + S** — select a screen region, OverlayLingo recognizes the text and translates it.
- **OCR runs locally** through `Windows.Media.Ocr` (`ocr.ps1`) — the image never leaves the machine. The `en-US` and `ru` language packs are used; other writing systems need the matching OCR pack installed in Windows (Settings → Time & language).
- **Any language on the screen** — the recognized text goes through the same auto-detect pipeline, so a Spanish, German or French page comes out in Russian or English. The "Screenshot language" setting only forces the recognition language for a tricky screenshot.
- In the result window: **click a word for a reverse translation**, **double-click to save the pair to your notebook**, `Esc` to close; the window collapses to its title bar and resizes from the edges.

### Look and sound

- **25 fonts** (Press Start 2P by default, plus Roboto, JetBrains Mono, Montserrat, PT Sans, Lora and others).
- **Text and background colors, opacity, font size, window size** — with a live preview.
- **Typewriter effect** — the translation is revealed character by character at an adjustable speed.
- **5 sets of real typing recordings** (typewriter, mechanical, laptop, retro, membrane), volume 0–100 %, with a "Preview" button.
- **Window position** — next to the selection or remembered between runs; reset to the default is one click away.

### Study mode built from your own words

- **`OverlayLingo.txt` on the Desktop** — words are appended automatically and grouped under a date header (e.g. "17 ноября 2026").
- **Quiz**: EN→RU and RU→EN, "Day" and "All days" modes, rounds with a score and a percentage, a grade for the round, and a replay with different words.
- The word list is read straight from the notebook, so you can add your own pairs in the `cat — кот` format.

### Where it works

- **Regular desktop apps** (Word, Notepad, messengers, CRM) — via UI Automation, with an exact caret position.
- **Chrome, Telegram, Electron**: OverlayLingo tells the system that a screen reader is present (`SPI_SETSCREENREADER`) and Chromium starts exposing the field text; the setting is restored on exit. There is also a manual route: `chrome://accessibility` → "Native accessibility API support".
- **Fallback mode** — passive keyboard capture for apps that expose nothing: text is assembled from keystrokes and the overlay appears next to the mouse cursor.
- **Password fields are skipped** on purpose — UIA marks them as protected.

### System integration

- **Tray icon** with a menu: enable/disable translation, study, settings, diagnostics, about, exit.
- **Start with Windows**.
- **Toggle translation** with a single hotkey, no restart.
- **Settings live in `settings.json`** next to the executable.

## Hotkeys

| Shortcut | Action |
|---|---|
| `Ctrl + Alt + T` | enable / disable translation (configurable) |
| `Ctrl + Alt + S` | translate a selected screen region |
| `Ctrl + Alt + R` | reopen the last translation window (configurable) |

## How it works

1. A watcher polls the focused field and reads its text plus the caret rectangle through UI Automation.
2. After a pause (130 ms by default) the text goes to the translation service — Google, MyMemory or DeepL.
3. The answer is cached and shown in a topmost overlay that does not steal focus.
4. A click copies it, the button replaces the field text, and a double-click in the screenshot window saves the word to the notebook.

## Installation

1. Download `OverlayLingo-1.0.1-win-x64.zip` from [Releases](../../releases) and unpack it into any folder.
2. Run `OverlayLingo.exe` — the app lives in the tray and opens no windows.
3. Requires [.NET Desktop Runtime 8 (x64)](https://dotnet.microsoft.com/download/dotnet/8.0).

## Build from source

You need Windows 10/11 x64 and the .NET SDK 8.0 (the project is WPF + WinForms, `net8.0-windows`).

```powershell
git clone https://github.com/slavauspensky-nv/OverlayLingo.git
cd OverlayLingo
dotnet build -c Release
```

The executable is `bin\Release\net8.0-windows\OverlayLingo.exe`; it needs `OverlayLingo.dll`, `OverlayLingo.runtimeconfig.json`, `OverlayLingo.deps.json` and the `fonts\` and `sounds\` folders next to it.

## Privacy

- Text is sent only to the translation service you selected; the OCR image never leaves your machine.
- **Passive keyboard capture sees everything you type, including passwords** — it is enabled by a checkbox in settings and can be turned off there. Disable it if you don't need it.
- Fields marked as password inputs are skipped even when read through UI Automation.

## Limitations

- Apps running as administrator are unreachable while OverlayLingo runs without elevation.
- Terminals, consoles and Java applications do not expose text through UIA.
- For Chromium apps the caret coordinates are unavailable, so the overlay appears next to the mouse cursor.
- Google without a key may throttle requests; for steady use, a DeepL key is the better option.
- The MyMemory fallback only handles RU/EN pairs — other source languages rely on Google or DeepL.
- Only the target is fixed to Russian or English; OCR reads the languages whose Windows packs are installed.

## Diagnostics

`overlaylingo.log` is written next to the executable. Command-line modes:

```
OverlayLingo.exe --probe 30      # for 30 seconds dump everything UIA sees to probe.log
OverlayLingo.exe --selftest      # read text/caret from its own field → selftest.log
OverlayLingo.exe --translate     # exercise the translation service → translate.log
OverlayLingo.exe --demo          # draw the overlay and save overlay.png
OverlayLingo.exe --hooktest      # test keyboard capture → hooktest.log
OverlayLingo.exe --tree 4 chrome # dump an app's accessibility tree → tree.log
OverlayLingo.exe --parsetest     # test parsers for Google responses → parsetest.log
OverlayLingo.exe --icontest      # test the tray icon → trayicon.png / .ico
OverlayLingo.exe --fieldtest     # inspect the focused field → fieldtest.log
```

Key log lines:

| Line | Meaning |
|---|---|
| `started`, `build ...` | startup and build timestamp |
| `tray: создан` | tray icon registered |
| `accessibility: request=... error=...` | attempt to enable accessibility for Chromium apps |
| `keyboard hook: установлен` | passive keyboard capture is active |
| `snap: field=... proc=...` | focused field or application changed |
| `field proc=Notepad ... text="..."` | field text discovered |
| `hook: "..."` | text reconstructed from keystrokes |
| `ok: "..."` | translation received |
| `replace check: успешно` / `НЕ СРАБОТАЛО` | whether the replacement landed in the field |
| `blind: proc=chrome` | the app exposes no text through UIA |

If `SPI_SETSCREENREADER` is blocked by the system (`error=5` in the log), Chrome accessibility stays off — keyboard capture or the manual `chrome://accessibility` toggle is the way around it.

## Fonts

The `fonts/` folder holds 25 Google Fonts used by the overlay and the study window: Alegreya, Caveat, Comfortaa, Fira Code, Fira Sans, JetBrains Mono, Lato, Lora, Marck Script, Montserrat, Noto Sans, Nunito, Open Sans, Oswald, Philosopher, Playfair Display, Press Start 2P, PT Sans, PT Serif, Roboto, Rubik, Russo One, Source Sans 3, Ubuntu, Yeseva One.

Most of them are distributed under the SIL Open Font License 1.1 (OFL): free to use and embed in an application as long as the license terms are kept and the fonts are not sold on their own. Before a public release, check the license on each font's Google Fonts page.

## License

[MIT](LICENSE) © 2026 Vladislav Uspensky, Vyacheslav Uspensky.

# Contributing

Bug reports, fixes and ideas are welcome. For anything large, please open an issue first, so we can agree on it before you spend time.

## Setup

You need Windows 10 or 11 and nothing else. Run `build.bat` and it creates `PromptPilot.exe`.

The app uses the C# compiler built into Windows, which supports **C# 5 only**: no `$"..."`, `?.`, `nameof` or expression-bodied members. Please don't add NuGet packages, since one small `.exe` with no dependencies is a core goal.

## Layout

| File | Purpose |
|---|---|
| `src/Core.cs` | Settings, default modes and templates, history |
| `src/Ai.cs` | Providers, prompts, privacy shield, prompt score, diff |
| `src/Win.cs` | Windows hooks, app detection, reading and pasting text |
| `src/Widget.cs` | The floating widget |
| `src/App.cs` | Tray, hotkeys, rewrite flow, startup, self-checks |
| `src/Install.cs` | Start menu and Settings registration |
| `src/Dialogs.cs` | Preview, questions, template palette |
| `src/Ui.cs`, `src/MainWindow.cs` | Main window |

## Testing

Always test with a separate data folder, so your own settings stay untouched:

```bat
PromptPilot.exe --data %TEMP%\pp-dev
PromptPilot.exe --data %TEMP%\pp-dev --selftest %TEMP%\result.txt
PromptPilot.exe --data %TEMP%\pp-dev --shot %TEMP%\pages
```

`--selftest` writes `ok` and the check count, or lists what failed. `--shot` renders every page to images without taking focus.

The app presses keys in whichever window is in front. If you script a test, check the front window before every simulated key, or your keystrokes can land in a real chat.

## Principles

- **No idle work.** No polling timers. React to Windows events.
- **Never lose the user's text.** If anything is uncertain, leave the text alone and put the result on the clipboard.
- **Nothing is sent without a click.**
- **Keep it small.** Add a feature only if it earns its weight.

## Pull requests

Keep each one focused, match the surrounding style, add a self-check for new logic, and say how you tested it. Never commit API keys, your `settings.json` or your history.

By contributing, you agree that your work is released under the MIT licence.

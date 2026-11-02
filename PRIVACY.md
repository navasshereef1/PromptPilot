# Privacy

PromptPilot has no account, no server of its own and no analytics. The author receives none of your data.

## What it reads

- **Your chat box**, only when you click a button or press a hotkey. It doesn't watch your typing.
- **Your clipboard**, because it copies and pastes to read and replace text. Your clipboard is put back afterwards. If your text changed while it worked, the result stays on the clipboard for you to paste.
- **The active window's title and browser address**, to tell whether you're in an AI chat. This stays on your PC.
- **The current chat**, only if you turn on *Read the current chat*. It's off by default and works in browsers only.
- **Files you attach to a project**, each time a mode that uses context runs.

## What leaves your PC

When you click, these go to the AI provider you set up:

- your draft
- for modes that use context, your About me, the active project's notes and files, and the chat or clipboard text if you turned those on

The **Context** page has a button that shows exactly what would be sent. **Fix** sends only your draft.

Connections to the provider are also opened in advance to speed up your first click. These carry no prompt text. You can turn this off under **Privacy & System**.

Each provider's own terms apply, on your own account. Some free tiers, including Google's free Gemini tier, may use prompts to improve their products. To keep everything on your PC, use Ollama.

## The privacy shield

By default, emails, phone numbers, API keys, passwords, card numbers, PAN and Aadhaar numbers are replaced with placeholders such as `[EMAIL_1]` before sending, and restored in the result. It works by pattern matching, so it can miss things. It doesn't hide names, addresses or company details.

## What's stored on your PC

Everything is in `%APPDATA%\PromptPilot`:

| File | Contents |
|---|---|
| `settings.json` | Settings, modes, templates, About me and projects. API keys are encrypted for your Windows account |
| `history.jsonl` | Your rewrites, **in plain text**, kept for 90 days by default. Can be turned off or cleared |
| `log.txt` | Errors only, never your prompts |

Exporting settings leaves out your API keys.

## Removing it

Uninstall from Settings > Apps, then delete `%APPDATA%\PromptPilot` and the folder holding `PromptPilot.exe`.

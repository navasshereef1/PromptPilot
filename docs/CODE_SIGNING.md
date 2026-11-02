# Code signing policy

**Current status:** PromptPilot releases are not code-signed yet, so Windows may show a warning on direct downloads. An application for free open-source code signing is planned. This page will be updated if it's approved.

## How releases are made

- Every release is built by GitHub Actions from the public source code in this repository. The build steps are in [`build.yml`](../.github/workflows/build.yml) and [`build.bat`](../build.bat).
- Each release has a SHA-256 checksum and a public build record, which you can verify with:

  ```bash
  gh attestation verify PromptPilot.exe --repo navasshereef1/PromptPilot
  ```
- The release workflow runs the self-checks before publishing.
- The code is never packed or obfuscated, so security tools can inspect it normally.

## Team

| Role | Who |
|---|---|
| Author and committer | [Navas Shereef](https://github.com/navasshereef1) |
| Reviewer of outside contributions | [Navas Shereef](https://github.com/navasshereef1) |
| Release approver | [Navas Shereef](https://github.com/navasshereef1) |

## Privacy

PromptPilot doesn't send information to any networked system without a user request, other than opening a connection to the AI providers the user set up, which carries no prompt text and can be turned off. See the [privacy notes](../PRIVACY.md).

## Reporting a false alarm

If your antivirus flags PromptPilot, please [open an issue](https://github.com/navasshereef1/PromptPilot/issues) with the product name and the detection name. It presses keys and uses the clipboard only when you click, which is how it replaces your text.

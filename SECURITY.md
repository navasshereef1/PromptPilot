# Security

To report a security problem, please use [GitHub's private vulnerability reporting](https://github.com/navasshereef1/PromptPilot/security/advisories/new) rather than a public issue.

Include what you found, how to reproduce it and what it could affect. This is a one-person project, so replies are best effort.

Only the latest release gets fixes.

## Checking a download

Each release includes a SHA-256 checksum. In PowerShell:

```powershell
Get-FileHash .\PromptPilot.exe -Algorithm SHA256
```

Only download from this repository's Releases page. The app isn't code-signed yet, so Windows may show a warning. You can also read the source and build it yourself with `build.bat`.

To confirm GitHub built a release from this repository's source:

```bash
gh attestation verify PromptPilot.exe --repo navasshereef1/PromptPilot
```

If you think an API key has leaked, revoke it on the provider's website and create a new one.

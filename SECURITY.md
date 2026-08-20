# Security Policy

## Supported versions

| Version | Supported          |
| ------- | ------------------ |
| 1.4.x   | :white_check_mark: |
| 1.3.x   | :white_check_mark: |
| 1.0.x   | :x:                |

## Reporting a vulnerability

If you discover a security issue in v2rayF, please **do not** open a public GitHub issue with exploit details.

Instead:

1. Open a [GitHub Security Advisory](https://github.com/drmikecrypto/v2rayF/security/advisories/new) (preferred), or
2. Email the maintainer via the contact listed on the [GitHub profile](https://github.com/drmikecrypto).

Include:

- A clear description of the issue
- Steps to reproduce
- Impact assessment (if known)
- Suggested fix (optional)

We aim to acknowledge reports within **72 hours** and provide a fix or mitigation timeline when possible.

## Scope

- v2rayF application code in this repository
- Build and release workflows in `.github/`

Out of scope: vulnerabilities in [Xray-core](https://github.com/XTLS/Xray-core) itself (report upstream).

## Safe use

Use v2rayF only on networks and servers you are authorized to access. Proxy tools can route sensitive traffic; keep your share links and subscription URLs private.

### At-rest encryption (1.4+)

Server UUIDs/passwords, subscription URLs, and Secure Share passwords are stored encrypted under the app data directory (AES-GCM). The AES key is wrapped with:

- **Windows** — DPAPI (CurrentUser)
- **Android** — Android Keystore
- **macOS / Linux** — key file with restrictive permissions (`0600`)

Encrypted `.v2rayf` vault files use a user passphrase (PBKDF2 + AES-GCM). Prefer vault export over clipboard when moving profiles between devices.

### Updates (1.4+)

In-app updates download only from allowed GitHub hosts, verify **SHA256** (release digest or `SHA256SUMS`), and extract with Zip-Slip rejection. Android also checks APK signing fingerprints.

Windows builds can be Authenticode-signed with [Microsoft Artifact Signing](docs/tips/windows-smartscreen.md) so SmartScreen sees a real publisher (GitHub hosting alone does not establish trust).

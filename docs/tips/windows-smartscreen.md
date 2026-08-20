# Windows SmartScreen & Authenticode

GitHub Releases alone do **not** make Windows trust `v2rayF.exe`. SmartScreen looks at **publisher identity** (Authenticode), **publisher reputation**, and **file/hash reputation** — not “hosted on GitHub.”

## What users see today (unsigned)

**Windows protected your PC** → More info → **Run anyway**. That is normal for an unsigned Avalonia `.exe` zip until you sign with a real certificate and build reputation.

## What we do in CI

Release workflow (`.github/workflows/release.yml`) can sign **`v2rayF.exe`** with [Microsoft Artifact Signing](https://learn.microsoft.com/en-us/azure/artifact-signing/) (formerly Trusted Signing) on `win-x64` / `win-arm64` jobs, then re-zip the package.

We intentionally **do not** re-sign bundled `cores\xray.exe` / `sing-box.exe` (third-party). Signing your own launcher is what SmartScreen cares about for “Run anyway.”

Signing is **off by default** until you finish Azure identity verification and flip a repo variable (so tags still release while you set things up).

## One-time Azure setup (you must do this)

1. **Azure subscription** with billing enabled.
2. Create an **Artifact Signing** account + **public trust** certificate profile (identity verification required — individual or organization).  
   Docs: [Quickstart](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart) · [Identity validation](https://learn.microsoft.com/en-us/azure/artifact-signing/how-to-identity-validation).
3. Note:
   - Endpoint (region URI), e.g. `https://eus.codesigning.azure.net/`
   - Signing account name
   - Certificate profile name
4. Create an **App registration** (service principal) and grant it **Artifact Signing Certificate Profile Signer** on that account.
5. Prefer **federated credentials** (OIDC) for GitHub Actions — no long-lived client secret:
   - Issuer: `https://token.actions.githubusercontent.com`
   - Subject: `repo:drmikecrypto/v2rayF:ref:refs/tags/v*` (or `environment:…` / `repo:…:environment:release`)
   - Audience: `api://AzureADTokenExchange`

Pricing is consumption-based (Basic plan is typically on the order of ~$10/month + signatures). Confirm current pricing on Azure.

## GitHub repository configuration

### Secrets (Settings → Secrets and variables → Actions)

| Secret | Value |
|--------|--------|
| `AZURE_CLIENT_ID` | App registration application (client) ID |
| `AZURE_TENANT_ID` | Directory (tenant) ID |
| `AZURE_SUBSCRIPTION_ID` | Subscription ID |

### Variables (Settings → Secrets and variables → Actions → Variables)

| Variable | Example |
|----------|---------|
| `ARTIFACT_SIGNING_ENABLED` | `true` |
| `ARTIFACT_SIGNING_ENDPOINT` | `https://eus.codesigning.azure.net/` (match your region) |
| `ARTIFACT_SIGNING_ACCOUNT` | your signing account name |
| `ARTIFACT_SIGNING_CERT_PROFILE` | your certificate profile name |

When `ARTIFACT_SIGNING_ENABLED` is not `true`, Windows zips ship **unsigned** and the job prints a notice.

## After the first signed release

1. Download `v2rayF-win-x64.zip`, extract, right-click `v2rayF.exe` → Properties → **Digital Signatures** — should show your publisher name.
2. SmartScreen may still warn for a while on a **new** publisher/hash. Reputation builds from legitimate downloads and clean behavior; keep using the **same** Artifact Signing identity on every release.
3. Optional later: MSIX + Microsoft Store (Store signing removes the typical SmartScreen download warning) — separate packaging work.

## Local verify (after a signed build)

```powershell
Get-AuthenticodeSignature .\v2rayF.exe | Format-List *
```

Expect `Status : Valid` and a Subject that matches your verified publisher.

## Related

- [SmartScreen reputation for developers](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)
- [Artifact Signing GitHub Action](https://github.com/Azure/artifact-signing-action)
- macOS equivalent friction: [macos-gatekeeper.md](macos-gatekeeper.md)

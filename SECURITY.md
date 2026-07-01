# Security — threat model & posture

This document describes Mapping Studio's security architecture, the threats it defends against, and the
posture decisions (including what is deliberately out of scope). Operational setup lives in
[INSTALL.md](INSTALL.md); the design reference is §15a of the
[architecture document](DataMappingLineageApp-Architecture.md).

## What the app protects

Mapping Studio manages **metadata** (data models, mappings, lineage) — not the underlying business data.
Classification/tokenization of sensitive data is applied by the downstream ETL engine, not here. The
assets to protect are therefore: the metadata itself (integrity + attribution), the availability of the
shared folder pipeline, and the credentials/sessions of its users.

## Architecture recap

- **Web host (`App.Web`)** — multi-user Blazor Server. Authentication is **opt-in** (`Auth:Require`):
  off = a single fully-privileged local workspace (development only); on = the security module.
- **Security store** — ASP.NET Core Identity over EF Core in an **isolated** database (SQLite or SQL
  Server), separate from the domain store, never synced to the shared folder. Schema ships as **EF
  migrations** (one migrations assembly per provider). Data Protection keys persist there so multiple
  hosts share a key ring.
- **Desktop host (`App.Desktop`)** — deliberately **single-user**: it runs as the local OS account with a
  private working copy and full permissions. There is no sign-in on the desktop; its trust boundary is
  the Windows session itself, and its writes are attributed to the OS account. Organizations that need
  per-user authorization use the web host; the desktop is the offline/power-user shell.

## Controls by threat

| Threat | Control |
|---|---|
| Credential guessing / stuffing | Password policy (length/character classes, configurable); per-account **lockout**; per-IP **rate limiting** on all auth POST endpoints; account-enumeration-safe reset + login messages |
| Credential theft (phishing, reuse) | **TOTP two-factor** (opt-in per user, or **enforced per role** via `Auth:Providers:Local:Mfa:Require` = `Administrators`/`All`; unenrolled users are confined to the account pages until they enrol); one-time recovery codes |
| Session theft / fixation | HttpOnly, SameSite cookies; Identity rotates the cookie on sign-in; **security-stamp validation** every 2 minutes so revoked/disabled users drop off; admin **Revoke sessions** |
| Privilege escalation | Server-side **policy-per-permission** authorization on every gated operation (UI gating is convenience, not the control); system roles protected; the **last administrator cannot be demoted**; permission claims are computed server-side at sign-in |
| XSS / injection | Strict **Content-Security-Policy** (no inline/eval scripts, self-hosted only, `frame-ancestors 'none'`), `X-Content-Type-Options`, `Referrer-Policy`; Blazor's default output encoding; EF Core parameterized queries |
| CSRF | Antiforgery tokens on every state-changing account form (validated explicitly), SameSite cookies |
| Open redirect | `returnUrl` accepted only when site-relative (single leading `/`, no scheme) |
| Secrets exposure | Client secrets / SMTP / bootstrap-admin passwords are supplied **out-of-band** (user-secrets, env, vault) — never in `appsettings.json`; the audit log records events, not secrets |
| Tampering with audit | The security audit is append-only through the app; the domain change-log fold is the domain audit (architecture §8) |
| Supply chain | `build/check-dependencies.ps1` (CI gate) fails on known-vulnerable packages; transitive pins where needed |
| Multi-host token confusion | Shared Data Protection key ring in the security store + shared `ApplicationName` — cookies issued by any host validate on all |

## Federation (SSO)

OpenID Connect providers (Entra ID / Google / Okta / generic) use **authorization code + PKCE**; the
callback resolves to a local account (existing link → verified-email link → JIT provisioning with a
default role). IdP group/role claims map to app roles **additively** — an IdP can grant, never silently
revoke, locally-assigned roles. Provider secrets are config references, supplied out-of-band.

## Deliberately out of scope (with rationale)

- **Passkeys / WebAuthn** — valuable, but requires an attestation library (e.g. Fido2NetLib) and
  origin-bound configuration; planned as an additive provider once the deployment origin story (TLS,
  domains) is fixed by the hosting organization.
- **SAML 2.0** — modern deployments federate via OIDC; SAML would only be added for a legacy IdP
  requirement (e.g. via Sustainsys.Saml2).
- **Per-table / per-column ACLs** — the current model authorizes *operations* (view/edit/publish/import)
  app-wide. Entity-level ACLs are designed (role × table → permission, enforced in the store facade) but
  deferred until a concrete need exists; the metadata-driven catalog makes them retrofittable.
- **Runtime provider-config editor** — auth providers are configuration (reviewed, versioned, secretless);
  editing identity-provider settings from the web UI increases attack surface for little operational gain.
- **Desktop sign-in** — see above: the desktop's trust boundary is the OS session by design.

## Operational requirements (deploy-time)

1. Serve over **TLS** (reverse proxy or Kestrel certs) and set `Auth:Require=true` outside localhost.
2. Set the **bootstrap admin password** out-of-band on first run; rotate it after sign-in.
3. Multi-host: point every host at the **same security store** and `ApplicationName`.
4. Configure **SMTP** (`Auth:Email`) before enabling self-service reset/registration.
5. Protect the security DB file/server with OS/DB ACLs; it contains password hashes and DP keys.
6. Keep the dependency scan in CI green.

## Reporting

This is an internal tool; report suspected vulnerabilities to the repository owner (see LICENSE holder).

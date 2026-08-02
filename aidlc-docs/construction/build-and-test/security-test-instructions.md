# Security Test Instructions

Generated because the **Security Baseline extension is enabled and blocking** for this project. Each
section maps to the SECURITY rule it verifies.

---

## 1. Dependency and supply-chain scanning (SECURITY-10)

```bash
dotnet list shared/EventManager.Shared.slnx package --vulnerable --include-transitive
dotnet list backend/EventManager.Backend.slnx package --vulnerable --include-transitive
dotnet list admin/EventManager.Admin.slnx package --vulnerable --include-transitive
dotnet list judge/EventManager.Judge.slnx package --vulnerable --include-transitive
dotnet list checkin/EventManager.Checkin.slnx package --vulnerable --include-transitive
```

**Expected**: no vulnerable packages. Versions are pinned centrally in `Directory.Packages.props`;
`SQLitePCLRaw.bundle_e_sqlite3` carries a security pin with its rationale recorded there.

Also confirm the Dockerfile pins base images by digest or explicit tag — never `latest`.

> **Gaps**: no automated vulnerability scan step in CI, and **no SBOM generation**. SECURITY-10
> requires an SBOM for production deployments; `.github/workflows/backend.yml` has a placeholder
> comment where `docker sbom`/scan should run. Both are outstanding.

---

## 2. Authorization and access control (SECURITY-08)

This is the highest-value area — U9 is an authorization surface, and two blocking findings were
caught during its design.

**Automated** — already covered:
```bash
dotnet test backend/EventManager.Backend.slnx --filter "FullyQualifiedName~ReadNonDisclosure"
dotnet test backend/EventManager.Backend.slnx --filter "FullyQualifiedName~Rbac"
dotnet test backend/EventManager.Backend.slnx --filter "FullyQualifiedName~ReadProperty"
```

These assert deny-by-default (PBT-3 and U9 P1), that denial is indistinguishable from absence, that
no read endpoint returns 403, and that cross-event ids are never readable.

**Manual probing** — run the Postman **Read API (U9)** folder as a second account that is neither
organizer nor registrant of the target event:

| Probe | Required result |
|---|---|
| Roster of an event you do not administer | 404, **never 403** |
| Account roster of an event you do not administer | 404 |
| A division id from another event under this event's path | 404 |
| An event id that does not exist vs. one you cannot see | **byte-identical** responses |
| Any read endpoint with no bearer token | 401 |
| Another account's registration detail | 404 |

**IDOR checklist**: every U9 resource is addressed under `/api/events/{eventId}/…`, so each id is
authorized against an event the caller holds a tier on. When adding endpoints, keep that shape —
a top-level `/api/divisions/{id}` would bypass the structural defence.

---

## 3. Authentication and credentials (SECURITY-12)

```bash
dotnet test backend/EventManager.Backend.slnx --filter "FullyQualifiedName~SecurityTests"
```

Verify manually:
- Password minimum 8 characters **and** the breached-password validator rejects known-compromised
  passwords (Postman: *Register — breached password (negative)*)
- Account lockout after 5 failed logins, 15-minute window
- TOTP MFA enrol/confirm, and that MFA is required at login once enrolled
- Refresh-token rotation, and that logout revokes them
- Account deletion requires re-authentication and is refused for a sole Full Admin
- **No credential material in any response** — asserted by the U9 account-roster test

---

## 4. Input validation and injection (SECURITY-05)

- All queries go through EF Core with parameterization; **no raw SQL is composed from user input**.
  Verify with `grep -rn "FromSqlRaw\|ExecuteSqlRaw" --include=*.cs .` — expect no results.
- FluentValidation runs on write endpoints before any data access
- U9 read endpoints use route constraints (`{eventId:long}`) so malformed ids are rejected as 400
  before reaching a service

---

## 5. Transport, headers, and misconfiguration (SECURITY-01/04/09)

```bash
curl -kI https://localhost/api/events
```

Confirm `SecurityHeadersMiddleware` emits `Content-Security-Policy`, `Strict-Transport-Security`,
`X-Content-Type-Options: nosniff`, `X-Frame-Options`, and `Referrer-Policy`. Confirm HSTS
`max-age` ≥ 31536000 outside Development.

Confirm error responses are generic: force a 500 and verify **no stack trace, framework version, or
database detail** appears. The global handler in `Program.cs` returns a fixed message.

---

## 6. Rate limiting and abuse (SECURITY-11)

- `login` — 5 requests/minute per IP
- `registration` — 10 requests/hour per IP

Exceed each and confirm 429. Note the **abuse case this design explicitly addresses**: account
enumeration through the event-scoped account endpoint, which is why "any account by id" was
rejected during requirements in favour of roster-only reads.

> **Gap**: the U9 read endpoints carry **no rate limit**. They are authenticated and return 404 for
> unauthorized reads, so enumeration yields nothing, but a rate limit on the read surface would be
> defence in depth worth considering.

---

## 7. Logging and alerting (SECURITY-03/14)

- Authorization denials are logged with acting account, event id, and endpoint — and **no PII**
  (`EventReadController.Denied`)
- Verify no password, token, MFA secret, or personal data appears in logs during a full Postman run

> **Gaps**: logs go to console rather than a centralized service; there is **no alerting** on
> repeated authentication failures or authorization violations, and no defined retention. SECURITY-14
> requires all three for production. Deferred with the rest of the observability stack.

---

## Summary of outstanding security work

| Item | Rule | Status |
|---|---|---|
| Dependency scan in CI | SECURITY-10 | Not wired — placeholder in workflow |
| SBOM generation | SECURITY-10 | Not implemented |
| Centralized logging | SECURITY-03 | Console only |
| Security alerting + retention | SECURITY-14 | Not implemented |
| Coverage gate blocking merge | NFR-4.4 | Placeholder in workflow |
| Rate limiting on read endpoints | SECURITY-11 | Not applied (low risk) |

None of these blocked a stage gate at design time — they are deployment-and-operations concerns,
and the Operations phase is still a placeholder. They are recorded here so they are not mistaken
for completed controls.


---

## 8. Hub credentials — the second authentication path (U10, 2026-08-02)

Added by U10. Until then the cloud authenticated only people; it now also authenticates **hubs**, so
these checks are additional to §2 and §3 rather than covered by them.

| Check | How | Expected |
|---|---|---|
| Key returned once only | Issue a credential, then `GET .../hub-credentials` | The listing carries no `key` or `keyHash` field |
| Hash-only storage | Inspect the `HubCredentials` table | No column holds a usable key |
| Scope confinement | Ingest a batch naming another event | **Refused entirely** — no partial acceptance, nothing stored |
| Immediate revocation | Revoke, then ingest again | Refused on the very next request (no session, no cache) |
| Expiry ≡ revocation | Advance past `ExpiresAt` | Same refusal, same generic message |
| Non-disclosure | Present unknown / expired / revoked keys | All three produce the **same** response; the body must not say which |
| No credential in output | Read `/health`, `/api/replication/status`, and logs | The key never appears |
| Least privilege | Attempt any non-ingest route with a hub credential | Refused — the credential permits ingest and cursor read only |

Automated coverage: `backend/tests/.../HubCredentialTests.cs` and
`tests/EventManager.Integration.Tests/CredentialPathTests.cs`.

### Metrics ingress

The `/otlp/*` route is internet-reachable by necessity — a venue hub sits behind NAT and cannot be
scraped — and OTLP has no authentication of its own, so Caddy gates it with a bearer token.

- A wrong or absent token must yield **401 at Caddy**, with the collector never seeing the request.
- **Known limitations, deliberate and recorded**: the comparison is not constant-time, and the token is shared across all hubs, so rotation invalidates every hub at once. Proportionate for metrics; revisit if metrics ever carry anything sensitive. The *replication* credential is per-hub and revocable.

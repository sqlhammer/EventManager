# U10 — NFR Requirements Plan (MINIMAL depth)

**Stage**: CONSTRUCTION → NFR Requirements · **Unit**: U10 · **Branch**: `unit/u10-http-replication`
**Depth**: Minimal, scoped by the approved execution plan to **tech-stack selection only**

---

## What this stage is and is not

**Not** re-deriving non-functional requirements. `U10-NFR-1..8` were approved at Requirements
Analysis and Functional Design has already made them testable (`BR-REPL-45` defines the lag
objective, `BR-REPL-43` the completeness rule, `BR-REPL-27` the timeout). Restating them here would
be ceremony.

What is genuinely undecided is **which libraries**, and those are new production dependencies that
SECURITY-10 attaches to.

### Already settled by existing code — not asked

| Concern | Status |
|---|---|
| Rate limiting | **Already in the codebase.** `Program.cs:106-114` uses ASP.NET Core's built-in limiter with named `login` / `registration` policies. `BR-REPL-50` is a new policy, not a new dependency. |
| Dependency pinning | **Already enforced.** Central Package Management with `CentralPackageVersionOverrideEnabled=false` — a project cannot float its own version. |
| Vulnerability scanning | **Already on.** `NuGetAudit=true` with `NuGetAuditMode=all`, auditing transitive packages. There is even a live security pin in `Directory.Packages.props` (SQLitePCLRaw 2.1.12 for GHSA-2m69-gcr7-jv3q). |
| Persistence, validation, ids, testing | Unchanged — EF Core, FluentValidation, IdGen, xunit, FsCheck all already present. |

So SECURITY-10's pinning and scanning criteria are satisfied by the repo as it stands. **SBOM is the
one part that is not** — see NFR-Q6.

---

## PART 1a — Tech Stack Questions

---

### NFR-Q1 — OpenTelemetry package set for the hub

`U10-FR-18` needs the hub's `Meter` instruments exported over OTLP.

A) **Minimal**: `OpenTelemetry.Extensions.Hosting` + `OpenTelemetry.Exporter.OpenTelemetryProtocol`. Exports our own instruments and nothing else. Two packages. **My lean.**

B) **A plus auto-instrumentation** (`OpenTelemetry.Instrumentation.AspNetCore`, `.Http`) — would also give replication request durations for free, but instruments the hub's whole HTTP surface, which nothing asked for.

C) **Exporter only**, wiring the meter provider by hand. One fewer package, more code at the composition root.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### NFR-Q2 — Which collector, and what does it do with the metrics?

The Compose stack gains a collector (F3=B). Note that a **pull-based** design was not viable: a venue hub sits behind NAT and is not addressable from the cloud, so the cloud could never scrape it. Push over OTLP is the only shape that works here.

A) **Core collector** (`otel/opentelemetry-collector`), OTLP in → debug/file out. Smallest attack surface. Proves the pipeline, but nothing can query the metrics.

B) **Contrib collector** (`otel/opentelemetry-collector-contrib`), OTLP in → Prometheus exposition out, so something can scrape it whenever Operations adds a dashboard. One service, larger image. **My lean** — consistent with "this unit emits, Operations consumes".

C) **B plus a Prometheus service** in Compose, so metrics are queryable now. Genuinely useful; adds a second service and storage to a stack this unit was meant to touch lightly.

X) Other (please describe after [Answer]: tag below)

[Answer]: B

---

### NFR-Q3 — Secret protection API (D-U10-02, F1=B)

A) **`System.Security.Cryptography.ProtectedData`** — the DPAPI wrapper. Small, Microsoft-owned, machine-bound, exactly what F1=B chose. **My lean.**

B) **`Microsoft.AspNetCore.DataProtection`** — cross-platform and already familiar in ASP.NET Core, but its key ring itself needs protecting, which reopens the key-management question D-09 deliberately deferred.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### NFR-Q4 — `HttpClient` lifetime

A) **`IHttpClientFactory`** via `AddHttpClient`. Handles socket exhaustion and DNS staleness. **Adds no package** — `Microsoft.Extensions.Http` is already in the ASP.NET Core shared framework the hub host uses. **My lean.**

B) **One long-lived `HttpClient`** owned by the transport. No registration; must handle DNS staleness by hand, which matters for a process that runs all day.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### NFR-Q5 — Retry and circuit breaking: hand-rolled or a library?

`BR-REPL-29..36` already specify the exact behaviour.

A) **Hand-rolled**, as specified. No new dependency. The rules are unusual enough that a library would be configured around rather than used: only *connection* failures advance the breaker, and a permanent failure consumes no retry attempts. Also keeps the logic directly unit-testable, which matters for `P-REPL-1`. **My lean.**

B) **`Microsoft.Extensions.Http.Resilience`** (Polly). Battle-tested and standard, but the custom classification still has to be written, and the breaker semantics would need overriding.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

### NFR-Q6 — SBOM (SECURITY-10)

Pinning and scanning are already satisfied. SBOM generation is not, and it is an open **project-level** gap recorded at Build-and-Test — it is not caused by this unit.

A) **Leave it open** — remains a project-level gap, unchanged by U10. **My lean**, because closing it properly means covering all six solutions, which is a project-wide task rather than something this unit should do half of.

B) **Close it for this unit** — generate an SBOM for the projects U10 touches. Partial coverage may read as done when it is not.

C) **Close it project-wide now** — add SBOM generation to CI for every solution. Right outcome, clearly outside this unit's scope.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

## PART 1b — Resolved Decisions



| Question | Answer | Resolution |
|---|---|---|
| NFR-Q1 OTel packages | **A** | Minimal set: `OpenTelemetry.Extensions.Hosting` + `OpenTelemetry.Exporter.OpenTelemetryProtocol`, both **1.17.0** |
| NFR-Q2 Collector | **B** | Contrib collector, OTLP in → Prometheus exposition out. Exact tag pinned at Infrastructure Design |
| NFR-Q3 Secret protection | **A** | `System.Security.Cryptography.ProtectedData` **10.0.10** |
| NFR-Q4 HttpClient | **A** | `IHttpClientFactory` — adds no package |
| NFR-Q5 Retry/breaker | **A** | Hand-rolled per BR-REPL-29..36 |
| NFR-Q6 SBOM | **A** | Stays open as a project-level gap, unchanged by U10 |

**Three new packages, all in the hub host only.** Nothing in `backend/`, `shared/`, or the hub
library — so the library stays platform-neutral (U10-CON-1) and exporter-neutral (AD-Q8=A).
Versions resolved against nuget.org on 2026-07-27 rather than guessed.

---

## PART 2 — Execution Checklist

- [x] Generate `construction/u10-http-replication/nfr-requirements/nfr-requirements.md` — restating U10-NFR-1..8 as *inherited and already approved*, with the `BR-REPL-*` rule that makes each measurable
- [x] Generate `construction/u10-http-replication/nfr-requirements/tech-stack-decisions.md` — every choice with rationale and the packages it adds
- [x] List every new package with its pinned version for `Directory.Packages.props`
- [x] Record SECURITY-10 status honestly: pinning and scanning satisfied by existing repo configuration, SBOM per NFR-Q6
- [x] Confirm no decision contradicts D-U10-01..15, AD-Q1..Q9, or BR-REPL-1..50
- [x] Update `aidlc-docs/aidlc-state.md`
- [x] Log the approval prompt in `audit.md` before presenting
- [x] Mark every checklist item [x] in the same interaction as the work

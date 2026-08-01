# U10 — Tech Stack Decisions

**Unit**: U10 HTTP Replication Adapter · **Stage**: NFR Requirements (minimal depth)
**Answers**: NFR-Q1=A, NFR-Q2=B, NFR-Q3=A, NFR-Q4=A, NFR-Q5=A, NFR-Q6=A

---

## 1. Decisions

| # | Decision | Rationale |
|---|---|---|
| **TS-U10-1** | **OpenTelemetry, minimal package set** — `OpenTelemetry.Extensions.Hosting` + `OpenTelemetry.Exporter.OpenTelemetryProtocol` | Exports the `Meter` instruments `U10-FR-18` asks for and nothing else. Auto-instrumentation was rejected as scope nobody requested |
| **TS-U10-2** | **Contrib collector** in the Compose stack — OTLP in, Prometheus exposition out | Real pipeline, one service, leaves a scrape target for whenever Operations adds a dashboard. Consistent with "this unit emits, Operations consumes" (requirements §7) |
| **TS-U10-3** | **`System.Security.Cryptography.ProtectedData`** for DPAPI | Exactly what F1=B chose: machine-bound protection. `Microsoft.AspNetCore.DataProtection` was rejected because its key ring needs protecting, reopening the key-management question D-09 deferred |
| **TS-U10-4** | **`IHttpClientFactory`** via `AddHttpClient` | Handles socket exhaustion and DNS staleness, which matter for a process running all day. **Adds no package** — already in the shared framework |
| **TS-U10-5** | **Hand-rolled retry and circuit breaking** per `BR-REPL-29..36` | No new dependency, and the semantics are unusual enough that a library would be configured around rather than used: only *connection* failures advance the breaker (`BR-REPL-34`), and a permanent failure consumes no retry attempts (`BR-REPL-33`). Keeps the logic directly unit-testable, which `P-REPL-1` depends on |
| **TS-U10-6** | **SBOM stays open** as a project-level gap | Not caused by this unit; closing it properly spans all six solutions. Partial coverage would read as done when it is not |
| **TS-U10-7** | **`System.Diagnostics.Metrics.Meter`** for instrumentation | From AD-Q8=A. Component code references no OTel type; the exporter is wired only at the composition root and is swappable |
| **TS-U10-8** | **`System.Text.Json`** over the existing DTOs | From D-U10-15. No new wire contract, no `shared/` change, no serializer dependency |
| **TS-U10-9** | **ASP.NET Core's built-in rate limiter**, new `"ingest"` policy | Not a decision so much as a finding: `Program.cs:106-114` already uses it for `login` and `registration`. `BR-REPL-50` adds a policy, not a dependency |

---

## 2. New packages

Versions resolved against nuget.org on 2026-07-27. Central Package Management requires exact pins
and forbids per-project overrides, so these go in `Directory.Packages.props` and nowhere else.

| Package | Version | Used by | Purpose |
|---|---|---|---|
| `OpenTelemetry.Extensions.Hosting` | **1.17.0** | `admin/EventManager.Hub` (host) | Meter provider wiring |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | **1.17.0** | `admin/EventManager.Hub` (host) | OTLP export |
| `System.Security.Cryptography.ProtectedData` | **10.0.10** | `admin/EventManager.Hub` (host) | DPAPI protection |

**Three packages, all in the hub host only.** Nothing new in `backend/`, nothing in `shared/`, and
nothing in the hub *library* — TS-U10-3 and TS-U10-1 are both registered at the composition root, so
the library stays platform-neutral (U10-CON-1) and free of exporter types (AD-Q8=A).

Note that `ProtectedData` 10.0.10 is a patch ahead of the `10.0.0` framework pins already in
`Directory.Packages.props`. That is deliberate — it is the current patch of the same wave, and CPM
makes taking it a one-line change.

### Container image

| Image | Pin | Notes |
|---|---|---|
| `otel/opentelemetry-collector-contrib` | **Must be pinned to an exact tag, never `latest`** (SECURITY-10) | Exact tag selected at Infrastructure Design, which also owns its network exposure (SECURITY-07) and access logging (SECURITY-02) |

---

## 3. Consistency check

No decision here contradicts an approved one:

- D-U10-15 (`System.Text.Json`, no `shared/` change) — upheld by TS-U10-8.
- AD-Q6=A / AD-Q8=A (library stays platform- and exporter-neutral) — upheld: all three new packages land in the host.
- BR-REPL-26..36 — TS-U10-5 implements them directly rather than approximating them with a library.
- F3=B (collector in the cloud stack) — TS-U10-2. **U10-CON-2 still stands**: the collector is unreachable during an outage, and `ReplicationStatus` remains the venue-visible signal.

---

## 4. What this stage did not decide

Left to the stages that own them, rather than pre-empted here:

- The collector's **exact image tag, network exposure, and access logging** → Infrastructure Design.
- The **`"ingest"` rate-limit numbers** (U10-CON-3) → NFR Design.
- **Breaker thresholds and timer intervals** — already fixed as defaults by `BR-REPL-35`, `BR-REPL-38`, `BR-REPL-39`; NFR Design confirms them and settles configuration binding.

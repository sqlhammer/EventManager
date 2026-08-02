# U10 — NFR Requirements (inherited)

**Unit**: U10 HTTP Replication Adapter · **Stage**: NFR Requirements (minimal depth)

This unit's non-functional requirements were **approved at Requirements Analysis** and are not
re-derived here. What this document adds is the mapping from each one to the business rule that makes
it measurable — because an objective nobody can evaluate is not a requirement, it is a wish.

---

## 1. Inherited requirements and how each is measured

| ID | Requirement | Made measurable by | How it is verified |
|---|---|---|---|
| **U10-NFR-1** | Under normal connectivity the cloud is no more than **5 minutes** behind the hub | `BR-REPL-45` — lag is `now − OccurredAt` of the oldest unreplicated event, **zero** when no backlog | Unit test over a seeded backlog; the drain timer (`BR-REPL-39`, 60s default) gives a wide margin |
| **U10-NFR-2** | 100% of the local log mirrored before an event is declared closed | `BR-REPL-43` — for every device, cloud high-water mark ≥ hub high-water mark | `VerifyCompletenessAsync`; exercised by `P-REPL-1` |
| **U10-NFR-3** | Every HTTP call has an explicit timeout | `BR-REPL-27` — configurable, 30s default | Unit test with a hanging stub handler |
| **U10-NFR-4** | An outage is a no-op, never data loss | `BR-REPL-36` (open breaker = no-op) and `BR-REPL-41` (seeding failure is non-fatal) | `P-REPL-1`, plus the existing zero-internet property from U7 |
| **U10-NFR-5** | No credential material in logs, metrics, health, or errors | `BR-REPL-9`, `BR-REPL-24` | Assertion sweep over emitted output in tests |
| **U10-NFR-6** | TLS 1.2+ for all hub→cloud traffic | `BR-REPL-26` — non-HTTPS base URL prevents replication starting, unless the dev override is set | Unit test on transport construction |
| **U10-NFR-7** | Cloud availability/RTO/RPO inherited from U3 (Medium, 99.5%, RTO 4h, RPO 24h) | **No rule — deliberately** | Not verified here; this unit adds no cloud workload, so there is nothing new to measure. Consistent with how it was handled at User Stories |
| **U10-NFR-8** | Replication does not degrade hub responsiveness during an event | `BR-REPL-37` — the append signal is non-blocking and drops when full | Unit test that a full channel never blocks an append |

`U10-NFR-7` is the only one without a rule, and that is intentional and recorded rather than an
omission.

---

## 2. Requirements deliberately **not** added

- **No new availability target for the hub.** The hub is a venue laptop; its availability is a physical question, not a software one, and U7's backup/recovery already covers the failure mode (US-506).
- **No throughput target.** NFR-5.1 (300 athletes, ~8 mats, ~20 devices) already bounds event volume, and the 500-envelope batch cap plus a 60-second drain interval is orders of magnitude above it.
- **No cloud-side latency target for ingest.** U3-NFR-P1 (p95 < 500ms) already applies and this unit adds no expensive work to the ingest path — provenance is one nullable column set at insert.

---

## 3. Extension status at this stage

| Rule | Status |
|---|---|
| SECURITY-10 Supply chain | **Pinning: compliant** — Central Package Management with `CentralPackageVersionOverrideEnabled=false`. **Scanning: compliant** — `NuGetAudit=true`, `NuGetAuditMode=all`. **Trusted source: compliant** — nuget.org only. **SBOM: open**, unchanged by this unit (NFR-Q6=A) and still a project-level gap from Build-and-Test |
| SECURITY-01 / NFR-6 | **Compliant** — `BR-REPL-26` |
| RESILIENCY-10 | **Compliant** — `BR-REPL-27` timeout, `BR-REPL-34/35` breaker, hand-rolled per NFR-Q5=A |
| PBT-01 | **Compliant** — `P-REPL-1`; FsCheck already present, no new test dependency |

**No blocking findings.** SBOM is an accepted, pre-existing, project-level gap and is recorded as
such rather than being counted against this unit or quietly closed halfway.

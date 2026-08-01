# U10 — Business Rules (`BR-REPL-*`)

**Unit**: U10 HTTP Replication Adapter · **Stage**: Functional Design

Technology-agnostic. Each rule names the requirement and story it serves.

---

## Issuance (US-801)

| ID | Rule | Serves |
|---|---|---|
| **BR-REPL-1** | A credential may be issued only by an account holding organizer rights on the target event, evaluated with the existing `EventAuthorizer`. | U10-FR-2 |
| **BR-REPL-2** | The key is generated from a cryptographic random source with at least 256 bits of entropy, returned **exactly once** at issue, and is never retrievable afterwards by any path. | U10-FR-2, U10-NFR-5 |
| **BR-REPL-3** | The cloud persists only a **SHA-256 hash** of the key, matching the pattern already established by `RefreshTokenStore`. A reader of the cloud database cannot recover a usable credential. **Corrected 2026-08-01 (Code Generation C-1)**: this rule originally said "salted hash". That was wrong for this design — a salted hash cannot be looked up, so authentication would have to scan every credential row and verify each one, and salting exists to defeat rainbow tables against *low-entropy* secrets, which a 256-bit random key (BR-REPL-2) is not. The salt would have cost the indexed lookup and bought nothing. | U10-FR-2, SECURITY-12 |
| **BR-REPL-4** | `ExpiresAt = Event.Date + grace`, where grace is configurable and defaults to **14 days**. Expiry is never caller-supplied. *(The model has no event end date; `EventRow.Date` is a single day.)* | FD-Q1=C, CL-B=D |
| **BR-REPL-5** | An event may have at most **3** credentials in state `Active`. Expired and revoked credentials do not occupy a slot. Issuing a fourth is refused with a message naming the limit. | FD-Q2 |
| **BR-REPL-6** | A label is required, non-empty, and length-bounded. It is human identification only and carries no authority. | U10-FR-2, SECURITY-05 |

## Authentication (US-809)

| ID | Rule | Serves |
|---|---|---|
| **BR-REPL-7** | A presented key authenticates if and only if it hashes to a stored credential in state `Active`. Every other outcome — unknown, expired, revoked, malformed — produces one generic failure that discloses nothing about which case applied. | U10-FR-3, SECURITY-09 |
| **BR-REPL-8** | Authentication is evaluated on **every request**. There is no session and no cached decision, which is what makes revocation effective on the next attempt. | U10-FR-4, US-808 |
| **BR-REPL-9** | The key value never appears in a log line, metric tag, health response, status response, or error message — on either side. | U10-NFR-5 |

## Authorization (US-809)

| ID | Rule | Serves |
|---|---|---|
| **BR-REPL-10** | A hub-credential caller may ingest only for its bound `EventScopeId`. A batch containing **any** event outside that scope is refused **in its entirety** — no partial acceptance. | U10-FR-3 |
| **BR-REPL-11** | A hub-credential caller may read cursors only for its bound scope. | U10-FR-12 |
| **BR-REPL-12** | A hub credential grants nothing beyond ingest and cursor read. It cannot read event data, manage roster, or administer accounts. | U10-FR-3, SECURITY-06 |
| **BR-REPL-13** | Account-based ingest is unchanged: an account caller still authorizes via `OrganizerAction.ManageRoster` on every scope in the batch. | D-U10-09 |

## Expiry and revocation (US-808)

| ID | Rule | Serves |
|---|---|---|
| **BR-REPL-14** | Expiry and revocation are identical in effect: both are **permanent** failures, never retried. | U10-FR-4 |
| **BR-REPL-15** | Revocation is immediate and one-way. There is no un-revoke; the remedy is to issue a new credential. | U10-FR-4 |
| **BR-REPL-16** | When the installed credential expires within the warning threshold (configurable, default **7 days**), replication status carries a warning. | FD-Q3=D |
| **BR-REPL-17** | Close-out is refused when the installed credential is already expired. The refusal names re-issue as the remedy and states that the completeness report is unavailable — it requires cloud cursors, which require a valid credential. | FD-Q3=D |
| **BR-REPL-18** | Revoking a credential never affects data already replicated, and never disables the hub locally. A revoked hub keeps its event log and keeps running the event. | US-808 |

## Ingest provenance (US-809, SECURITY-13)

| ID | Rule | Serves |
|---|---|---|
| **BR-REPL-19** | On the ingest path, each newly appended event records the ingesting credential id. The attribute is optional and set **once, at insert**. | FD-Q7=B |
| **BR-REPL-20** | Duplicate events are skipped, not updated. Provenance therefore records the **first** credential to deliver an event, not the most recent — relevant when a replacement hub (US-506) re-sends events the original already delivered. | FD-Q7=B |
| **BR-REPL-21** | Events appended by the cloud itself carry no provenance value. | FD-Q7=B |

## Hub custody (US-802)

| ID | Rule | Serves |
|---|---|---|
| **BR-REPL-22** | The hub holds at most one credential. Installing while one is present is **refused**; clearing is a separate explicit action. | FD-Q8=B |
| **BR-REPL-23** | The stored value is protected before it is written and unprotected only in memory at the moment of use. A copy of the hub database is unusable on another machine. | U10-FR-5, D-U10-02 |
| **BR-REPL-24** | Read paths may report *whether* a credential is installed; none may return or echo its value. | U10-NFR-5 |
| **BR-REPL-25** | A hub with no installed credential does not attempt replication and reports "no cloud credential". This is a stated condition, **not** an error state, and does not stop the hub running the event. | US-802 |

## Transport (US-803, US-809)

| ID | Rule | Serves |
|---|---|---|
| **BR-REPL-26** | The cloud base URL must be HTTPS. A non-HTTPS URL prevents replication from starting, unless the development override is explicitly enabled. | U10-FR-14, SECURITY-01 |
| **BR-REPL-27** | Every request carries an explicit timeout, configurable, defaulting to **30 seconds**. No call waits unbounded. | U10-NFR-3, RESILIENCY-10 |
| **BR-REPL-28** | A batch carries at most **500** envelopes and at most the configured byte cap. An oversized batch is **split**, never dropped and never failed. | U10-FR-13, D-U10-14 |

## Failure classification (US-804 table)

| ID | Rule | Serves |
|---|---|---|
| **BR-REPL-29** | *Transient-connection*: no network, connection refused, DNS failure, connect timeout, request timeout. | U10-FR-6 |
| **BR-REPL-30** | *Transient-response*: server-side failure responses and request-timeout responses. | U10-FR-6 |
| **BR-REPL-31** | *Throttled*: the cloud asking the caller to slow down. The wait it specifies is honoured when present; otherwise the standard backoff applies. | U10-FR-8, D-U10-13 |
| **BR-REPL-32** | *Permanent*: malformed request, unauthenticated, unauthorized, not found, payload too large, unprocessable, and any response that cannot be understood. | U10-FR-6 |
| **BR-REPL-33** | Only transient and throttled failures are retried, up to the attempt limit. A permanent failure propagates immediately, consumes no attempts, and is surfaced **distinctly from an outage** — the operator's response differs. | U10-FR-7, US-804 |

## Circuit breaker (US-804)

| ID | Rule | Serves |
|---|---|---|
| **BR-REPL-34** | Only *transient-connection* failures advance the breaker. A server-side failure means the cloud is reachable and unwell, which is not the condition the breaker exists for. | U10-FR-9 |
| **BR-REPL-35** | The breaker opens after **3** consecutive connection failures (configurable). While open, the transport reports offline. After a cool-down (configurable, default **60 seconds**) one trial request is permitted: success closes and resets the breaker, failure re-opens it. | U10-FR-9, D-U10-04 |
| **BR-REPL-36** | An open breaker makes replication a **no-op**, never an error. Nothing is surfaced to the people running the event. | U10-NFR-4, US-804 |

## Triggers (US-803, US-807)

| ID | Rule | Serves |
|---|---|---|
| **BR-REPL-37** | The append signal is non-blocking and drops when full. A dropped signal is not an error: it carries no data, and the drain timer is the backstop. Blocking an append on a full replication channel would let a cloud problem slow down the event. | U10-FR-10, AD-Q5=C |
| **BR-REPL-38** | Append-triggered replication is debounced. Configurable, default **2 seconds**. | FD-Q5=D |
| **BR-REPL-39** | A drain timer runs on a configurable interval, default **60 seconds**, and replicates when a backlog exists or the breaker cool-down has elapsed. This is what makes breaker recovery and the lag objective reachable when appends stop. | U10-FR-10, F2=C |
| **BR-REPL-40** | Close-out replicates within a bounded window (configurable, default **2 minutes**) and then reports whatever completeness was reached. It always returns. | U10-FR-11, FD-Q6=A |

## Cursors and completeness (US-805, US-807)

| ID | Rule | Serves |
|---|---|---|
| **BR-REPL-41** | At startup the hub seeds its cursors from the cloud. Failure to reach the cloud is **non-fatal** — the hub starts with empty cursors and proceeds; re-sending is wasteful, never incorrect. | U10-FR-12, US-805 |
| **BR-REPL-42** | Cursors advance **only** from an acknowledgement, and only forward. | U10-FR-19 |
| **BR-REPL-43** | The event log is complete when, for every device, the cloud's high-water mark is at least the hub's. | U10-NFR-2 |
| **BR-REPL-44** | Re-sending already-delivered events is always safe: the cloud accepts nothing twice and reports the same progress. | U10-FR-19 |

## Lag and status (US-806)

| ID | Rule | Serves |
|---|---|---|
| **BR-REPL-45** | **Replication lag** — the U10-NFR-1 objective — is `now − OccurredAt` of the oldest unreplicated event, and is **zero** when there is no backlog. | U10-NFR-1, FD-Q4=D |
| **BR-REPL-46** | Time since last successful replication and the count of unreplicated events are reported alongside as operational detail. Neither is the objective; time-since-last-success in particular climbs while idle even when the cloud is perfectly current. | FD-Q4=D |
| **BR-REPL-47** | The pending-event count is as of the last replication run on `/health` and in exported metrics, and is presented as such. **Amended 2026-07-27 (NFR Design ND-Q6=C)**: the human-facing `GET /api/replication/status` computes the pending count **and** the lag together in a single store pass, so it never returns a live lag beside a stale count. Each surface is internally consistent. | AD-Q7=A, ND-Q6=C |
| **BR-REPL-48** | All replication status is computed in-process and never requires reaching the cloud — otherwise the one question asked during an outage would be unanswerable during an outage. | U10-FR-17, U10-CON-2 |

## Ingest availability (US-810)

| ID | Rule | Serves |
|---|---|---|
| **BR-REPL-49** | The ingest route enforces a request-body size limit. An oversized request is rejected without consuming server resources, and the rejection is permanent (not retried). A conforming hub never produces one, because it splits first (BR-REPL-28). | U10-FR-15 |
| **BR-REPL-50** | The ingest route is rate-limited by a policy set above the hub's expected burst — including a large post-outage drain. A throttled hub slows down and completes rather than failing (BR-REPL-31). | U10-FR-15, U10-CON-3 |

---

## Property-based verification (PBT-01)

**P-REPL-1** — For any interleaving of outages, connection failures, throttling, batch splits,
permanent failures, and hub restarts, the cloud's log is per device a **gap-free prefix** of the
hub's log with **no duplicates**.

Exercises BR-REPL-10, -20, -28, -33, -41, -42, -43, -44.

---

## Coverage

Every U10-FR has at least one owning rule:

| FR | Rules | FR | Rules |
|---|---|---|---|
| 1 | 26–28 | 11 | 40, 43 |
| 2 | 1–6 | 12 | 11, 41 |
| 3 | 7, 10, 12, 13 | 13 | 28 |
| 4 | 8, 14, 15, 18 | 14 | 26 |
| 5 | 22–25 | 15 | 49, 50 |
| 6 | 29–32 | 16 | 9 |
| 7 | 33 | 17 | 45–48 |
| 8 | 31 | 18 | 45, 46 |
| 9 | 34–36 | 19 | 42, 44 |
| 10 | 37–39 | | |

Non-functional: U10-NFR-1 → BR-REPL-45 · NFR-2 → 43 · NFR-3 → 27 · NFR-4 → 36, 41 · NFR-5 → 9, 24 ·
NFR-6 → 26 · NFR-8 → 37. **U10-NFR-7 has no rule** — it inherits U3's targets unchanged and adds no
cloud workload, as recorded at User Stories.

---

## Extension applicability at this stage

| Rule | Status |
|---|---|
| SECURITY-05 Input validation | **Compliant** — BR-REPL-6 bounds the label, BR-REPL-49 bounds the body |
| SECURITY-06 Least privilege | **Compliant** — BR-REPL-10, -11, -12 |
| SECURITY-08 Access control | **Compliant** — BR-REPL-7, -8, -10; deny-by-default, no partial acceptance |
| SECURITY-11 Secure design | **Compliant** — abuse case bounded by scope, expiry, revocation, and cap (BR-REPL-5); BR-REPL-50 rate limits |
| SECURITY-12 Credentials | **Compliant** — BR-REPL-2, -3, -9, -23 |
| SECURITY-13 Integrity/audit | **Compliant** — BR-REPL-19, -20, -21 record the delivering actor |
| SECURITY-15 Fail-safe | **Compliant** — BR-REPL-7 fails closed; BR-REPL-36 degrades to a no-op rather than an error |
| PBT-01 | **Compliant** — P-REPL-1 stated with the rules it exercises |
| RESILIENCY-10 | **Compliant** — BR-REPL-27 timeout, -34/-35 breaker, -25/-36/-41 degraded modes |

**No blocking findings.**

**No frontend artifact** is produced for this unit: the hub's MAUI shell remains a deferred seam, so
there is no UI whose components, state, or form validation could be designed.

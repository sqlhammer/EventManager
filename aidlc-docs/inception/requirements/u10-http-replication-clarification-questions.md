# U10 HTTP Replication Adapter — Requirements Clarification Questions

**Stage**: INCEPTION → Requirements Analysis (Step 6 answer analysis)
**Source answers**: `u10-http-replication-verification-questions.md` — Q1=C, Q2=C, Q3=A, Q4=C, Q5=D, Q6=B, Q7=B, Q8=C, Q9=C, Q10=D (5 min), Q11=D

Q3=A, Q4=C, Q7=B and Q10's 5-minute lag target are unambiguous — no follow-up needed. Four combinations do need a decision rule.

---

## Finding 1 (BLOCKING — SECURITY-12, SECURITY-01): Q1=C + Q2=C

You chose **C on Q1** — a new backend-issued, **long-lived, ingest-only** hub credential — and **C on Q2** — store it as a **plaintext row in `hub.db`**.

Q2's preamble flagged that option C is a blocking finding under the enabled Security Baseline extension unless explicitly accepted, and the Q1=C pairing makes it the worst case rather than the mildest: a long-lived key that does not rotate is exactly the credential type least suited to plaintext at rest. Concretely — `hub.db` is unencrypted SQLite on a laptop that travels to venues; anyone with file access gets a credential that keeps working until someone notices and revokes it. Under Q1=A (a 14-day *rotating* refresh token) the same exposure would self-heal; under Q1=C it does not.

I am not going to record this as resolved on the strength of a letter. Pick how you want it closed:

A) **Accept the plaintext row, with compensating controls that bound the blast radius.** The credential is event-scoped and ingest-only (SECURITY-06 least privilege), carries an expiry, is revocable from the cloud at any time, and the cloud persists only a **hash** of it — so a cloud-side database compromise yields no usable hub keys. The plaintext-on-hub exposure is documented as an **accepted risk** carried until the SQLCipher seam (D-09) lands, with SECURITY-12 marked accepted-with-rationale rather than compliant.

B) **Same credential design, wrapped with Windows DPAPI before it is written.** (This is Q2=A applied to a Q1=C credential.) The row exists but the value is encrypted with a machine/user-bound key, so a copied `hub.db` is useless off that machine. The hub is Windows-only today, so this is a small amount of platform code and it removes the finding outright. **My recommendation.**

C) **The app never persists it** — the operator supplies the key through configuration (environment variable / secret file) at hub start. (Q2=B applied to Q1=C.) No secret written by us at all; protection becomes a deployment responsibility.

D) **Make the credential short-lived so plaintext matters less** — the cloud issues a key valid for ~24h that the hub auto-renews while online. A stolen `hub.db` is worthless the next day. Costs a renewal path in the backend and degrades if the hub is offline across the expiry boundary.

X) Other (please describe after [Answer]: tag below)

[Answer]: B

---

## Finding 2 (FUNCTIONAL GAP): Q5=D + Q4=C + Q10=D cannot all hold

You chose **append-driven replication (Q5=D)**, a **circuit breaker (Q4=C)**, a **5-minute lag objective and a close-out completeness requirement (Q10=D)**.

Append-driven means replication only ever fires when a *new local event is appended*. Two consequences follow that defeat Q10:

1. **The circuit breaker has nothing to reopen it.** When connectivity drops, the breaker opens and suppresses attempts. If the cool-down expires but no new event happens to be appended at that moment, nothing re-triggers replication — the backlog sits there and the 5-minute lag target is missed indefinitely.
2. **End of event is exactly when appends stop.** The completeness half of Q10=D ("100% mirrored before the event is declared closed") needs a drain at the moment the log goes quiet — which is precisely when an append-driven trigger never fires again.

So the trigger needs something besides appends:

A) **Append-driven + a low-frequency drain timer** (e.g. every 60s) that runs only when a backlog exists or the breaker's cool-down has elapsed. Keeps D's responsiveness and makes the 5-minute target and close-out completeness achievable.

B) **Append-driven + an explicit "close event" flush** that replicates to completion and then runs `VerifyCompletenessAsync`. No timer; the 5-minute lag target then holds only while events are actively flowing, and a mid-event outage is not recovered from until close-out.

C) **Both A and B** — timer for steady state and breaker recovery, explicit flush at close-out as the completeness gate. **My recommendation.**

D) **Drop append-driven and use Q5=C instead** (background timer + manual trigger). Simpler, but loses the near-real-time responsiveness that made D attractive.

X) Other (please describe after [Answer]: tag below)

[Answer]: C

---

## Finding 3 (SCOPE AMBIGUITY): Q8=C metrics have no destination

You chose **C on Q8** — logs + health status + a metrics exporter (OpenTelemetry / Prometheus).

There is nowhere for those metrics to go. U3's infrastructure design stands up `api`, `db` and Caddy only — no Prometheus, no OTLP collector — and the hub itself is a venue machine that is frequently offline, which is the case where you most want the metrics. "Add an exporter" therefore means one of three materially different scopes:

A) **Expose metrics locally on the hub** (`/metrics`, Prometheus text format). Nothing changes in the cloud stack; who scrapes it is a documented operations seam. Self-contained, works offline, but nobody is collecting it yet.

B) **Export OTLP to a collector added to the cloud docker-compose stack.** Real centralized metrics (and what RESILIENCY-05 actually asks for), but it is an infrastructure change and only reports while the hub is online — blind exactly during an outage.

C) **A now, with the OTLP exporter present but disabled behind configuration** so B can be switched on later without a code change. **My recommendation** — it keeps this unit's infrastructure footprint at zero while leaving the path open.

X) Other (please describe after [Answer]: tag below)

[Answer]: B

---

## Finding 4 (COVERAGE AMBIGUITY): Q11=D against a brand-new auth path

You chose **D on Q11** — stubbed `HttpMessageHandler` tests plus a manual docker-compose walkthrough. That was a reasonable answer when the adapter was going to reuse an existing login; Q1=C, Q6=B and Q9=C changed what is being built underneath it. This unit now adds, in `backend/`: a hub-credential entity, an issuing endpoint, a **new authentication path**, a `GET /api/ingest/high-water-marks` endpoint, and rate limiting plus a body-size cap on ingest.

To be clear about what D does and does not leave uncovered: the backend additions still get their own unit tests in `backend/tests/EventManager.Api.Tests`, as every U3/U9 addition did. What D leaves untested by any automated test is the **seam** — a real hub credential presented by the real adapter to the real ingest endpoint. Under a blocking Security Baseline, a new authorization path whose only end-to-end verification is a human following a markdown file is worth a deliberate decision rather than an inherited one.

A) **Keep D as answered.** Backend units cover the new endpoints, the seam is verified manually in the walkthrough. Fastest; accepts that a credential/scope regression would not fail the build.

B) **D plus one narrow in-process end-to-end test** covering only the credential path: adapter → real `EventIngestController`, asserting 200 for a valid scoped credential and 401/403 for a revoked, expired, or wrong-event one. One cross-solution reference, in one clearly-labelled test file. **My recommendation.**

C) **Upgrade to Q11=C in full** — a separate integration test project referencing both sides, covering the whole hub↔cloud flow.

X) Other (please describe after [Answer]: tag below)

[Answer]: B
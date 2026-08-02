# Performance Test Instructions

**Status**: **not yet executed.** No load test has been run against this system. The targets below
come from the approved NFRs; the "actual" column is empty because there is no measurement, and it
would be dishonest to present one.

---

## Requirements (from approved NFRs)

| Target | Value | Source |
|---|---|---|
| Registration / login latency | **p95 < ~500 ms** under nominal burst, excluding deliberate rate-limit and lockout responses | U3-NFR-P1, NFR-5.4 |
| Concurrency envelope (cloud) | **Hundreds of concurrent users** during pre-event registration bursts | NFR-5.4 |
| Scale envelope (event day) | **300 athletes, ~8 mats, ~20 concurrent LAN devices**, 1–3 organizers per event | NFR-5.1 |
| Availability | 99.5% | U3-NFR-R2 |
| Error rate | Not separately specified — treat non-2xx excluding intentional 429/423 as failure | — |

**The load profile is deliberately lopsided.** The cloud backend is sized for the *registration
burst* — hundreds of users in a short window before the event — and explicitly **not** for event-day
load, because on event day the hub is authoritative and the cloud is a mirror. Load-testing the
cloud with event-day traffic would be testing the wrong thing.

---

## What to test, in priority order

1. **Registration burst** — the only path with a stated latency target. Concurrent
   `POST /api/registration` and `POST /api/registration/batch` against one open event.
2. **Login** — same target, and it sits behind a 5-per-minute rate limiter that must be excluded
   from latency statistics rather than counted as failures.
3. **U9 read endpoints** — untested under load and now the most likely source of slow queries.
   `GET /api/events` runs four queries and `GET .../registrants` returns a complete unpaginated
   roster, so both scale with data volume. Worth measuring at 300 athletes.
4. **Ingest** — `POST /api/ingest/batch` with realistic event-log batches, since a hub reconnecting
   after an outage submits a large backlog in bursts.

---

## Setup

```bash
cd backend
cp .env.example .env
docker compose up -d --build
```

Seed a realistic dataset first — an event with ~300 athlete profiles and registrations across ~20
divisions. The Postman **Registration** folder can do this via `batch`, or script it directly
against the API.

No load-testing tool is currently a project dependency. **k6** is the natural fit: it scripts in
JavaScript, runs in a container, and needs nothing added to the .NET solutions.

```bash
docker run --rm -i grafana/k6 run - <script.js
```

---

## Suggested k6 shape

```javascript
export const options = {
  stages: [
    { duration: '30s', target: 50 },   // ramp
    { duration: '2m',  target: 200 },  // hundreds of concurrent users (NFR-5.4)
    { duration: '30s', target: 0 },
  ],
  thresholds: {
    'http_req_duration{scenario:registration}': ['p(95)<500'],   // U3-NFR-P1
    http_req_failed: ['rate<0.01'],
  },
};
```

Authenticate each virtual user once and reuse the JWT — re-logging in per iteration would measure
the rate limiter rather than the registration path.

---

## Analysis

Record p50/p95/p99, throughput, and error rate per endpoint. Separate **intentional** 429 (rate
limit) and 423 (lockout) from genuine errors — counting them as failures will make a
correctly-behaving system look broken.

Likely first bottlenecks, from reading the code rather than measurement:

- **`GET /api/events`** issues four queries, one of which scans every open event. Fine at tens of
  events; worth watching as the table grows.
- **`GET .../registrants`** returns the whole roster with no pagination — a deliberate decision
  (Q7=A) that is correct at 300 athletes and would need revisiting at tens of thousands.
- **Inline synchronous projection** means every write folds read models in the same transaction.
  This is what makes reads read-your-writes consistent and the ETag watermark exact, but it puts
  projection work on the write path.
- **Idempotency-key lookups** occur in the same transaction as each batch write.

## If targets are missed

Measure before changing anything. The three levers most likely to matter, in order: add indexes
suggested by query plans, add pagination to the roster read, and move projection off the write path
— but note that the last one **invalidates the U9 watermark ETag** (U9-CON-3) and would require
switching to a projection-applied high-water mark.


---

## U10 addendum — replication lag (2026-08-02)

U10 introduced **U10-NFR-1**: under normal connectivity the cloud is no more than **5 minutes** behind
the hub.

**Status: NOT MEASURED.** Consistent with every other target in this document, no load test has been
run. What exists is a *definition* test, not a measurement: `BR-REPL-45` defines lag as the age of
the oldest unreplicated event (zero when there is no backlog), and that definition is unit-tested.
Whether the target holds under a real event's write rate is unknown.

Time-since-last-success was rejected as the metric precisely because it would look like a breach
whenever the hub is idle and the cloud is perfectly current — worth remembering when someone
eventually builds the dashboard.

### When a load test is run

| Parameter | Value |
|---|---|
| Workload | Spoke sync batches at an event-day rate (~300 athletes, ~8 mats, ~20 devices — NFR-5.1) |
| Measure | `eventmanager.replication.lag.seconds` p95 over the run |
| Pass | p95 < 300 s under normal connectivity |
| Also watch | `eventmanager.replication.backlog` should return to 0 between bursts; `replication.failures` tagged `Throttled` should be 0 — a conforming hub should never trip its own rate limit |

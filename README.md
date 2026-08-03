# EventManager

Offline-first tournament management software for independent dojo owners running local martial
arts tournaments of 50–300 athletes.

## The problem

Every existing tournament platform (Smoothcomp, TournamentTiger, Kihapp, MartialMatch, NinjaPanel)
is priced for large grappling federations or national circuits, and every one of them is
entirely cloud-dependent — a venue WiFi outage can take down registration, scoring, and results
mid-event with no fallback. Independent dojo owners running a one-day, 50–300 athlete tournament
are left with tools built for the wrong scale, the wrong budget, and the wrong risk tolerance.

## The approach

EventManager treats internet connectivity as a bonus, not a requirement. The entire event —
registration, brackets, check-in, weigh-in, scoring, results — runs on a local hub-and-spoke
network at the venue, with zero internet dependency. Every state change is written as an
immutable, sequence-numbered event to a local, durable log *before* anything acknowledges
success; state is a replay of that log. Replay is idempotent, so the same event applied twice
(a queued offline action replayed after reconnect, a retried sync) changes nothing. That
idempotent-append discipline is what makes offline queuing, LAN sync, and eventual cloud
replication safe without a distributed consensus protocol.

Connectivity, when it exists, is additive: the venue hub mirrors its event log to the cloud
asynchronously whenever internet is available, and the cloud is never a competing source of
truth — only a mirror the hub replicates into.

## Who it's for

| User | Needs |
|---|---|
| Tournament organizer (dojo owner) | Run a 50–300 athlete event with fast setup and confidence it won't fail if venue WiFi does |
| Judges/scorekeepers | Enter scores on their assigned mat with zero cloud dependency |
| Check-in/weigh-in staff | Mark athletes present and record weight with automatic range validation, offline |
| Athletes & spectators | Register online ahead of time; follow brackets and results on the day |

## Architecture

Two planes, one shared event-sourcing core:

- **Cloud plane (online, pre-event + mirror)** — an ASP.NET Core Web API backed by PostgreSQL.
  Handles accounts, organizer RBAC, registration (including coach bulk entry), division
  configuration, payment (behind a provider-agnostic seam), and read access to event/division/
  registration data. It also authenticates the hub itself as a principal, receiving replicated
  event batches over HTTPS.
- **Venue plane (event day, LAN, internet optional)** — a hub-and-spoke topology. The **Admin
  hub** embeds a local ASP.NET Core server and local SQLite event log, and is authoritative for
  bracket structure, divisions, and schedule. **Judge** and **Check-In** apps are spokes that pair
  to the hub over the LAN: each Judge instance is authoritative only for its own assigned mat;
  Check-In is append-only. The hub replicates its event log to the cloud asynchronously whenever
  internet is available, and resumes gap-free after an outage.

```
CLOUD (online):        cloud-backend (ASP.NET Core API) --- PostgreSQL (mirror)
                               ▲  |
                   replicate   |  | download event
                   (HTTPS,     |  | before event day
                   hub creds)  |  ▼
VENUE LAN (event day, internet optional):
    admin-hub (embedded Kestrel server + local SQLite event log)
        ◄── scores (mat-scoped) ──  judge app
        ◄── check-ins/weigh-ins ──  checkin app
        ── pushes brackets/schedule/results ──►  both spokes
```

Every action becomes a durable local write before it's acknowledged, is applied to the hub
idempotently (`AppendIfNotExists`), and is mirrored to the cloud the same way — so an event
written on a phone with no signal is never lost and never double-counted once connectivity
returns.

## Major components

| Component | What it is |
|---|---|
| `shared/EventManager.Domain` | Core domain engines: bracket generation/advancement, seeding, scoring, weigh-in policy evaluation, role-based authorization policy |
| `shared/EventManager.Sync` | The event-sourcing primitives: `TournamentEvent`, `IEventStore`, idempotent replay, projections, Snowflake ID generation, the replication protocol |
| `shared/EventManager.Contracts` | Wire DTOs, mappers, and FluentValidation validators shared by every client and the API |
| `shared/EventManager.ClientSync` | Reusable spoke-side sync library: durable offline queue, hub pairing, reconnect/replay, push consumption |
| `backend/EventManager.Api` | The cloud backend — accounts/auth (incl. MFA), organizer RBAC, registration (self/parent/coach-bulk), division config, event ingest from hubs, results and read endpoints, hub-credential issuance/authentication |
| `backend/EventManager.Payments` | Payment-provider seam (`IPaymentProvider`) with a stub implementation; a real Stripe adapter is a drop-in replacement |
| `admin/EventManager.Hub` | The venue-day LAN hub: device pairing, offline RBAC, sync intake, bracket/scoring/weigh-in/finalization/dispute orchestration, backup/recovery, and the HTTP client that replicates the hub's event log to the cloud |
| `judge/EventManager.Judge*` | The Judge spoke app — durable-before-ack score capture, mat queue and cross-mat views |
| `checkin/EventManager.Checkin*` | The Check-In spoke app — append-only check-in and weigh-in recording against the weigh-in policy engine |
| `tests/`, `*/tests/` | xUnit test suites per component, including property-based tests (FsCheck) for the event-sourcing and bracket/scoring engines |

Native operational apps (Admin hub, Judge, Check-In) are built on .NET MAUI; each currently ships
as a compiling Windows desktop head, with Android/iOS/Mac heads pending those toolchains. A
browser-based web portal (Blazor) for pre-event registrant/coach self-service and organizer setup
is part of the product vision but not yet built — today, registration and event setup happen
through the cloud API directly.

## Current state

All core tournament-day capabilities are implemented and tested end to end: account/auth,
organizer RBAC, registration (including coach bulk entry), division configuration, bracket
generation and advancement, mat-scoped scoring, weigh-in recording and policy evaluation,
check-in, division finalization, dispute flagging, offline queuing with idempotent replay, local
backup/recovery, and HTTP-based hub-to-cloud replication with its own hub identity and credential
lifecycle. Payment processing is a stub behind a real interface, not a live integration.

Deliberately out of scope for now: the Spectator app, multi-event/federation management, a public
web portal, live payment processing, and transactional email — all noted as open items before a
public launch.

## Repository layout

```
shared/    Event-sourcing core, domain engines, contracts, client sync — consumed by every app
backend/   Cloud API (ASP.NET Core + PostgreSQL) and the payment-provider seam
admin/     The venue-day hub app (MAUI + embedded server)
judge/     The Judge spoke app
checkin/   The Check-In spoke app
tests/     Cross-solution integration tests (e.g. hub-to-cloud credential path)
postman/   API request collections
```

Each area under `shared/`, `backend/`, `admin/`, `judge/`, and `checkin/` has its own `tests/`
project alongside the source.

# U10 Application Design — Clarification Questions

**Stage**: INCEPTION → Application Design, Step 8 answer analysis
**Answers**: AD-Q1=A, AD-Q2=A, AD-Q3=A, AD-Q4=B, AD-Q5=C, AD-Q6=A, AD-Q7=A, AD-Q8=A, AD-Q9=C

Seven answers are unambiguous and internally consistent. Two have consequences the options did not state — both verified against the code before writing this, not inferred.

---

## CL-1 — AD-Q4=B makes `ReplicationClient` long-lived, and its store is scoped

You chose **B**: scheduling lives inside `ReplicationClient` rather than in a separate hosted service. The option said this means "a larger change to merged U7 code", and you accepted that. Two things it did **not** say:

**1. There is a dependency-lifetime problem.** Verified in `admin/EventManager.Hub/Program.cs`:

- `IEventStore` → `HubEventStore` is registered **scoped** (line 40)
- `HubDbContext` is **scoped** (line 16, `AddDbContext` default)
- `ReplicationClient` is **not registered in DI at all** today — it is constructed directly, and only in tests (`admin/tests/EventManager.Hub.Tests/ResilienceTests.cs:56,117`)

A `BackgroundService` is a singleton. A singleton cannot hold a scoped `IEventStore` — that is a captive dependency, and with `HubDbContext` (not thread-safe) underneath it, the failure mode is intermittent corruption under concurrent access rather than a clean startup error. So AD-Q4=B requires an explicit decision about how the now-long-lived client gets a store.

**2. It invalidates a line in the approved execution plan.** §4 step 5 isolates the `ReplicationClient` edit as "the only change to merged U7 code", and this design plan's own checklist says to "confirm it stays limited to retry classification". Under AD-Q4=B that edit also adds a channel consumer, two timers, a close-out path, and a service lifetime. **I will update U10-CON-6 and execution plan §4 step 5 to state the real scope** — that is a factual correction following from your answer, not a question. The quality gate that the 17 existing admin tests stay green across the change becomes considerably more important, and I will keep it.

**The question is only how the client obtains its store:**

A) **Inject `IServiceScopeFactory`** and create a scope per replication run. Idiomatic .NET for exactly this situation; leaves U4a's scoped registrations untouched; each run gets a clean `HubDbContext`. **My recommendation.**

B) **Inject `IDbContextFactory<HubDbContext>`** and construct a store per run from it. Equivalent isolation, more explicit about the DbContext specifically, but adds a factory registration alongside the existing `AddDbContext`.

C) **Re-register `IEventStore` and `HubDbContext` as singletons** so the client can hold them directly. Simplest injection — and the one I would argue against: `DbContext` is not thread-safe, the hub serves concurrent HTTP requests through those same scoped services, and this would change registrations U4a and U4b depend on.

X) Other (please describe after [Answer]: tag below)

[Answer]: A

---

## CL-2 — AD-Q9=C creates a project that no `dotnet test` run would reach

You chose **C**: a new `tests/EventManager.Integration.Tests` project at the repo root referencing both sides.

The repo has no root-level `tests/` directory and **five solutions** — `shared/`, `backend/`, `admin/`, `judge/`, `checkin/` — and `construction/build-and-test/` drives verification by running `dotnet test` against each solution in turn. A project that belongs to no solution is built and run by nothing: it would compile only if someone targeted it directly, and the credential-path test that F4=B was specifically added to protect would never fail a build. Since the whole point of that test is to catch a credential or scope regression automatically, this needs settling now rather than at Code Generation.

A) **A sixth solution** — `EventManager.Integration.slnx` at the repo root containing the new project; Build-and-Test instructions gain a sixth `dotnet test` line. Completes the isolation that made C attractive, and the coupling is visible at solution level. **My recommendation.**

B) **Add the project to `backend/EventManager.Backend.slnx`** — runs with the existing backend sweep, no new solution or build step, but it puts an `admin/`-referencing project inside the backend solution, which blurs the separation C was chosen for.

C) **Add it to `admin/EventManager.Admin.slnx`** — same trade-off in the other direction.

D) **Add the project file to both the admin and backend solutions.** No new solution; it runs in two sweeps (so the test executes twice per full verification).

X) Other (please describe after [Answer]: tag below)

[Answer]: A

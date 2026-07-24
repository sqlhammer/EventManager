# EventManager — Requirement Verification Questions

Your vision.md and tech-env.md are thorough, but the following points are ambiguous, contradictory, or left open. Please answer each question by filling in the letter choice after the [Answer]: tag. If none of the options match, choose the last option (Other) and describe your preference.

---

## Question 1
**Spectator app scope contradiction.** vision.md places "Spectator Mobile Experience" OUT of MVP (Phase 2), but tech-env.md repeatedly describes four client apps including Spectator (local SQLite on "all four client apps", read-only Spectator role in the sync topology). Which is correct for the MVP build?

A) MVP builds only Admin, Judge, and Check-In apps — no Spectator app at all (tech-env descriptions are forward-looking)

B) MVP includes a minimal Spectator app (read-only brackets/results on LAN only, no cellular/cloud path)

C) MVP includes the full Spectator experience (LAN + cellular delivery paths)

D) Other (please describe after [Answer]: tag below)

[Answer]: A

## Question 2
**Hot standby in MVP.** vision.md lists hub hot-standby under the full-scope "Offline-First Sync & Admin Hub" feature area and the Risks section says it "requires a hot-standby design from day one, not retrofitted later." How should MVP treat hub failover?

A) Design for it, build it in MVP — a second Admin device can run as hot standby and take over mid-event

B) Design for it in MVP (event-log architecture, promotion protocol designed and documented), but implement failover itself in a later phase

C) MVP relies on manual recovery only (restore from cloud replica or device backup); hot standby is fully deferred

D) Other (please describe after [Answer]: tag below)

[Answer]: C

## Question 3
**Sport/ruleset scope for scoring.** Martial arts tournaments score very differently (point sparring, continuous sparring, BJJ/grappling points & submissions, forms/kata judging panels). Which scoring model(s) must the MVP Judge app support?

A) One generic configurable model — win/loss + numeric score entry per match, no sport-specific rule enforcement

B) Point sparring + forms/kata (typical karate/TKD dojo tournament formats)

C) Grappling/BJJ model (points by position, advantages, penalties, submission outcomes)

D) Multiple sport-specific rulesets selectable per division (please list which after [Answer]: tag)

E) Other (please describe after [Answer]: tag below)

[Answer]: B, design for multiple styles being supported in the future.

## Question 4
**Bracket formats for MVP.** Which bracket/competition formats must MVP bracket generation support?

A) Single elimination only

B) Single elimination + round robin (common for small divisions of 3–4 athletes)

C) Single + double elimination + round robin

D) Other (please describe after [Answer]: tag below)

[Answer]: B, leave the design open for extending this.

## Question 5
**Free/paid tier enforcement in MVP.** The business model is free-tier-to-paid conversion, but no tier limits or billing mechanics are specified. What is in MVP scope?

A) No tier logic in MVP — everything is free; tiers/billing come later

B) Tier limits enforced (e.g., athlete cap per event) but paid-plan purchase handled manually (no self-serve billing)

C) Full self-serve tiers: free-tier limits + Stripe subscription/upgrade flow in MVP

D) Other (please describe after [Answer]: tag below; include the free-tier limit if you know it, e.g. max athletes per event)

[Answer]: A

## Question 6
**Stripe payment scope in MVP.** For athlete registration payments, what must MVP support?

A) Full Stripe integration: card payment at online registration, refunds handled in Stripe dashboard (not in-app)

B) Full Stripe integration plus in-app refund/cancellation handling and coupons/early-bird pricing (as listed in the full-scope vision)

C) Payments optional per event: organizer can run registration with "pay at the door" tracking only, Stripe if configured

D) Other (please describe after [Answer]: tag below)

[Answer]: C but leave the Stripe integration stubbed/mocked for MVP. We will not sign up with Stripe for the MVP.

## Question 7
**Repository/solution structure.** tech-env.md specifies "separate repository per app... with shared sync/event-log logic published as a versioned .NET package," but we are building in a single workspace (c:\repos\EventManager). What structure should code generation use?

A) Monorepo: one .NET solution in this workspace with separate projects per app (Admin, Judge, Check-In, backend) + shared library project referenced directly — can be split into separate repos/packages later

B) Monorepo now, but shared logic packaged as an actual local NuGet package to preserve the versioned-package consumption pattern

C) Simulate multi-repo: top-level folder per app, each with its own solution, shared library consumed via local NuGet feed

D) Other (please describe after [Answer]: tag below)

[Answer]: C

## Question 8
**LAN transport security (open question in tech-env.md).** How should MVP secure hub↔spoke LAN traffic (SignalR/WebSocket) given the implicitly-trusted-LAN model?

A) Plain HTTP/WS on the LAN for MVP — trust boundary is the venue network, as the LAN trust model states; revisit later

B) HTTPS/WSS with a self-signed cert generated by the hub, pinned by client apps during pairing (QR pairing exchanges the cert fingerprint)

C) Plain WS but with a per-event shared secret/pairing token required to join the hub (app-level auth, no transport encryption)

D) B + C combined: pinned self-signed TLS plus pairing token

E) Other (please describe after [Answer]: tag below)

[Answer]: E. I need more details about each option to make a decision.

## Question 9
**SQLite at-rest encryption (open question in tech-env.md).** Should local client databases be encrypted at rest in MVP?

A) No — rely on device OS security (device PIN/encryption); athlete data sensitivity is low

B) Yes — use SQLCipher or equivalent EF Core-compatible encryption on all clients

C) Encrypt only the Admin hub database (it holds the full roster incl. payment references), not Judge/Check-In devices

D) Other (please describe after [Answer]: tag below)

[Answer]: B

## Question 10
**Cloud deployment target.** tech-env.md says Docker on "VPS/ECS/ACI." Which concrete target should Infrastructure Design and deployment artifacts assume for MVP?

A) Single Docker-Compose VPS (e.g., Hetzner/DigitalOcean/Lightsail) — cheapest, fits solo-promoter scale

B) AWS ECS (Fargate) + RDS PostgreSQL

C) Azure Container Apps/ACI + Azure Database for PostgreSQL

D) Keep it provider-agnostic: Dockerfiles + compose only, no provider-specific IaC in MVP

E) Other (please describe after [Answer]: tag below)

[Answer]: D

## Question 11
**Weigh-in policy detail.** "Automatic range validation" is specified for weigh-in. What should happen when an athlete misses their registered weight class?

A) Flag only — staff sees an out-of-range warning; organizer resolves manually

B) Flag + assisted move: app suggests the correct division and organizer can approve a one-tap division move (brackets regenerate if not yet started)

C) Configurable per event: strict disqualification, auto-move, or allowance/tolerance percentage

D) Other (please describe after [Answer]: tag below)

[Answer]: C

## Question 12
**Athlete/coach accounts.** Organizers authenticate via ASP.NET Identity + JWT on the cloud backend. Do athletes/coaches who register online also need accounts in MVP?

A) No accounts — registration is a public form per event (email confirmation link only); coach bulk registration via a coach-specific form/upload without login

B) Lightweight accounts for coaches only (so they can manage their roster across events); athletes remain account-less

C) Accounts for both coaches and athletes (self-service edits, history)

D) Other (please describe after [Answer]: tag below)

[Answer]: C

---

# Extension Opt-In Questions

## Question 13: Security Extensions
Should security extension rules be enforced for this project?

A) Yes — enforce all SECURITY rules as blocking constraints (recommended for production-grade applications)

B) No — skip all SECURITY rules (suitable for PoCs, prototypes, and experimental projects)

X) Other (please describe after [Answer]: tag below)

[Answer]: A

## Question 14: Property-Based Testing Extension
Should property-based testing (PBT) rules be enforced for this project?

A) Yes — enforce all PBT rules as blocking constraints (recommended for projects with business logic, data transformations, serialization, or stateful components)

B) Partial — enforce PBT rules only for pure functions and serialization round-trips (suitable for projects with limited algorithmic complexity)

C) No — skip all PBT rules (suitable for simple CRUD applications, UI-only projects, or thin integration layers with no significant business logic)

X) Other (please describe after [Answer]: tag below)

[Answer]: A

## Question 15: Resiliency Extensions
Should the resiliency baseline be applied to this project?

**What this extension is.** Enabling it applies a set of **directional, design-time best practices** for building resilient systems, derived from the **AWS Well-Architected Framework (Reliability Pillar)** and resilience-review guidance. It steers requirements, design, and code toward fault tolerance, high availability, observability, and recoverability — covering 15 practice areas across business goals, change management, observability, high availability, disaster recovery, and continuous improvement.

**What this extension is NOT.** Enabling it does **not** make your workload production-ready, nor does it certify or guarantee any availability, RTO, or RPO target. It is a **starting point** that scaffolds good resiliency decisions early — it is not a substitute for a formal **AWS Well-Architected Review** of the built system.

Treat the output as a well-grounded **first draft of your resiliency posture** to build on and validate — not a finished, production-certified result.

A) Yes — apply the resiliency baseline as directional best practices and design-time guidance (recommended for business-critical workloads, as an informed starting point that you can validate and harden before go-live)

B) No — skip the resiliency baseline (suitable for PoCs, prototypes, and experimental projects where rapid iteration matters more than reliability)

X) Other (please describe after [Answer]: tag below)

[Answer]: A

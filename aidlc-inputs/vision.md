# EventManager — Vision

## Executive Summary

EventManager is offline-first tournament management software built for independent dojo owners running local martial arts tournaments of 50 to 300 athletes. Every competing platform in the market is cloud-only, and venue WiFi failure is the single most-cited catastrophic scenario on tournament day — EventManager solves this by running the full event (registration, brackets, check-in, scoring) on a local hub network with zero internet dependency. It targets solo promoters currently underserved by tools built for either large grappling federations or national taekwondo circuits. Success means dojo owners can run a real tournament on a free tier, trust it not to fail when the venue WiFi does, and convert to a paid plan as their events grow.

## Problem Statement

Independent dojo owners running 50–300 athlete tournaments have no tournament software built for their scale, budget, or risk tolerance. Every existing platform (Smoothcomp, TournamentTiger, Kihapp, MartialMatch, NinjaPanel) is priced or positioned for larger grappling federations or national circuits, and every one of them is entirely cloud-dependent — meaning a venue WiFi outage can take down registration, scoring, and results mid-event with no fallback.

This creates two compounding failures for the dojo owner: financial risk (opaque or per-athlete pricing that doesn't fit a small one-day event) and operational risk (a tournament that can grind to a halt the moment the internet does). EventManager addresses both by offering a genuine free tier for small events and an architecture that treats internet connectivity as a bonus rather than a requirement.

## Target Users
| User type | Needs |
| --------- | ----- |
| Tournament organizer (dojo owner) | Run a 50–300 athlete tournament with a fast setup, transparent free-to-paid pricing, and confidence the event won't fail if venue WiFi does |
| Judges/scorekeepers | Enter scores and outcomes on their assigned mat with zero cloud dependency, even fully offline |
| Check-in/weigh-in staff | Mark athletes present, record weight, and get automatic validation without needing internet |
| Athletes & spectators (parents, coaches) | Follow brackets and live results on their own phone, on the venue LAN or cellular |

## Success Metrics
| Metric | Target |
| ------ | ----- |
| Free-tier tournaments run | 10 tournaments in first year |
| Free → paid conversion rate | 15% within 90 days of first event |
| Zero-data-loss events (offline/LAN mode) | 100% — no lost scores/check-ins across any tournament, connected or not |

## Full Scope Vision

### Feature area: Registration & Payments
Online registration, Stripe payment collection, coach bulk registration, coupons, early-bird pricing.

### Feature area: Divisions, Brackets & Seeding
Division configuration by weight/rank/age/gender, automatic bracket generation, seeding, academy separation.

### Feature area: Offline-First Sync & Admin Hub
Embedded local server on the Admin app, event-log architecture, LAN sync, cloud replication when internet is available, hot standby for hub failover.

### Feature area: Judge Scoring & Live Results
Per-mat scoring apps, real-time result tabulation, dispute flagging.

### Feature area: Check-In & Weigh-In
Athlete check-in, weigh-in recording with automatic range validation, queue status.

### Feature area: Spectator Mobile Experience
Live bracket following, real-time score updates, LAN and cellular delivery paths.

### Feature area: Federation/Multi-Event Management
Multi-event umbrella management for organizers running a season or circuit of tournaments.

## MVP Scope — Features IN
| Feature | Rationale |
| ------- | --------- |
| Registration & Payments (incl. coach bulk registration) | Table stakes for a usable event tool; coach bulk registration matches how dojo owners actually enter athletes |
| Divisions, Brackets & Seeding | Core value proposition — automates the manual bracket work every organizer currently does by hand |
| Offline-First Sync & Admin Hub | The flagship differentiator — works even when venue WiFi fails |
| Check-In & Weigh-In | Required for event-day operations; low complexity, high daily utility |
| Judge Scoring & Live Results | Completes the event-day loop from check-in through results |

## MVP Scope — Features OUT
| Feature | Reason | Target phase |
| ------- | ------ | ------------ |
| Spectator Mobile Experience | Read-only, higher tolerance for staleness; not required to run an event | Phase 2 (right after MVP) |
| Federation/Multi-Event Management | Single-event flow needs to be proven first | v2+ |

## Risks and Open Questions
- Gym/membership-management integration (Mindbody, Jackrabbit Martial Arts, Pike13) is deliberately out of scope for now — open question on whether and when to build it
- mDNS device discovery can be blocked by some venue networks — needs a documented fallback (manual IP entry / QR code pairing) before launch
- Admin hub device failing mid-event is a critical-impact risk — requires a hot-standby design from day one, not retrofitted later
- Small-team/solo-dev support scalability is a risk every competitor in this market shares — worth deciding early how much organizer support capacity the team can realistically offer

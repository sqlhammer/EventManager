# EventManager — Hub-and-Spoke End-to-End Data Flow

As-built data flow across the spokes (U5 Judge / U6 Check-In), the admin hub (U4a Core / U4b
Competition / U7 Resilience), and the cloud backend (U3 / U8).

Two views are provided:
- **[`data-flow.svg`](data-flow.svg)** — a hand-drawn, precisely-laid-out SVG (open it directly in a
  browser or VS Code; it is theme-aware). This is the presentation view.
- The **Mermaid** source below — the editable, version-friendly source of truth (GitHub renders it
  natively; in VS Code use the "Markdown Preview Mermaid Support" extension).

## Presentation view (SVG)

![Hub-and-spoke data flow](data-flow.svg)

## Editable source (Mermaid)

```mermaid
flowchart TB
    subgraph SPOKES["SPOKES — Judge (U5) / Check-In (U6) · venue LAN"]
        SUI["Capture UI<br/>score · check-in · weigh-in"]
        SLOG["SpokeEventLog<br/>(durable-BEFORE-ack)"]
        SSTORE[("Local store<br/>IEventStore + LocalEventQueue outbox")]
        STX["ISyncTransport<br/>(WSS, cert-pinned)"]
        SUI -->|"1 capture"| SLOG
        SLOG -->|"2 persist locally"| SSTORE
        SLOG -->|"3 ack UI (only after persist)"| SUI
        SSTORE -->|"4 queued events"| STX
    end

    subgraph HUB["ADMIN HUB — U4a Core / U4b Competition / U7 Resilience"]
        PAIR["PairingService<br/>one-time token → DeviceCredential + workerId"]
        INTAKE["SyncIntakeService<br/>revocation + mat-authority check<br/>AppendIfNotExists (idempotent)"]
        HSTORE[("HubEventStore<br/>append-only event log (SQLite)")]
        HPROJ["HubProjectionHost<br/>device / bracket / standings"]
        COMP["Competition (U4b)<br/>ScoringEngine → BracketService.Advance<br/>WeighInResolution · Finalize · Disputes"]
        PUSH["IHubPush<br/>(SignalR)"]
        REPL["ReplicationClient (U7)<br/>ReplicationProtocol + retry/backoff<br/>completeness verify"]
        BACKUP["Backup / Recovery (U7)<br/>AES + SHA-256, replay-rebuild"]
        INTAKE -->|"6 idempotent append"| HSTORE
        HSTORE --> HPROJ
        HSTORE --> COMP
        COMP -->|"advance / finalize"| HSTORE
        HPROJ -->|"7 updates"| PUSH
        HSTORE --> REPL
        HSTORE <--> BACKUP
    end

    subgraph CLOUD["CLOUD BACKEND — U3 / U8 · internet"]
        REST["REST controllers<br/>accounts · events · divisions<br/>registration (U8 payment stub)"]
        INGEST["EventIngestController<br/>event-scoped, AppendIfNotExists (idempotent)"]
        CSTORE[("PostgresEventStore<br/>event-sourced + ASP.NET Identity")]
        CPROJ["CloudProjectionHost<br/>roster · results read models"]
        REST --> CSTORE
        INGEST -->|"10 idempotent append"| CSTORE
        CSTORE --> CPROJ
    end

    ORG["Organizer / Registrant / Coach"] -->|"pre-event setup (TLS)"| REST
    STX -->|"5 sync batch (X-Device-Id)"| INTAKE
    PUSH -->|"8 push: brackets / standings / revocation"| STX
    PAIR -.->|"QR + cert fingerprint"| STX
    REPL -->|"9 replicate when online<br/>(outage = no-op, resume gap-free)"| INGEST
    CSTORE ==>|"0 event download → readiness (pre-event)"| HSTORE
    CPROJ -->|"results / history"| ORG

    style SPOKES fill:#E8F5E9,stroke:#1B5E20
    style HUB fill:#FFF3E0,stroke:#E65100
    style CLOUD fill:#E3F2FD,stroke:#0D47A1
    style SLOG fill:#FFE082,stroke:#F57F17,color:#000
    style INTAKE fill:#FFCC80,stroke:#E65100,color:#000
    style REPL fill:#FFCC80,stroke:#E65100,color:#000
    style HSTORE fill:#FFA726,stroke:#E65100,color:#000
    style CSTORE fill:#90CAF9,stroke:#0D47A1,color:#000
```

## The numbered flow
- **0** — Before event day, the cloud event is **downloaded to the hub** (readiness gate); the hub then runs with zero internet.
- **1–4** — A spoke captures an action, writes it **durably to its local log before acking the UI** (NFR-1.1, zero loss), and queues it.
- **5–6** — The queued batch syncs to the hub; `SyncIntakeService` checks device revocation + mat authority and does an **idempotent append** (replays never duplicate).
- **7–8** — The hub folds projections / advances brackets and **pushes updates back** to spokes over SignalR.
- **9–10** — When the internet returns, the hub **replicates its log to the cloud** idempotently (an outage is a no-op that resumes gap-free); the cloud is a **mirror**, never a conflicting source of truth.

**Invariant across every hop:** `AppendIfNotExists` (idempotent) + durable-before-ack = the zero-loss, no-duplicate backbone. Backup/Recovery guards the hub log independently.

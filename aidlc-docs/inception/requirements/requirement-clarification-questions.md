# EventManager — Requirement Clarification Questions (Round 2)

Two sources for this round:

1. **Q8 follow-up** — you asked for more detail on the LAN transport security options before deciding.
2. **Resiliency Baseline questions** — you enabled the Resiliency extension (Q15: A). Its rules require several decisions to be made by *you*, not inferred by me: RTO/RPO & DR strategy, change management, CI/CD tooling, rollback mechanism, deployment style, regional topology, and incident response.

Please answer each question by filling in the letter choice after the [Answer]: tag.

---

## Question 1: LAN Transport Security — Detailed Options (resolves original Q8)

**Context.** During a tournament, the Admin hub's embedded Kestrel server talks to Judge and Check-In apps over the venue's WiFi. That traffic includes the full athlete roster (names, ages, weights — minors' PII), scores, and check-in status. Venue WiFi is typically a shared network: other parents, coaches, and athletes are on the same LAN. Note: you enabled the **Security Baseline** extension (Q13: A), whose SECURITY-01 rule requires TLS 1.2+ for data in transit and SECURITY-08 requires authenticated endpoints — this constrains which options can pass compliance without a documented exception.

### Option A — Plain HTTP/WS, open hub
**How it works:** Hub serves unencrypted HTTP/WebSocket. Any device that discovers the hub (mDNS or manual IP) can connect and sync. No pairing step.

- **Pros:** Zero setup friction on event day; simplest possible implementation; no certificate machinery; trivially debuggable (Wireshark, browser).
- **Cons:** Anyone on venue WiFi can read all traffic (roster PII, scores) and can *connect as a judge* and submit scores — no authentication at all. A malicious or curious spectator on the same WiFi could alter results.
- **Security Baseline impact:** Non-compliant with SECURITY-01 (encryption in transit) and SECURITY-08 (deny-by-default auth). Would require you to record a documented exception, and it would appear as a standing finding at every stage gate.
- **Effort:** ~0 extra work.

### Option B — Self-signed TLS, cert pinned at pairing
**How it works:** On event creation, the hub generates a self-signed certificate. Devices pair by scanning a QR code shown on the Admin screen; the QR encodes the hub's IP/port + the certificate's SHA-256 fingerprint. The client app connects over HTTPS/WSS and accepts *only* a cert matching that fingerprint (pinning). This defeats man-in-the-middle attacks without needing a public certificate authority.

- **Pros:** Real encryption of all LAN traffic; MITM-proof after pairing; works fully offline (no CA, no internet); QR pairing doubles as the documented mDNS fallback the vision requires.
- **Cons:** Encrypts but does **not authenticate clients** — anyone who photographs the QR (it's displayed on a screen in a public venue) or who ignores pinning can still connect and act as a judge. MAUI/platform TLS pinning callbacks add moderate implementation complexity; cert regeneration mid-event needs handling.
- **Security Baseline impact:** Satisfies SECURITY-01; still weak on SECURITY-08 (no client identity/authorization).
- **Effort:** Moderate (cert generation, pinning handshake on 3 platforms, QR pairing UI).

### Option C — Pairing token over plain WS
**How it works:** Traffic stays unencrypted, but to join the hub a device must present a per-event join token (entered manually or via QR). The hub authorizes the device and assigns its role (Judge mat 3, Check-In, etc.).

- **Pros:** Simple; gives the hub a real device-identity/role model (which you need anyway for "each Judge app is authoritative only for its own mat"); blocks casual join-and-tamper.
- **Cons:** All traffic still readable on shared WiFi (PII exposure); a sniffer can capture the token from the cleartext handshake and replay it, so the auth is only as strong as the network is trusted.
- **Security Baseline impact:** Satisfies part of SECURITY-08; non-compliant with SECURITY-01 (no transport encryption) — documented exception needed.
- **Effort:** Low–moderate (token issuance, role assignment, join flow).

### Option D — B + C combined: pinned TLS + pairing token (recommended)
**How it works:** One QR scan delivers hub address + cert fingerprint + a one-time enrollment token. The client connects over WSS (pinned cert), redeems the token, and receives a device credential + role (e.g., "Judge, Mat 2"). All subsequent traffic is encrypted and every connection is an identified, role-scoped device. Token is one-time-use, so a photographed QR is useless after the intended device enrolls.

- **Pros:** Encrypted **and** authenticated — the only option fully compliant with SECURITY-01 + SECURITY-08 with no exceptions; the device-identity/role model directly implements your mat-ownership authority rules and gives you a clean audit trail per device (helps the zero-data-loss/dispute story); QR pairing flow is shared with option B so much of the work overlaps.
- **Cons:** Highest implementation effort of the four (though B and C individually build most of its parts); slightly longer pairing flow on event morning (one scan per device, ~10 seconds each).
- **Security Baseline impact:** Fully compliant.
- **Effort:** Moderate–high, but it is essentially B + C, and C's role model is needed regardless for mat authority.

### Choose one:

A) Plain HTTP/WS, open hub (requires documented security exception)

B) Pinned self-signed TLS only

C) Pairing token over plain WS only (requires documented security exception)

D) Pinned self-signed TLS + one-time pairing token with device roles (Recommended — only fully compliant option)

E) Other (please describe after [Answer]: tag below)

[Answer]: D

---

# Resiliency Baseline — Required Decisions

These apply primarily to the **cloud backend** (the always-on production workload). Remember the architectural context: the Admin hub is the source of truth during an event and runs fully offline — the cloud is a mirror/replica. A cloud outage during a tournament does not stop the tournament.

## Question 2: RTO/RPO Goals and Disaster Recovery Strategy
What are your Recovery Time Objective (RTO) and Recovery Point Objective (RPO) goals for the cloud backend? These determine the appropriate Disaster Recovery strategy and infrastructure redundancy level.

A) RPO/RTO: Hours — Backup & Restore strategy. Lowest cost ($). Data backed up, no services deployed. Redeploy from IaC and restore from backups on failure. Suitable for non-critical workloads.

B) RPO/RTO: 10s of minutes — Pilot Light strategy. Cost: $$. Data live, services idle. Infrastructure deployed but not running, scaled up on failover.

C) RPO/RTO: Minutes — Warm Standby strategy. Cost: $$$. Data live, services run at reduced capacity. Scaled up during failover.

D) RPO/RTO: Near real-time — Multi-site Active/Active strategy. Highest cost ($$$$). Suitable for mission-critical, zero-downtime requirements.

E) N/A — Single-region deployment is acceptable, no cross-region DR needed. Rely on multi-zone availability within one region. (Note: arguably the natural fit here — the offline-first hub already protects event-day operations, and the hub can re-replay its event log to the cloud after any cloud outage, which effectively restores lost cloud data.)

X) Other (please describe after [Answer]: tag below)

[Answer]: E, consider warm standby for post-MVP phases

## Question 3: Change Management Process
How should production changes for this workload be governed? AI-DLC will conform the design to your answer rather than inventing a process.

A) Use our existing organizational change management process — provide the name/tool after the [Answer]: tag (e.g., ServiceNow, Jira Change, internal CAB).

B) No formal process exists yet — AI-DLC should propose a lightweight change management process (change record + approval + rollback note) for the team to adopt.

C) N/A — this workload is exempt from formal change management (e.g., solo-developer project). Document the exemption rationale.

X) Other (please describe after [Answer]: tag below)

[Answer]: B

## Question 4: CI/CD and Deployment Tooling
What CI/CD tooling and deployment process should this workload use?

A) Use our existing CI/CD pipeline — provide the tool after the [Answer]: tag (e.g., GitHub Actions, GitLab CI, Jenkins).

B) No pipeline exists — AI-DLC should propose a CI/CD pipeline definition appropriate to the chosen runtime (given the provider-agnostic Docker target from Q10, this would likely be GitHub Actions building/testing/pushing Docker images).

X) Other (please describe after [Answer]: tag below)

[Answer]: B

## Question 5: Rollback Mechanism
How should a failed production deployment of the cloud backend be rolled back?

A) Redeploy previous image/artifact version (version-pinned rollback — simplest, fits Docker Compose target)

B) Blue/green swap back to the previous environment

C) Canary auto-rollback on health/metric regression

D) Database-aware rollback required (EF Core migration reversal) — flag for explicit design

E) Other (please describe after [Answer]: tag below)

[Answer]: A

## Question 6: Deployment Style
What deployment strategy is acceptable for the cloud backend's risk profile? (Context: the hub tolerates cloud downtime by design — brief backend downtime during deploys does not interrupt live tournaments; queued events replay on reconnect.)

A) Direct / in-place (lowest cost, brief downtime during deploys) — arguably acceptable here given the hub's offline tolerance

B) Rolling (gradual instance replacement)

C) Blue/green (zero-downtime cutover, higher cost)

D) Canary (progressive traffic shift with automated rollback)

E) Other (please describe after [Answer]: tag below)

[Answer]: A

## Question 7: Regional Topology
Does the cloud backend require multi-region deployment, or is single-region with multi-zone redundancy sufficient? (If you answered E on Question 2, option A here is the consistent choice.)

A) Single-region, multi-zone — tolerates zone failure, not full-region failure. Lower cost.

B) Multi-region active-passive — survives region failure with failover. Higher cost.

C) Multi-region active-active — survives region failure with no downtime. Highest cost.

D) Other (please describe after [Answer]: tag below)

[Answer]: A

## Question 8: Incident Response Process
How are production incidents handled for this workload?

A) Use our existing incident response process — provide the reference after the [Answer]: tag (e.g., PagerDuty runbooks, internal on-call process).

B) No formal process exists — AI-DLC should propose a lightweight incident response and Correction of Errors (COE) process for adoption.

X) Other (please describe after [Answer]: tag below)

[Answer]: B

# Build Instructions

**Scope**: all 9 MVP units + post-MVP U9 and U10, across **six** solutions.
**Verified**: 2026-08-02 on `unit/u10-http-replication` — build succeeded, 0 errors, **2 warnings**.

> **The 0-warning gate is not currently met.** Two `SYSLIB0060` obsolescence warnings come from
> `admin/EventManager.Hub/Resilience/BackupRecovery.cs` (U7 code, unchanged since it was merged) and
> surface in both the admin and integration solutions. They are pre-existing — verified by building
> the branch with all U10 changes stashed — and are tracked as a follow-up, not a U10 regression.
>
> **An incremental build reports 0 warnings** because that file is not recompiled. Use
> `--no-incremental` when you need the true count.

---

## Prerequisites

| Requirement | Version / note |
|---|---|
| **.NET SDK** | 10.0.302 (verified). `dotnet --version` must report ≥ 10.0.x |
| **NuGet access** | Central package management via `Directory.Packages.props` — versions are pinned there, not per-project (SECURITY-10) |
| **Docker + Compose** | Only for running the cloud backend against PostgreSQL. Not needed to build or unit-test |
| **dotnet-ef** | `dotnet tool install --global dotnet-ef --version 10.0.0` — only for creating/applying migrations |
| **MAUI workloads** | `maui-windows` (installed). `maui-android` is installed but **unusable without a JDK + Android SDK** |
| **OS** | Windows for the MAUI heads. The libraries, cloud backend, and hub build on any .NET 10 platform |

### Environment variables
None are required to build. The cloud backend needs configuration only at **run** time — see
`backend/.env.example`. `Jwt:SigningKey` falls back to a development-only value and **throws** in
any non-Development environment.

---

## Build Steps

### 1. Restore dependencies
```bash
dotnet restore shared/EventManager.Shared.slnx
dotnet restore backend/EventManager.Backend.slnx
dotnet restore admin/EventManager.Admin.slnx
dotnet restore judge/EventManager.Judge.slnx
dotnet restore checkin/EventManager.Checkin.slnx
dotnet restore EventManager.Integration.slnx     # U10 cross-solution seam test
```

### 2. Build all units
There is no single root solution — the repo is five independent solutions, in dependency order:

```bash
dotnet build shared/EventManager.Shared.slnx      # U1 Domain/Sync, U2 Contracts/ClientSync
dotnet build backend/EventManager.Backend.slnx    # U8 Payments, U3 Api, U9 Read API
dotnet build admin/EventManager.Admin.slnx        # U4a Hub Core, U4b Competition, U7 Resilience
dotnet build judge/EventManager.Judge.slnx        # U5 Judge core + MAUI Windows head
dotnet build checkin/EventManager.Checkin.slnx    # U6 Check-In core + MAUI Windows head
```

`shared/` must build first — every other solution references those projects by path.

### 3. Verify build success
- **Expected output**: `Build succeeded. 0 Warning(s) 0 Error(s)` for each solution
- **Artifacts**: `bin/Debug/net10.0/` per project; MAUI heads emit to
  `bin/Debug/net10.0-windows10.0.19041.0/`
- **Acceptable warnings**: none. The build is warning-clean; treat any new warning as a regression

### 4. Release build (as CI does)
```bash
dotnet build backend/EventManager.Backend.slnx -c Release --no-restore
```

---

## Container build (cloud backend only)

```bash
cd backend
cp .env.example .env        # then set real values — see Secrets below
docker compose up -d --build
```

The build context is the **repo root** (`..`), not `backend/`, so the Dockerfile can copy both
`shared/` and `backend/`. Running `docker build` from inside `backend/` will fail to find `shared/`.

Only the Caddy proxy publishes a port (443). `docker-compose.override.yml` additionally publishes
PostgreSQL on 5432 for local IDE access; it is committed deliberately and must never reach a
production host.

---

## Troubleshooting

### Restore fails with package-version errors
**Cause**: a `PackageReference` carries an inline `Version` attribute, which conflicts with central
package management.
**Fix**: remove the inline version and declare it in `Directory.Packages.props`.

### `shared/` types not found when building `backend/` or `admin/`
**Cause**: `shared/` was not built first, or a project reference path is wrong.
**Fix**: build `shared/EventManager.Shared.slnx` first.

### MAUI head fails: JDK or Android SDK not found
**Cause**: `maui-android` is installed but the toolchain is not. Verified absent in this
environment — `java` is not on PATH and `ANDROID_HOME` is unset.
**Fix**: the Windows heads target `net10.0-windows` only and build without it. To add an Android
head, install a JDK 17+ and the Android SDK, then add the TFM to the head's `.csproj`. iOS/Mac
heads additionally require macOS with Xcode.

### Stray nested MAUI project after scaffolding
**Cause**: `dotnet new maui` resets the shell working directory.
**Fix**: always pass an **absolute** `-o` path. If a stray project appears inside a core library it
gets globbed into the compile, producing duplicate-`AssemblyInfo` errors — delete the nested
directory and clear that library's `bin`/`obj`.

### API returns 405 on an endpoint you just added
**Cause**: the running container predates the code — `docker compose up -d` reuses the existing
image.
**Fix**: check container age with `docker ps --format '{{.Names}}\t{{.CreatedAt}}'`, then
`docker compose up -d --build api`.

---

## Secrets

No credential is committed. `backend/.env.example` lists the required keys; real values go in
`backend/.env`, which is git-ignored. `Jwt:SigningKey` must be at least 32 characters and the
application refuses to start without it outside Development (SECURITY-12).

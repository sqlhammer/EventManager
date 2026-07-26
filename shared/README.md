# EventManager — Shared Libraries

Shared .NET class libraries consumed by the backend, hub, and spoke apps (D-07 layout). Built as versioned packages via a local NuGet feed.

## Packages (as of U2)
| Package | Purpose |
|---|---|
| `EventManager.Domain` | Pure domain model + correctness-critical engines (bracket, seeding, scoring, weigh-in, RBAC). No I/O. Heaviest PBT surface. |
| `EventManager.Sync` | Event-sourcing plumbing: `TournamentEvent`, `IEventStore`, idempotent replay, projections, Snowflake ids (IdGen), replication protocol. Independent of Domain (payloads are opaque). |
| `EventManager.Contracts` | Transport-level DTOs + `EventEnvelopeMapper` + FluentValidation validators (single source of truth for the wire). Domain REST/LAN DTOs grow with their consumers. |
| `EventManager.ClientSync` | Spoke offline behavior: durable queue, idempotent replay, bounded-backoff reconnect, typed push consumer, pairing. Transport is a seam (`ISyncTransport`/`IHubDiscovery`); SignalR impl provided at app wiring. |

## Build & test
```bash
dotnet build shared/EventManager.Shared.slnx
dotnet test  shared/EventManager.Shared.slnx
```
Requires the .NET 10 SDK. Runtime deps: IdGen (Snowflake), ErrorOr (result type), System.Text.Json. Test deps: xUnit, FsCheck.Xunit.

## Dependency versions (Central Package Management)

The whole repo uses [Central Package Management](https://learn.microsoft.com/nuget/consume-packages/central-package-management). **Every NuGet version is declared once** in the root [`Directory.Packages.props`](../Directory.Packages.props), and `.csproj` files reference packages *without* a `Version`:

```xml
<!-- Directory.Packages.props (root) -->
<PackageVersion Include="xunit" Version="2.9.3" />

<!-- any .csproj -->
<PackageReference Include="xunit" />
```

- **To add a package:** add a `<PackageVersion>` to the root file, then a version-less `<PackageReference>` in the project that needs it.
- **To bump/patch a version (incl. security pins):** edit the one line in `Directory.Packages.props` — it applies to every project and every solution.
- Projects specifying their own `Version` will fail the build (`CentralPackageVersionOverrideEnabled` is off). `NuGetAudit` runs in `all` mode, so transitive advisories surface at restore time.

## Conventions
- Immutable records; pure engines return `ErrorOr<T>` for expected failures.
- Identities are 64-bit Snowflakes (`Snowflake` in Domain; `long` on the wire in Sync).
- Single-writer contract on `IEventStore`/`ProjectionHost` (consumers serialize writes).

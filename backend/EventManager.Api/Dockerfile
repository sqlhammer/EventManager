# syntax=docker/dockerfile:1
# Multi-stage build for the EventManager cloud backend (NFR-6.4). Pinned base images (NFR-2.9);
# runs as a non-root user (SP-7/hardening).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Central Package Management: the root props file holds every NuGet version. It must sit at the
# build-context root so the version-less <PackageReference>s below resolve (else NU1015).
COPY Directory.Packages.props ./

# Copy the projects U3 depends on (shared libs + payments) + the API, then restore/publish.
COPY shared/ shared/
COPY backend/EventManager.Payments/ backend/EventManager.Payments/
COPY backend/EventManager.Api/ backend/EventManager.Api/
COPY backend/Directory.Build.props backend/

RUN dotnet publish backend/EventManager.Api/EventManager.Api.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
# Run as the non-root 'app' user (UID 1654) baked into the .NET base image (SP-7/hardening).
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "EventManager.Api.dll"]

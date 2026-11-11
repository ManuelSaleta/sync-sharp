# Sync.Sharp

[![NuGet Version](https://img.shields.io/nuget/v/Sync.Sharp.svg?style=flat-square)](https://www.nuget.org/packages/Sync.Sharp/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE)
[![.NET Compatibility](https://img.shields.io/badge/.NET-%3E%3D%2010.0-512BD4.svg?style=flat-square)](https://dotnet.microsoft.com/)

A modern, zero-trust media acquisition, VPN killswitch verification, Prowlarr/qBittorrent orchestration, `ffprobe` validation, and restricted `rsync` (`rrsync -wo`) transfer library & worker host for .NET 10.

Published under **ChaoticDreams** on NuGet and built atop [`Plex.Core.Http`](https://www.nuget.org/packages/Plex.Core.Http/).

---

## Features

- **VPN Tunnel Gate (`VpnGuardService`):** Verifies active WireGuard / NordLynx kernel network interfaces and IPv4 tunnel addresses prior to any indexer or swarm activity; automatically pauses active torrents if the VPN drops.
- **Plex Library Deduplication (`MediaPollingService` & `MediaTitleMatcher`):** Fuzzy release title normalization, year extraction, token subset matching, and Levenshtein similarity checks against Plex Media Server libraries and Hubs before grabbing releases.
- **Indexer & Quarantine Pipeline (`ProwlarrClient` & `QBitClient`):** Queries Prowlarr (`/api/v1/search`) with regex/size/seeder filters and season-pack scoring, dispatches magnets to qBittorrent in a paused **quarantine** state, and audits torrent file manifests to strip non-media files and reject path-traversal or executable payloads before resuming.
- **Zero-Trust Media Verification & Transfer (`MediaTransferService`):** Enforces strict staging directory boundaries, rejects symbolic links, validates container formats and video streams via `ffprobe`, and transfers verified media to remote NAS storage over SSH (`rrsync`).
- **One-Line Host & DI Support (`SyncSharpHost` & `AddSyncSharp`):** Run a complete standalone daemon in a single line (`await SyncSharpHost.RunAsync(args);`) or register individual services with `IServiceCollection.AddSyncSharp(configuration)`.

---

## Installation

Install the package via NuGet CLI:

```bash
dotnet add package Sync.Sharp
```

---

## Quickstart

### 1. One-Line Worker Host (`Program.cs`)

```csharp
using Sync.Sharp;

return await SyncSharpHost.RunAsync(args);
```

### 2. Dependency Injection (`IServiceCollection`)

```csharp
using Sync.Sharp.Extensions;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddSyncSharpConfiguration();
builder.Services.AddSyncSharp(builder.Configuration);

using var host = builder.Build();
await host.RunAsync();
```

### 3. Declarative Container Build (`Dockerfile`)

Because `Sync.Sharp` encapsulates the entire media synchronization engine and host builder as a NuGet package, infrastructure repositories (such as `homelab`) can build a self-contained single-file Alpine container image directly from NuGet without embedding raw C# source files:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS builder
ARG SYNC_SHARP_VERSION=1.0.*
WORKDIR /src

RUN dotnet new console -n SyncRunner && \
    dotnet add SyncRunner package Sync.Sharp --version "${SYNC_SHARP_VERSION}" && \
    printf 'return await Sync.Sharp.SyncSharpHost.RunAsync(args);\n' > SyncRunner/Program.cs && \
    dotnet publish SyncRunner/SyncRunner.csproj \
      -c Release \
      -r linux-musl-x64 \
      -p:PublishSingleFile=true \
      -p:SelfContained=true \
      -p:InvariantGlobalization=true \
      -p:AssemblyName=vpn-media-sync \
      -o /out

FROM alpine:3.21
RUN apk add --no-cache rsync openssh-client ffmpeg iproute2 tzdata libstdc++ \
    && adduser -D -u 1000 appuser \
    && mkdir -p /app /state /downloads /home/appuser/.ssh \
    && chown -R appuser:appuser /app /state /downloads /home/appuser/.ssh

WORKDIR /app
COPY --from=builder /out/vpn-media-sync /usr/local/bin/vpn-media-sync
USER appuser
ENTRYPOINT ["/usr/local/bin/vpn-media-sync"]
```

---

## Environment Variable Overrides

`AddSyncSharpConfiguration()` maps flat container/Kubernetes environment variables onto hierarchical options automatically:

| Environment Variable | Configuration Key | Description |
| :--- | :--- | :--- |
| `CONFIG_PATH` | *(File Overlay)* | Optional path to external `appsettings.json` |
| `PROWLARR_API_KEY` | `Indexer:ApiKey` | Prowlarr API key |
| `PROWLARR_BASE_URL` | `Indexer:BaseUrl` | Prowlarr base URL (default `http://127.0.0.1:9696`) |
| `QBIT_USERNAME` | `Downloader:Username` | qBittorrent WebUI username |
| `QBIT_PASSWORD` | `Downloader:Password` | qBittorrent WebUI password |
| `QBIT_BASE_URL` | `Downloader:BaseUrl` | qBittorrent WebUI URL (default `http://127.0.0.1:8080`) |
| `PLEX_SERVER_URL` | `PlexClientOptions:ServerUrl` | Plex Media Server URL |
| `PLEX_TOKEN` | `PlexClientOptions:Token` | Plex authentication token |
| `WEBHOOK_SECRET_TOKEN` | `Schedule:WebhookSecretToken` | Shared secret token for `POST /webhook` or `POST /sync` |
| `ENABLE_TRENDING_FEEDS` | `Trending:Enabled` | Enable/disable trending feeds |
| `POPULAR_MOVIES_URL` | `Trending:PopularMoviesUrl` | Override popular movies feed URL |
| `POPULAR_TV_URL` | `Trending:PopularTvUrl` | Override popular TV feed URL |

---

## Publishing to NuGet

### 1. Pack with .NET CLI

```bash
dotnet pack -c Release -o ./nupkg
```

### 2. Automated via GitHub Actions

- **`ci.yml`**: Builds, runs unit tests, and packs the NuGet package on every push and pull request to `main`.
- **`publish.yaml`**: Uses NuGet Trusted Publishing (`NuGet/login@v1` under `ChaoticDreams`) to publish to NuGet.org on release or `workflow_dispatch`.

---

## License

This project is licensed under the [MIT License](LICENSE).

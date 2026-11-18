# ==============================================================================
# Multi-Stage Dockerfile for vpn-media-sync (.NET 10 Single-File + Alpine)
# Stage 1: Compiles Sync.Sharp + SyncRunner into a self-contained musl binary
# Stage 2: Minimal Alpine runtime with rsync, openssh-client, ffprobe, & iproute2
# ==============================================================================

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS builder
WORKDIR /src

COPY src/Sync.Sharp/ src/Sync.Sharp/

RUN dotnet new console -n SyncRunner -o SyncRunner && \
    dotnet add SyncRunner/SyncRunner.csproj reference src/Sync.Sharp/Sync.Sharp.csproj && \
    printf 'return await Sync.Sharp.SyncSharpHost.RunAsync(args);\n' > SyncRunner/Program.cs && \
    dotnet publish SyncRunner/SyncRunner.csproj \
      -c Release \
      -r linux-musl-x64 \
      -p:PublishSingleFile=true \
      -p:SelfContained=true \
      -p:InvariantGlobalization=true \
      -o /out

FROM alpine:3.21
RUN apk add --no-cache \
      rsync \
      openssh-client \
      ffmpeg \
      iproute2 \
      tzdata \
      libstdc++ \
    && adduser -D -u 1000 appuser \
    && mkdir -p /app /state /downloads /home/appuser/.ssh \
    && chown -R appuser:appuser /app /state /downloads /home/appuser/.ssh

WORKDIR /app
COPY --from=builder /out/SyncRunner /usr/local/bin/vpn-media-sync

USER appuser
ENTRYPOINT ["/usr/local/bin/vpn-media-sync"]

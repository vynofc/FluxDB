# syntax=docker/dockerfile:1
# FluxDB Installer (Go) — Linux-Container, cross-compiled nach Windows (amd64)
# Build-Kontext: Installer/
#
#   docker build -t fluxdb-installer-build ./Installer

FROM golang:1.26-alpine AS build
WORKDIR /src
COPY . .
RUN go mod download
RUN CGO_ENABLED=0 GOOS=windows GOARCH=amd64 \
    go build -ldflags="-s -w" -o /out/FluxDB-Installer.exe .

# Export-Stage: Artefakt beim Container-Start nach /out (gemountetes Volume) kopieren
FROM alpine:3.22 AS export
WORKDIR /app
COPY --from=build /out/FluxDB-Installer.exe ./
CMD ["sh", "-c", "mkdir -p /out && cp /app/FluxDB-Installer.exe /out/ && echo 'Fertig: /out/FluxDB-Installer.exe'"]

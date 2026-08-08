# syntax=docker/dockerfile:1
# FluxDB Log-Viewer (Go) — Linux-Container, cross-compiled nach Windows (amd64)
# Build-Kontext: Log_Viewer/
#
#   docker build -t fluxdb-logviewer-build ./Log_Viewer

FROM golang:1.24-alpine AS build
WORKDIR /src
COPY . .
RUN go mod download
RUN CGO_ENABLED=0 GOOS=windows GOARCH=amd64 \
    go build -ldflags="-s -w" -o /out/Log_Viewer.exe .

# Export-Stage: Artefakt beim Container-Start nach /out (gemountetes Volume) kopieren
FROM alpine:3.22 AS export
WORKDIR /app
COPY --from=build /out/Log_Viewer.exe ./
CMD ["sh", "-c", "mkdir -p /out && cp /app/Log_Viewer.exe /out/ && echo 'Fertig: /out/Log_Viewer.exe'"]

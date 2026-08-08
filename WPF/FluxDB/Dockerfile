# syntax=docker/dockerfile:1
# FluxDB WPF-App — Windows-Container-Build (WPF kann nicht unter Linux gebaut werden)
# Build-Kontext MUSS das Repo-Root sein (wegen version.txt fuer die Assembly-Version).
#
#   docker build -f WPF/FluxDB/Dockerfile -t fluxdb-wpf-build .
#
# Erfordert: Docker Desktop im Windows-Container-Modus.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Zuerst nur version.txt + Projektdatei kopieren (besseres Layer-Caching fuer restore)
COPY version.txt ./
COPY WPF/FluxDB/FluxDB.csproj WPF/FluxDB/
RUN dotnet restore WPF/FluxDB/FluxDB.csproj

# Restliche Quellen kopieren und publish ausfuehren
COPY WPF/FluxDB/ WPF/FluxDB/
RUN dotnet publish WPF/FluxDB/FluxDB.csproj -c Release -o C:\publish --no-restore

# Export-Stage: Artefakte beim Container-Start nach C:\out (gemountetes Volume) kopieren
FROM mcr.microsoft.com/windows/servercore:ltsc2022 AS export
WORKDIR /app
COPY --from=build C:\publish C:\app\publish
CMD ["cmd", "/S", "/C", "xcopy /E /I /Y C:\\app\\publish C:\\out && echo Fertig: Artefakte liegen in C:\\out"]

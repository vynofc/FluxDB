# Plan: Konvertierung FluxDB.csproj zu SDK-Style

**TL;DR:** Die alte `FluxDB.csproj` durch eine minimale SDK-Style-Datei ersetzen, `packages.config` löschen und Pakete als `PackageReference` einbinden.

---

## Phase 1 – FluxDB.csproj neu schreiben

Die gesamte `WPF/FluxDB/FluxDB.csproj` ersetzen durch:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net472</TargetFramework>
    <UseWPF>true</UseWPF>
    <RootNamespace>FluxDB</RootNamespace>
    <AssemblyName>FluxDB</AssemblyName>
    <ApplicationIcon>FluxDB-icon.ico</ApplicationIcon>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <Deterministic>true</Deterministic>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="System.Data.SQLite.Core" Version="1.0.118.0" />
  </ItemGroup>
</Project>
```

Wesentliche Änderungen:
- `Sdk="Microsoft.NET.Sdk"` + `<UseWPF>true</UseWPF>` ersetzt die alten `ProjectTypeGuids` und `<Import>`-Zeilen
- Das SDK erkennt `.cs`/`.xaml` Dateien automatisch → alle expliziten `<Compile>`, `<Page>`, `<ApplicationDefinition>` Einträge entfallen
- `<GenerateAssemblyInfo>false</GenerateAssemblyInfo>` verhindert Konflikt mit der vorhandenen `Properties/AssemblyInfo.cs`, die das WPF-spezifische `[assembly: ThemeInfo(...)]` enthält
- `Stub.System.Data.SQLite.Core.NetFramework` entfällt — `System.Data.SQLite.Core` stellt bei `PackageReference` die nativen x64/x86-DLLs automatisch bereit
- Alle `<Content Include="bin\...">` Einträge (Build-Ausgaben, keine Quellen) entfallen

## Phase 2 – packages.config löschen

`WPF/FluxDB/packages.config` löschen — wird durch `PackageReference` im csproj ersetzt.

## Phase 3 – NuGet wiederherstellen

```powershell
dotnet restore WPF/FluxDB/FluxDB.csproj
```

---

## Verification

1. VS Code Fenster neu laden → Projekt sollte in C# Dev Kit ohne Fehler geladen werden
2. `msbuild WPF/FluxDB/FluxDB.csproj /p:Configuration=Debug` sollte erfolgreich bauen
3. Build-Ausgabe liegt jetzt unter `bin\Debug\net472\` statt `bin\Debug\` (SDK-Default) — ggf. `build.bat` prüfen

## Further Consideration

- Die Build-Skripte (`build.bat`, `build.sh`) und GitHub Actions übergeben `/p:OutDir=bin\` an MSBuild — das funktioniert weiterhin, weil `OutDir` den SDK-Standard überschreibt. Kein Handlungsbedarf, solange die Skripte nicht geändert werden.
- `GenerateAssemblyInfo=false` gewählt statt `AssemblyInfo.cs` zu löschen, weil die Datei `[assembly: ThemeInfo(...)]` enthält, das das SDK nicht auto-generiert.
- `Stub.System.Data.SQLite.Core.NetFramework` wurde nur als Old-Style-csproj-Workaround benötigt; PackageReference übernimmt die native DLL-Platzierung via `System.Data.SQLite.Core` direkt.

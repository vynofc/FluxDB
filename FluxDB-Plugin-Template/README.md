# FluxDB Plugin Template

This is a template for creating plugins for FluxDB.

## Quick Start

1. Copy this folder to your workspace
2. Rename the assembly in `PluginTemplate.csproj` (change `AssemblyName`)
3. Edit `MyPlugin.cs` to implement your plugin logic
4. Build the project:
   ```
   msbuild PluginTemplate.csproj /p:Configuration=Release
   ```
5. Copy the output DLL to the FluxDB `Plugins` folder:
   - `%LocalAppData%\FluxDB\Plugins\` or
   - `{FluxDB.exe directory}\Plugins\`

## Requirements

- .NET Framework 4.7.2
- Reference to `FluxDB.exe` (included as HintPath)

## Project Structure

| File | Description |
|---|---|
| `PluginTemplate.csproj` | Project file |
| `MyPlugin.cs` | Example plugin implementation |
| `Properties/AssemblyInfo.cs` | Assembly metadata |
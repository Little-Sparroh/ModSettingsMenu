# Mod Settings Menu

A MycoPunk mod that provides an in-game GUI for editing mod configuration files. Adapted from ToeKneeRED's 'MycoModList'.

## Description

Mod Settings Menu allows you to edit mod config files directly from within the game without needing to navigate to the config folder. This mod creates a clean, intuitive graphical user interface that displays all installed mods with configuration options. You can access the GUI by default with F10 (rebindable) or via a "Mod Config" button in the main menu.

Supports editing booleans, integers, floats, and strings. Changes are automatically saved to .cfg files and take effect immediately where possible.

## Getting Started

### Dependencies

* MycoPunk (base game)
* [BepInEx](https://github.com/BepInEx/BepInEx) - Version 5.4.2403 or compatible
* .NET Framework 4.8
* [HarmonyLib](https://github.com/pardeike/Harmony) (included via NuGet)

### Building/Compiling

1. Clone this repository and customize the following:
   - Rename namespace and class names appropriately
   - Modify PluginGUID to be unique (format: "author.modname")
   - Update PluginName and PluginVersion
   - Add your specific Harmony patches and functionality

2. Add any additional NuGet packages or references needed for your mod

3. Open the solution file in Visual Studio, Rider, or your preferred C# IDE

4. Build the project in Release mode to generate the .dll file

Alternatively, use dotnet CLI:
```bash
dotnet build --configuration Release
```

### Installing

**For distribution as a completed mod:**

**Option 1: Via Thunderstore (Recommended)**
1. Update `thunderstore.toml` with your mod's specific information
2. Publish using Thunderstore CLI or mod manager
3. Users download and install via Thunderstore Mod Manager

**Option 2: Manual Distribution**
1. Package the built .dll, any config files, and README
2. Users place the .dll in their `<MycoPunk Directory>/BepInEx/plugins/` folder

**Note:** This template is not meant to be installed directly - customize it first for your specific mod functionality.

### Executing program

Once customized and built, the mod will automatically load through BepInEx when the game starts. Check the BepInEx console for loading confirmation messages.

### Mod Development Structure

- **Plugin.cs:** Main plugin class with Awake method and Harmony initialization
- **thunderstore.toml:** Publishing configuration for Thunderstore
- **CSPROJECT.csproj:** Build configuration with proper references
- **Resources:** Icon and documentation placeholders

## Help

* **First time modding?** Check BepInEx documentation and MycoPunk modding resources
* **Harmony patches failing?** Ensure method signatures match the game's IL
* **Dependency issues?** Update NuGet packages and verify .NET runtime version
* **Thunderstore publishing?** Update all metadata in thunderstore.toml before publishing
* **Plugin not loading?** Check BepInEx logs for errors and verify GUID uniqueness

## Authors

* Sparroh (MycoPunk mod collection maintainer)
* ToeKneeRED (original Mod Author for the basis of this mod)
* funlennysub (original BepInEx template)
* [@DomPizzie](https://twitter.com/dompizzie) (README template)

## License

* This project is licensed under the MIT License - see the LICENSE.md file for details

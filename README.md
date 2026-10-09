# Paint.NET Format Add-in for Pinta

Opens Paint.NET images (`.pdn`) in Pinta 3.x, keeping layers, names, visibility, opacity and blend modes.
Read-only: images can be saved in any other format (e.g. `.ora` to keep layers).

Supports files saved by Paint.NET 3.x, 4.x and 5.x. Paint.NET's Additive, Reflect, Glow and Negation
blend modes have no Pinta equivalent and are opened as Normal.

Successor of the archived [PaintDotNetAddin](https://github.com/stevekew/PaintDotNetAddin), which relied on
`BinaryFormatter` (removed from modern .NET). This add-in uses a small NRBF reader instead, based on
[pypdn](https://github.com/addisonElliott/pypdn).

## Files

* `PdnFormatExtension.cs`: registers the format with Pinta
* `PdnImporter.cs`: builds the Pinta document
* `PdnReader.cs`: parses the `.pdn` file (header, document, layer pixels)
* `NrbfReader.cs`: minimal reader for the .NET binary serialization format used by `.pdn`

## Build

Requires the .NET 8 SDK (Pinta 3.1 runs on .NET 8). On macOS: `brew install dotnet@8`.

```bash
git submodule update --init   # Pinta sources, pinned to the 3.1.2 tag
dotnet build PdnFormatAddin -c Release
dotnet tool install --global Mono.Addins.UtilTool
mautil pack PdnFormatAddin/bin/Release/net8.0/PdnFormatAddin.dll   # creates PdnFormatAddin.1.0.mpack
```

## Install

* **Add-in Manager:** in Pinta open *Add-in Manager…* from the main menu, click the *Install from file…* button and pick `PdnFormatAddin.1.0.mpack`.
* **Manually:** copy `PdnFormatAddin.dll` into Pinta's add-ins folder and restart Pinta.
  On macOS: `~/Library/Application Support/Pinta/addins/addins/`.

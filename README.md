# Crybaby's IPAK Extractor

![Platform](https://img.shields.io/badge/platform-Windows-111111)
![Game](https://img.shields.io/badge/game-Black%20Ops%20II-f5b942)
![License](https://img.shields.io/badge/license-GPL--3.0-f5b942)

A native Xbox 360 Call of Duty: Black Ops II IPAK-to-DDS extractor. The
repository remains named `T6-Asset-Terminal`;

## Features

- Parses big-endian Xbox 360 BO2 IPAK headers, sections, indexes and blocks.
- Decompresses LZO1X texture commands.
- Groups streamed mip parts into complete images.
- Removes Xbox 360 texture tiling and endian layout.
- Writes standard BC1, BC3 and BC5/DXN DDS textures.
- Produces a clean output containing DDS files only.
- Does not require fastfiles or decompressed-zone folders at runtime.

## Usage

1. Download and extract the complete Windows release ZIP.
2. Run `Crybaby's IPAK Extractor.exe`.
3. Select an Xbox 360 BO2 `.ipak` file and an output folder.
4. Choose **Execute Extraction**.

## Validation testbed

Transit was the initial reverse-engineering and validation testbed, not the
identity or intended scope of the tool. Its `zm_transit_tm.ipak` contains 803
streamed mip-part entries which reconstruct into 257 complete DDS textures.
All 803 parts and all resulting DDS files were used to verify the pipeline.

The current packaged build embeds the verified Transit metadata catalog.
Additional BO2 IPAKs need equivalent image metadata before their names,
dimensions, GPU formats and mip grouping can be reconstructed reliably. The
IPAK parser, decompressor and Xbox texture conversion are format-level code.

## Build

```powershell
dotnet build .\src\T6AssetTool.Gui\T6AssetTool.Gui.csproj -c Release
```

Requires the .NET 8 SDK. Source is under `src/`. LZO1X decoding uses the
MIT-licensed `lzo.net` package.

This project is not affiliated with or endorsed by Activision or Treyarch.
Game assets remain the property of their respective owners.

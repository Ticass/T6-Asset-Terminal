# T6 Asset Terminal

Standalone Xbox 360 Black Ops II IPAK-to-DDS extractor for Transit.

![Platform](https://img.shields.io/badge/platform-Windows-111111)
![License](https://img.shields.io/badge/license-GPL--3.0-f5b942)

Run `dist/T6 Asset Terminal.exe`, select `zm_transit_tm.ipak`, choose an empty
output directory, and execute. No fastfile or decompressed-zone input is
required. The output directory contains only the 257 complete DDS textures.

The IPAK has 803 streamed mip-part entries. The embedded, verified Transit
metadata catalog groups all 803 parts into 257 named textures and supplies the
dimensions and GPU formats needed for correct DDS reconstruction.

Source is under `src/`. Xbox tiling, endian conversion, BC1/BC3/BC5 writing,
IPAK parsing and extraction are implemented in `T6AssetTool.Core`. LZO1X
decoding uses the MIT-licensed `lzo.net` package.

## Build

Requires the .NET 8 SDK:

```powershell
dotnet build .\src\T6AssetTool.Gui\T6AssetTool.Gui.csproj -c Release
```

This project is not affiliated with or endorsed by Activision or Treyarch.
Game assets remain the property of their respective owners.

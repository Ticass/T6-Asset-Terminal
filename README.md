# Crybaby's IPAK Extractor

![Platform](https://img.shields.io/badge/platform-Windows-111111)
![Game](https://img.shields.io/badge/game-Black%20Ops%20II-f5b942)
![License](https://img.shields.io/badge/license-GPL--3.0-f5b942)

A native Xbox 360 Call of Duty: Black Ops II IPAK-to-DDS extractor. The
repository remains named `T6-Asset-Terminal`;

## Features

- Parses BO2 IPAK headers, sections, indexes and blocks, preserving source
  endian when repacking.
- Decompresses LZO1X texture commands.
- Groups streamed mip parts into complete images.
- Reads BO2 IWI texture payloads directly when the IPAK stores standalone image
  chunks.
- Removes Xbox 360 texture tiling and endian layout for streamed raw mip parts.
- Writes standard BC1, BC3 and BC5/DXN DDS textures.
- Produces a clean output containing DDS files only.
- Does not require fastfiles or decompressed-zone folders at runtime.
- Builds the image catalog from the package's own metadata section, so any IPAK
  that carries one can be extracted with no zone and no prepared catalog.
- Falls back to hash-named DDS extraction for IPAKs that carry no image metadata.
- Repacks an IPAK, swapping individual images while every other entry is copied
  block-for-block. A repack with no swaps reproduces the source file byte for
  byte -- verified against all 179 IPAKs of a retail Xbox 360 install.

## Usage

1. Download and extract the complete Windows release ZIP. Keep the DLLs beside
   the executable.
2. Run `Crybabys IPAK Extractor.exe`.
3. Pick a mode:
   - **Extract (IPAK to DDS)** -- choose an `.ipak` and an output folder, then
     **Execute Extraction**.
   - **Repack (DDS to IPAK)** -- choose the source `.ipak`, the folder holding
     your modified `.dds` files, and where to write the rebuilt `.ipak`, then
     **Execute Repack**.

> The executable filename has no apostrophe. An apostrophe in the .NET assembly
> name made WPF fail to parse its own compiled XAML, so v1.1.0 exited before it
> could draw a window. The application title is unchanged.

## Repacking

Available in the GUI (**Repack** mode) and on the command line:

```
T6AssetTool.Cli repack <input.ipak> [swaps_dir] <output.ipak>
```

`swaps_dir` holds `<image_name>.dds` files; each name is hashed the way the game
hashes it and matched against the index. Hash-named fallback files such as
`hash_01234567_89abcdef.dds` target that exact index entry. DDS replacements
are written back as BO2 IWI payloads for standalone IPAKs, while a
`<namehash>:<datahash>.bin` file replaces one part's payload verbatim instead.

Entries that are not being swapped are copied as stored, so the command modes
this codebase cannot decode pass through untouched. Name hashes and data hashes
are never rewritten -- the matching fastfile's streamed-part descriptors look
images up by those, and the zone also carries each part's dimensions and level
size, so a replacement that keeps the original size needs no fastfile edit while
one that changes size does.

## Validation testbed

Transit was the initial reverse-engineering and validation testbed, not the
identity or intended scope of the tool. Its `zm_transit_tm.ipak` contains 803
streamed mip-part entries which reconstruct into 257 complete DDS textures.
All 803 parts and all resulting DDS files were used to verify the pipeline.

The build embeds the verified Transit catalog, which still takes precedence for
`zm_transit_tm.ipak`. Every other package is catalogued from its own metadata
section (`iwi:`, `format:`, `size:`, `width:`, `height:`, `levels:`, `mip:` per
streamed part), so no zone is needed to recover names, dimensions, GPU formats
or mip grouping.

Measured across all 183 IPAKs of a retail Xbox 360 install, `format:` is only
ever DXT5, DXN, DXT1, DXT3, or one of four uncompressed forms totalling 818
records out of 378k. Only the block-compressed four can be written as BC DDS;
anything else is reported and skipped rather than written incorrectly.

Some packages carry no metadata section at all -- index and data only. For those
the extractor parses each decoded BO2 IWI header and writes hash-named files
such as `hash_01234567_89abcdef.dds`. The original image names are not present
in those IPAKs, but width, height, mip count and BC format are read from the
IWI payload instead of guessed from byte size.

## Build

```powershell
dotnet build .\src\T6AssetTool.Gui\T6AssetTool.Gui.csproj -c Release
```

Requires the .NET 8 SDK. Source is under `src/`. LZO1X decoding uses the
MIT-licensed `lzo.net` package.

This project is not affiliated with or endorsed by Activision or Treyarch.
Game assets remain the property of their respective owners.

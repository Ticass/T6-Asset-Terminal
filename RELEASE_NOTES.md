# T6 Asset Terminal v1.0.0

Initial public release of the standalone Xbox 360 Black Ops II Transit
IPAK-to-DDS extractor.

## Features

- Extracts `zm_transit_tm.ipak` without requiring fastfiles or decompressed zones.
- Reconstructs 257 complete textures from all 803 streamed IPAK parts.
- Produces clean, named DDS files only.
- Handles Xbox 360 texture tiling and endian conversion.
- Supports BC1, BC3 and BC5/DXN textures.
- Includes a Bloomberg Terminal × Vercel-inspired Windows interface.
- Runs as a self-contained Windows x64 application.

## Installation

1. Download `T6-Asset-Terminal-v1.0.0-win-x64.zip`.
2. Extract the entire ZIP to a folder.
3. Run `T6 Asset Terminal.exe`.
4. Select `zm_transit_tm.ipak` and an output directory.
5. Choose **Execute Extraction**.

Keep all extracted runtime DLLs beside the executable.

## Verification

- IPAK parts processed: **803/803**
- Complete DDS textures produced: **257**
- Extraction failures: **0**

SHA-256 of the release ZIP:

`f808306ad46b79fc7f708bac100334246dee13c3d73c91d74574025acfb157bf`

## Scope

This release includes an embedded metadata catalog specifically for the Xbox
360 Transit package, `zm_transit_tm.ipak`. Other BO2 IPAKs are not supported by
this version.

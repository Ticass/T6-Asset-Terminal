# Crybaby's IPAK Extractor v1.1.0

Branding and documentation update for the Xbox 360 Black Ops II IPAK-to-DDS
extractor. The GitHub repository remains named `T6-Asset-Terminal`.

## Changes

- Renamed the application to **Crybaby's IPAK Extractor**.
- Reframed Transit as the initial validation testbed rather than the tool name.
- Replaced the GPL LZO dependency with the MIT-licensed `lzo.net` decoder.
- Simplified the runtime workflow to IPAK input and DDS output only.
- No fastfile or decompressed-zone input is required at runtime.

## Installation

1. Download `Crybabys-IPAK-Extractor-v1.1.0-win-x64.zip`.
2. Extract the complete ZIP.
3. Run `Crybaby's IPAK Extractor.exe`.
4. Select an IPAK and clean output directory.

## Transit validation

- Streamed parts processed: **803/803**
- Complete DDS textures: **257**
- Extraction failures: **0**

The current packaged build contains the verified Transit image metadata
catalog. See the README for the distinction between the general IPAK pipeline
and packaged metadata coverage.

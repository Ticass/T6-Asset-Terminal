# Crybaby's IPAK Extractor v1.3.0

## Any IPAK now catalogues itself

Extraction used to need an image-metadata catalog compiled into this assembly,
so the only package that opened was `zm_transit_tm.ipak`. Everything else failed
with *"No embedded image metadata catalog"*.

It turns out no zone and no prepared catalog are needed: retail already stores
the information in the package. Section 3 is a run of records -- name hash, data
hash, text length, then ASCII -- one per streamed mip part:

```
iwi: images/~-gshells_green_c.iwi
format: DXT1
offset: 40960
size: 20480
width: 64
height: 64
levels: 7
mip: 2
manual: 0
```

That is everything the zone scanner recovers from a fastfile. The tool now reads
it directly, so opening any package with a metadata section just works.

Verified on `zm_prototype.ipak`, which previously could not be opened at all:
**377 textures, 0 failed**, spanning every block format the game uses
(DXT5 164, ATI2/DXN 111, DXT1 86, DXT3 16). Header checks on the output match
the package's own width, height, FourCC and mip count.

Two details worth knowing:

- The metadata section describes more parts than a package stores --
  `zm_transit_tm.ipak` has 5372 records against 803 index entries -- so parts
  are matched to the index by `(nameHash, dataHash)` and the rest ignored.
- Metadata `levels:` is the count for that part alone, whereas the extractor
  wants counts cumulative from the smallest part up. They are accumulated on the
  way in, which also keeps the mip maths right when a package is missing one of
  an image's parts.

The embedded Transit catalog still takes precedence where it exists, so that
path is unchanged: 257 images from 803 parts, exactly as before.

## Limits, stated plainly

Measured across all 183 IPAKs of a retail Xbox 360 install, `format:` is only
ever DXT5 (158609), DXN (114409), DXT1 (98227), DXT3 (6688), or one of four
uncompressed forms (A8L8, X8R8G8B8, A8R8G8B8, L8) totalling 818 records. Only
the block-compressed four can be written as BC DDS; an image in any other format
is reported and skipped rather than written incorrectly.

26 of those 183 packages carry no metadata section at all -- index and data only
-- and nothing in them names their images. Those still need a matching zone, and
now say so explicitly instead of naming a missing catalog file.

---

# v1.2.0

## Fixed: v1.1.0 would not open

v1.1.0 exited immediately with no window and no error dialog.

The rebrand in v1.1.0 set the .NET assembly name to `Crybaby's IPAK Extractor`.
WPF compiles XAML to BAML that records the owning assembly by name, and
`Baml2006SchemaContext.ResolveAssembly` hands that string to
`System.Reflection.AssemblyName`, whose parser treats `'` as a quote delimiter.
So the very first `InitializeComponent()` threw:

```
System.Windows.Markup.XamlParseException: The given assembly name was invalid.
 ---> System.IO.FileLoadException: The given assembly name was invalid.
      File name: 'Crybaby's IPAK Extractor'
```

The process died with `0xE0434352` before drawing anything, which is why v1.0.0
worked and v1.1.0 did not. The assembly name is now `Crybabys IPAK Extractor`
and the branding moved to `Product` / `AssemblyTitle` and the window title.
**The executable is `Crybabys IPAK Extractor.exe`**; the displayed application
name is unchanged.

## Added: IPAK repacking in the GUI

An **Extract / Repack** mode switch. In Repack mode you choose the source
`.ipak`, a folder of modified `.dds` files, and the path for the rebuilt
`.ipak`. Each `.dds` is hashed the way the game hashes image names, matched
against the index, and re-tiled into the Xbox 360 layout. Entries you did not
touch are copied through block for block, so a repack with no replacements
reproduces the source byte for byte -- verified against all 179 IPAKs of a
retail install.

Name hashes and data hashes are never rewritten. The matching fastfile's
streamed-part descriptors look images up by those, and the zone also carries
each part's dimensions and level size, so a replacement that keeps the original
size needs no fastfile edit, while one that changes size does.

## Also in v1.2.0

- Removed hardcoded local paths baked into the input boxes.
- Inline image extraction for zones (`AssetExtractor.RunZoneInline`).
- DDS FourCC honours the GPU format byte, adding BC3 and BC5/DXN alongside BC1.
- `repack-add` CLI verb for inserting new entries into a package.
- Hoisted four `stackalloc`s out of loops in the metadata builders (CA2014).

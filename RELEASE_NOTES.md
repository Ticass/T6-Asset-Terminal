# Crybaby's IPAK Extractor v1.2.0

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
   at System.Reflection.AssemblyNameParser.ThrowInvalidAssemblyName()
```

The process died with `0xE0434352` before drawing anything. That is why v1.0.0
worked and v1.1.0 did not.

The assembly name is now `Crybabys IPAK Extractor` and the branding moved to
`Product` / `AssemblyTitle` and the window title, which are all free to contain
an apostrophe. **The executable is `Crybabys IPAK Extractor.exe`** -- the
displayed application name has not changed.

## Added: IPAK repacking in the GUI

The repacker was previously command-line only. The main window now has an
**Extract / Repack** mode switch.

In **Repack** mode you choose the source `.ipak`, a folder of modified `.dds`
files, and the path for the rebuilt `.ipak`. Each `.dds` is hashed the way the
game hashes image names, matched against the package index, and re-tiled into
the Xbox 360 layout. Entries you did not touch are copied through block for
block, so command modes this codebase cannot decode still survive a rebuild --
and a repack with no replacements reproduces the source file byte for byte,
verified against all 179 IPAKs of a retail Xbox 360 install.

Name hashes and data hashes are never rewritten. The matching fastfile's
streamed-part descriptors look images up by those, and the zone also carries
each part's dimensions and level size, so a replacement that keeps the original
size needs no fastfile edit, while one that changes size does.

`OPEN OUTPUT` reveals the rebuilt package in Explorer when repacking, instead of
opening the DDS folder you fed in.

## Also in this release

- Removed hardcoded local paths that were baked into the input boxes, so the
  fields start empty instead of pointing at a developer's machine.
- Inline image extraction for zones (`AssetExtractor.RunZoneInline`).
- DDS FourCC now honours the GPU format byte, adding BC3 and BC5/DXN alongside
  BC1.
- `repack-add` CLI verb for inserting new entries into a package.
- Hoisted two `stackalloc`s out of loops in the metadata builders (CA2014).

## Known limitations

Extraction still needs an embedded image-metadata catalog for the package being
opened; the shipped build carries the verified Transit catalog. Repacking has no
such requirement and works on any BO2 IPAK.

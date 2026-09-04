using System.Buffers.Binary;
using System.Text;

namespace T6AssetTool.Core;

public sealed record RepackResult(int Entries, int Replaced, IReadOnlyList<string> Log);
public sealed record IPakAddition(uint NameHash, uint DataHash, byte[] Data, string Metadata);

/// <summary>
/// Rebuilds an existing .ipak, optionally swapping payloads. Every entry keeps its name hash and
/// data hash, because those are what the matching .ff's streamed-part descriptors look up -- change
/// them and the zone stops finding its pixels.
///
/// The data section is carried over byte-for-byte and only the replaced slots are overwritten, so
/// entries this tool cannot decode (the LZO and still-unidentified command modes) pass through
/// untouched, and a repack with no replacements reproduces the source file exactly.
/// </summary>
public static class IPakRepacker
{
    const byte SlotFiller = 0x93;   // what retail leaves between entries inside the data section

    /// <param name="replacements">Keyed by name hash, or by "namehash:datahash" for a specific part.</param>
    public static RepackResult Repack(string sourcePath, string outputPath,
                                      IReadOnlyDictionary<string, byte[]> replacements, Action<string>? log = null,
                                      IReadOnlyList<IPakAddition>? additions = null)
    {
        var lines = new List<string>();
        void Say(string m) { lines.Add(m); log?.Invoke(m); }

        using var src = new IPakReader(sourcePath);
        if (replacements.Count == 0 && (additions is null || additions.Count == 0))
        {
            File.Copy(sourcePath, outputPath, overwrite: true);
            Say($"DONE  {src.Entries.Count} entries, 0 replaced -> {Path.GetFileName(outputPath)}");
            return new(src.Entries.Count, 0, lines);
        }

        bool bigEndian = src.BigEndian;
        byte[] data = src.SectionBytes(2), pairs = src.SectionBytes(4), metaSection = src.SectionBytes(3);
        var meta = ParseMetadata(metaSection);
        var index = src.Entries.OrderBy(e => e.Offset).ToList();   // file order, not the hash-sorted index order
        int replaced = 0; bool metaChanged = false;

        for (int i = 0; i < index.Count; i++)
        {
            var e = index[i];
            string exact = $"{e.NameHash:x8}:{e.DataHash:x8}", loose = $"{e.NameHash:x8}";
            byte[]? swap = replacements.GetValueOrDefault(exact) ?? replacements.GetValueOrDefault(loose);
            if (swap is null) continue;                            // untouched slots keep their original bytes

            byte[] blocks = IPakWriter.EncodeBlocks(swap, bigEndian);
            int was = OriginalSize(src, e);
            if (was >= 0 && swap.Length != was)
            {
                Say($"SIZE  {exact}: {was} -> {swap.Length} bytes; the zone still reports the old levelSize, " +
                    "so only do this alongside a matching .ff edit");
                if (meta.TryGetValue((e.NameHash, e.DataHash), out string? text))
                { meta[(e.NameHash, e.DataHash)] = ResizeMetadata(text, swap.Length)!; metaChanged = true; }
            }

            if (blocks.Length <= e.StoredSize)
            {
                // Fits its slot: overwrite in place and refill the tail, so every other offset holds.
                blocks.CopyTo(data, e.Offset);
                Array.Fill(data, SlotFiller, (int)e.Offset + blocks.Length, (int)e.StoredSize - blocks.Length);
            }
            else
            {
                // Outgrew its slot: park it past the end of the section on a 0x8000 boundary and
                // leave the old bytes where they are -- nothing points at them any more.
                int start = (data.Length + IPakWriter.SectionAlign - 1) & ~(IPakWriter.SectionAlign - 1);
                int oldLength = data.Length;
                Array.Resize(ref data, start + blocks.Length);
                if (start > oldLength) Array.Fill(data, SlotFiller, oldLength, start - oldLength);
                blocks.CopyTo(data, start);
                index[i] = e with { Offset = (uint)start };
                Say($"MOVE  {exact}  relocated to 0x{start:X}");
            }
            index[i] = index[i] with { StoredSize = (uint)blocks.Length };
            replaced++;
            Say($"SWAP  {exact}  {swap.Length} bytes -> {blocks.Length} stored");
        }

        foreach (var add in additions ?? [])
        {
            if (index.Any(e => e.NameHash == add.NameHash && e.DataHash == add.DataHash))
                throw new InvalidDataException($"IPAK entry {add.NameHash:x8}:{add.DataHash:x8} already exists");

            byte[] blocks = IPakWriter.EncodeBlocks(add.Data, bigEndian);
            int start = (data.Length + IPakWriter.SectionAlign - 1) & ~(IPakWriter.SectionAlign - 1);
            int oldLength = data.Length;
            Array.Resize(ref data, start + blocks.Length);
            if (start > oldLength) Array.Fill(data, SlotFiller, oldLength, start - oldLength);
            blocks.CopyTo(data, start);
            index.Add(new(add.NameHash, add.DataHash, (uint)start, (uint)blocks.Length));
            meta[(add.NameHash, add.DataHash)] = add.Metadata;
            Say($"ADD   {add.NameHash:x8}:{add.DataHash:x8}  {add.Data.Length} bytes -> {blocks.Length} stored at 0x{start:X}");
        }

        bool added = additions is { Count: > 0 };
        if (added) metaSection = BuildMetadataForEntries(index, meta);
        else if (metaChanged) metaSection = BuildMetadata(metaSection, meta);

        var idx = new MemoryStream();
        Span<byte> entry = stackalloc byte[16];   // hoisted: stackalloc inside the loop grows the frame per iteration
        foreach (var e in index.OrderBy(e => e.NameHash).ThenBy(e => e.DataHash))
        {
            Span<byte> row = entry;
            WriteUInt32(row, e.NameHash, bigEndian);
            WriteUInt32(row[4..], e.DataHash, bigEndian);
            WriteUInt32(row[8..], e.Offset, bigEndian);
            WriteUInt32(row[12..], e.StoredSize, bigEndian);
            idx.Write(row);
        }
        byte[] indexSection = idx.ToArray();

        if (added)
        {
            var rebuiltPairs = new MemoryStream();
            Span<byte> row = stackalloc byte[8];   // hoisted: stackalloc inside the loop grows the frame per iteration
            foreach (var e in index.OrderBy(e => e.NameHash).ThenBy(e => e.DataHash))
            {
                WriteUInt32(row, e.NameHash, bigEndian);
                WriteUInt32(row[4..], e.DataHash, bigEndian);
                rebuiltPairs.Write(row);
            }
            pairs = rebuiltPairs.ToArray();
        }

        // Each section keeps its source offset and count field; only its body may have changed.
        var bodies = new Dictionary<uint, byte[]> { [2] = data, [1] = indexSection, [4] = pairs, [3] = metaSection };
        var layout = src.Sections
            .Select(s => (s.Type, s.Offset, Body: bodies.GetValueOrDefault(s.Type) ?? src.SectionBytes(s.Type),
                          Count: added && s.Type is 1 or 2 or 3 or 4 ? (uint)index.Count : s.Count))
            .ToList();
        if (!IPakWriter.TryWriteAt(outputPath, layout, src.TotalSize, bigEndian))
        {
            Say("GREW  a section outgrew its slot; rebuilding the file with a fresh 0x8000-aligned layout");
            IPakWriter.WriteRelaid(outputPath, layout, bigEndian);
        }
        Say($"DONE  {index.Count} entries, {replaced} replaced -> {Path.GetFileName(outputPath)}");
        return new(index.Count, replaced, lines);
    }

    /// <summary>Reads a DDS written by <see cref="DdsWriter"/> back into an Xbox IWI payload.</summary>
    public static byte[] DdsToPayload(string ddsPath)
    {
        byte[] dds = File.ReadAllBytes(ddsPath);
        if (dds.Length < 128 || Encoding.ASCII.GetString(dds, 0, 4) != "DDS ") throw new InvalidDataException($"{ddsPath} is not a DDS");
        int height = (int)L(dds, 12), width = (int)L(dds, 16), mips = Math.Max(1, (int)L(dds, 28));
        string fourCc = Encoding.ASCII.GetString(dds, 84, 4);
        if (fourCc is not ("DXT1" or "DXT5" or "ATI2")) throw new NotSupportedException($"Unsupported DDS format {fourCc}");
        return BuildIwi(dds.AsSpan(128), width, height, mips, fourCc);
    }

    /// <summary>Collects replacements from a folder of &lt;name&gt;.dds files, resolving names to hashes.</summary>
    public static Dictionary<string, byte[]> FromFolder(string folder, Action<string>? log = null)
    {
        var map = new Dictionary<string, byte[]>();
        foreach (string file in Directory.EnumerateFiles(folder))
        {
            string name = Path.GetFileNameWithoutExtension(file), ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext == ".dds")
            {
                byte[] payload = DdsToPayload(file);
                if (TryFallbackHashName(name, out string exact))
                { map[exact] = payload; log?.Invoke($"LOAD  {name}.dds -> {exact}"); }
                else
                { map[$"{ZoneImageScanner.HashName(name):x8}"] = payload; log?.Invoke($"LOAD  {name}.dds -> {ZoneImageScanner.HashName(name):x8}"); }
            }
            else if (ext == ".bin" && name.Contains(':'))
            { map[name.ToLowerInvariant()] = File.ReadAllBytes(file); log?.Invoke($"LOAD  {name}.bin (raw payload)"); }
            else if (ext == ".bin")
            { map[$"{ZoneImageScanner.HashName(name):x8}"] = File.ReadAllBytes(file); log?.Invoke($"LOAD  {name}.bin -> {ZoneImageScanner.HashName(name):x8}"); }
        }
        return map;
    }

    static byte[] BuildIwi(ReadOnlySpan<byte> ddsPixels, int width, int height, int mipCount, string fourCc)
    {
        int bpb = fourCc == "DXT1" ? 8 : 16;
        byte[] payload = new byte[64 + ddsPixels.Length];
        "IWi"u8.CopyTo(payload);
        payload[3] = 0x1B;
        payload[4] = fourCc switch { "DXT1" => (byte)0x0B, "ATI2" => (byte)0x0E, _ => (byte)0x0D };
        payload[6] = (byte)(width & 0xff); payload[7] = (byte)(width >> 8);
        payload[8] = (byte)(height & 0xff); payload[9] = (byte)(height >> 8);
        payload[10] = 1;
        ddsPixels.CopyTo(payload.AsSpan(64));

        for (int i = 0; i < 8; i++)
        {
            int tail = 0, w = Math.Max(1, width >> i), h = Math.Max(1, height >> i);
            for (int level = i; level < mipCount; level++)
            {
                tail += Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * bpb;
                w = Math.Max(1, w / 2); h = Math.Max(1, h / 2);
            }
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(32 + i * 4), (uint)(64 + tail));
        }
        return payload;
    }

    static bool TryFallbackHashName(string name, out string exact)
    {
        exact = "";
        if (!name.StartsWith("hash_", StringComparison.OrdinalIgnoreCase) || name.Length != 22 || name[13] != '_') return false;
        string a = name.Substring(5, 8), b = name.Substring(14, 8);
        if (!uint.TryParse(a, System.Globalization.NumberStyles.HexNumber, null, out _) ||
            !uint.TryParse(b, System.Globalization.NumberStyles.HexNumber, null, out _)) return false;
        exact = $"{a.ToLowerInvariant()}:{b.ToLowerInvariant()}";
        return true;
    }

    /// <summary>Decoded size of an entry, or -1 when the entry uses a command mode the reader cannot decode.</summary>
    static int OriginalSize(IPakReader src, IPakEntry e) { try { return src.Extract(e).Length; } catch { return -1; } }

    static Dictionary<(uint, uint), string> ParseMetadata(byte[] section)
    {
        var map = new Dictionary<(uint, uint), string>();
        for (int o = 0; o + 16 <= section.Length;)
        {
            uint nameHash = B(section, o), dataHash = B(section, o + 4); int len = (int)B(section, o + 8);
            if (len < 0 || o + 16 + len > section.Length) break;
            map[(nameHash, dataHash)] = Encoding.ASCII.GetString(section, o + 16, len);
            o += 16 + len;
        }
        return map;
    }

    /// <summary>Rewrites section 3 in its original order, carrying edited text through.</summary>
    static byte[] BuildMetadata(byte[] original, Dictionary<(uint, uint), string> edited)
    {
        var output = new MemoryStream();
        Span<byte> head = stackalloc byte[16];   // hoisted: stackalloc inside the loop grows the frame per iteration
        for (int o = 0; o + 16 <= original.Length;)
        {
            uint nameHash = B(original, o), dataHash = B(original, o + 4); int len = (int)B(original, o + 8);
            if (len < 0 || o + 16 + len > original.Length) break;
            byte[] text = Encoding.ASCII.GetBytes(edited.GetValueOrDefault((nameHash, dataHash),
                                                  Encoding.ASCII.GetString(original, o + 16, len)));
            BinaryPrimitives.WriteUInt32BigEndian(head, nameHash);
            BinaryPrimitives.WriteUInt32BigEndian(head[4..], dataHash);
            BinaryPrimitives.WriteUInt32BigEndian(head[8..], (uint)text.Length);
            BinaryPrimitives.WriteUInt32BigEndian(head[12..], 0);
            output.Write(head); output.Write(text);
            o += 16 + len;
        }
        return output.ToArray();
    }

    static byte[] BuildMetadataForEntries(IEnumerable<IPakEntry> entries,
                                          Dictionary<(uint, uint), string> metadata)
    {
        var output = new MemoryStream();
        Span<byte> head = stackalloc byte[16];   // hoisted: stackalloc inside the loop grows the frame per iteration
        foreach (var e in entries.OrderBy(e => e.NameHash).ThenBy(e => e.DataHash))
        {
            byte[] text = Encoding.ASCII.GetBytes(metadata.GetValueOrDefault((e.NameHash, e.DataHash), ""));
            BinaryPrimitives.WriteUInt32BigEndian(head, e.NameHash);
            BinaryPrimitives.WriteUInt32BigEndian(head[4..], e.DataHash);
            BinaryPrimitives.WriteUInt32BigEndian(head[8..], (uint)text.Length);
            BinaryPrimitives.WriteUInt32BigEndian(head[12..], 0);
            output.Write(head); output.Write(text);
        }
        return output.ToArray();
    }

    static string? ResizeMetadata(string? text, int size) =>
        text is null ? null : string.Join('\n', text.Split('\n').Select(l => l.StartsWith("size: ") ? $"size: {size}" : l));

    static uint B(byte[] b, int o) => BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o, 4));
    static uint L(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o, 4));
    static void WriteUInt32(Span<byte> b, uint value, bool bigEndian)
    {
        if (bigEndian) BinaryPrimitives.WriteUInt32BigEndian(b, value);
        else BinaryPrimitives.WriteUInt32LittleEndian(b, value);
    }
}

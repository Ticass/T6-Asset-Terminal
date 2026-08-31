using System.Buffers.Binary;
using System.Text;

namespace T6AssetTool.Core;

/// <summary>One image payload destined for the data section, keyed the way the .ff references it.</summary>
/// <param name="PreEncoded">
/// True when <paramref name="Data"/> is already a chain of IPAK blocks copied out of another
/// archive, so it is written through untouched. False when it is a bare payload to be blocked here.
/// </param>
public sealed record IPakInput(uint NameHash, uint DataHash, byte[] Data, string? Metadata = null, bool PreEncoded = false);

/// <summary>
/// Writes Xbox 360 BO2 IPAK archives. Layout measured from retail dlc*_load_zm.ipak:
///   header 16B, one 16B descriptor per section, entries and sections both 0x8000-aligned.
///   Gaps are filled with 0x93 inside the data section and 0xA7 elsewhere -- both decode as an
///   out-of-range command count, so a reader walking past the last block stops instead of running on.
///   sections emitted data(2), index(1), pairs(4), metadata(3).
/// Payloads are stored as mode-0 (uncompressed) commands, which retail also uses for these
/// packs. LZO (mode 1) is read-only in this codebase, so nothing here emits it.
/// </summary>
public static class IPakWriter
{
    public const int SectionAlign = 0x8000, BlockAlign = 0x80, BlockHeader = 0x80;
    public const int MaxBlock = 0x40000;                      // header + payload per block
    public const int MaxCommand = 0x7FF0;                     // payload bytes per command
    const byte Filler = 0xA7;        // between sections
    const byte DataFiller = 0x93;    // between entries inside the data section

    public static void Write(string path, IReadOnlyList<IPakInput> entries)
    {
        using var fs = File.Create(path);
        Write(fs, entries);
    }

    public static void Write(Stream output, IReadOnlyList<IPakInput> entries)
    {
        // --- data section: entries in the order given, each starting on a 0x8000 boundary ---
        var data = new MemoryStream();
        var placed = new List<(IPakInput Entry, uint Offset, uint StoredSize)>(entries.Count);
        foreach (var e in entries)
        {
            PadTo(data, Align((uint)data.Position, SectionAlign), DataFiller);
            uint start = (uint)data.Position;
            if (e.PreEncoded) data.Write(e.Data); else WriteBlocks(data, e.Data);
            placed.Add((e, start, (uint)(data.Position - start)));
        }
        byte[] dataBytes = data.ToArray();

        // index / pair / metadata sections are all ordered by name hash, as retail writes them
        var ordered = placed.OrderBy(p => p.Entry.NameHash).ThenBy(p => p.Entry.DataHash).ToList();

        var index = new MemoryStream();
        foreach (var p in ordered) { W(index, p.Entry.NameHash); W(index, p.Entry.DataHash); W(index, p.Offset); W(index, p.StoredSize); }

        var pairs = new MemoryStream();
        foreach (var p in ordered) { W(pairs, p.Entry.NameHash); W(pairs, p.Entry.DataHash); }

        var meta = new MemoryStream();
        foreach (var p in ordered)
        {
            byte[] text = Encoding.ASCII.GetBytes(p.Entry.Metadata ?? "");
            W(meta, p.Entry.NameHash); W(meta, p.Entry.DataHash); W(meta, (uint)text.Length); W(meta, 0);
            meta.Write(text);
        }

        int count = entries.Count;
        WriteSections(output, new()
        {
            (2, dataBytes,        (uint)count),
            (1, index.ToArray(),  (uint)count),
            (4, pairs.ToArray(),  (uint)count),
            (3, meta.ToArray(),   (uint)count),
        });
    }

    /// <summary>
    /// Writes an archive whose sections are already laid out, keeping each one at the offset it
    /// came from. Retail packs use file-specific offsets (0x8000-aligned in dlc5, 0x40000-aligned
    /// in common_zm), and section 4's count field is not always the entry count, so an untouched
    /// pack only round-trips to the same bytes if all of that is carried rather than recomputed.
    /// Returns false when a body no longer fits its slot; the caller should then relayout.
    /// </summary>
    public static bool TryWriteAt(string path, IReadOnlyList<(uint Type, uint Offset, byte[] Body, uint Count)> sections, uint totalSize)
    {
        var sorted = sections.OrderBy(s => s.Offset).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            uint limit = i + 1 < sorted.Count ? sorted[i + 1].Offset : totalSize;
            if (sorted[i].Offset + sorted[i].Body.Length > limit) return false;
        }

        using var fs = File.Create(path);
        fs.Write("IPAK"u8);
        W(fs, 0x00050000); W(fs, totalSize); W(fs, (uint)sections.Count);
        foreach (var s in sections) { W(fs, s.Type); W(fs, s.Offset); W(fs, (uint)s.Body.Length); W(fs, s.Count); }
        foreach (var s in sorted) { PadTo(fs, s.Offset, Filler); fs.Write(s.Body); }
        PadTo(fs, totalSize, Filler);
        return true;
    }

    /// <summary>Writes the same sections with a freshly computed 0x8000-aligned layout.</summary>
    public static void WriteRelaid(string path, IReadOnlyList<(uint Type, uint Offset, byte[] Body, uint Count)> sections)
    {
        using var fs = File.Create(path);
        WriteSections(fs, sections.Select(s => (s.Type, s.Body, s.Count)).ToList());
    }

    /// <summary>Encodes a bare payload into the block chain an IPAK data section stores.</summary>
    public static byte[] EncodeBlocks(byte[] payload)
    {
        var s = new MemoryStream();
        WriteBlocks(s, payload);
        return s.ToArray();
    }

    // --- header + section table, then each section on its own 0x8000 boundary ---
    static void WriteSections(Stream output, List<(uint Type, byte[] Body, uint Count)> sections)
    {
        uint cursor = Align((uint)(16 + sections.Count * 16), SectionAlign);
        var offsets = new uint[sections.Count];
        for (int i = 0; i < sections.Count; i++)
        {
            offsets[i] = cursor;
            cursor = Align(cursor + (uint)sections[i].Body.Length, SectionAlign);
        }

        var head = new MemoryStream();
        head.Write("IPAK"u8);
        W(head, 0x00050000); W(head, cursor); W(head, (uint)sections.Count);
        for (int i = 0; i < sections.Count; i++)
        {
            W(head, sections[i].Type); W(head, offsets[i]);
            W(head, (uint)sections[i].Body.Length); W(head, sections[i].Count);
        }
        head.Position = 0; head.CopyTo(output);

        for (int i = 0; i < sections.Count; i++)
        {
            PadTo(output, offsets[i], Filler);
            output.Write(sections[i].Body);
        }
        PadTo(output, cursor, Filler);
    }

    /// <summary>The metadata blob retail stores in section 3 for a streamed image part.</summary>
    public static string Metadata(string imageName, string format, int size, int width, int height, int levels, int mip = 0) =>
        $"iwi: images/{imageName}.iwi\nformat: {format}\noffset: 0\nsize: {size}\n" +
        $"width: {width}\nheight: {height}\nlevels: {levels}\nmip: {mip}\nmanual: 1";

    static void WriteBlocks(Stream s, byte[] payload)
    {
        int consumed = 0;
        do
        {
            int take = Math.Min(payload.Length - consumed, MaxBlock - BlockHeader);
            int commands = Math.Max(1, (take + MaxCommand - 1) / MaxCommand);
            if (commands > 31) throw new InvalidDataException($"Block would need {commands} commands");

            long headerAt = s.Position;
            W(s, (uint)(commands << 24 | consumed));           // 24-bit running offset inside this image
            for (int i = 0, left = take; i < commands; i++)
            {
                int size = Math.Min(left, MaxCommand);
                W(s, (uint)size);                              // mode 0 = stored uncompressed
                left -= size;
            }
            PadTo(s, headerAt + BlockHeader, 0);
            s.Write(payload, consumed, take);
            consumed += take;
            PadTo(s, Align((uint)s.Position, BlockAlign), 0);
        } while (consumed < payload.Length);
    }

    static uint Align(uint v, int a) => (uint)((v + a - 1) & ~(a - 1));
    static void PadTo(Stream s, long target, byte fill) { while (s.Position < target) s.WriteByte(fill); }
    static void W(Stream s, uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); s.Write(b); }
}

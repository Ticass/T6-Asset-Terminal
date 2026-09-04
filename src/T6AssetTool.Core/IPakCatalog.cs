using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace T6AssetTool.Core;

/// <summary>
/// Builds an image catalog from an IPAK's own metadata section, so any package can be
/// extracted without a matching fastfile or a hand-built catalog shipped in this assembly.
///
/// Retail stores section 3 as a run of records -- nameHash, dataHash, textLength, 0, then
/// ASCII text -- one per streamed mip part:
///
///     iwi: images/~-gshells_green_c.iwi
///     format: DXT1
///     offset: 40960
///     size: 20480
///     width: 64
///     height: 64
///     levels: 7
///     mip: 2
///     manual: 0
///
/// That is everything ZoneImageScanner recovers from a zone, which is why the zone is not
/// needed. The section describes more parts than the package stores -- zm_transit_tm.ipak
/// has 5372 metadata records against 803 index entries -- so parts are matched against the
/// index by (nameHash, dataHash) and the rest are ignored.
///
/// MEASURED across all 183 IPAKs of a retail Xbox 360 install, "format:" is only ever
/// DXT5 (158609), DXN (114409), DXT1 (98227), DXT3 (6688), or one of four uncompressed
/// forms totalling 818 records. Only the block-compressed four can be written as BC DDS;
/// an image in any other format is reported and skipped rather than written incorrectly.
///
/// Packages with no metadata section fall back to hash-named DDS output. Those
/// packages do not carry original image names, and DXT5/DXN are indistinguishable
/// from block size alone, but the extractor can still infer dimensions/mips from
/// the IPAK payload and write editable DDS files without a zone.
/// </summary>
public static class IPakCatalog
{
    const uint MetadataSection = 3;

    /// <summary>Xenos texture format codes, the values DdsWriter.FourCc keys on.</summary>
    static int GpuFormat(string format) => format.ToUpperInvariant() switch
    {
        "DXT1" => 0x12,
        "DXT2" or "DXT3" => 0x13,
        "DXT4" or "DXT5" => 0x14,
        "DXN" => 0x1A,
        _ => 0,
    };

    public static IReadOnlyList<ZoneImage> FromPackage(IPakReader ipak, Action<string>? log = null)
    {
        byte[] blob = ipak.SectionBytes(MetadataSection);
        if (blob.Length == 0) return [];

        var present = ipak.Entries.Select(e => (e.NameHash, e.DataHash)).ToHashSet();
        var byImage = new Dictionary<uint, List<(uint DataHash, string Name, string Format, int Size, int Width, int Height, int Levels, int Mip)>>();
        int records = 0, skipped = 0;

        for (int o = 0; o + 16 <= blob.Length;)
        {
            uint nameHash = U32(blob, o), dataHash = U32(blob, o + 4);
            int len = (int)U32(blob, o + 8);
            if (len < 0 || o + 16 + len > blob.Length) break;
            records++;
            if (!present.Contains((nameHash, dataHash))) { o += 16 + len; continue; }

            var f = Parse(Encoding.ASCII.GetString(blob, o + 16, len));
            o += 16 + len;
            if (f is null) { skipped++; continue; }
            var (name, format, size, width, height, levels, mip) = f.Value;
            if (width <= 0 || height <= 0) { skipped++; continue; }
            if (!byImage.TryGetValue(nameHash, out var list)) byImage[nameHash] = list = [];
            list.Add((dataHash, name, format, size, width, height, Math.Max(1, levels), mip));
        }

        var images = new List<ZoneImage>();
        int unsupported = 0;
        foreach (var (nameHash, list) in byImage)
        {
            int gpu = GpuFormat(list[0].Format);
            if (gpu == 0)
            {
                unsupported++;
                log?.Invoke($"SKIP  {list[0].Name}: {list[0].Format} is not a block-compressed format");
                continue;
            }
            // The extractor derives each bundle's mip count as (this part's level count minus
            // the next smaller part's), so the counts it wants are cumulative from the
            // smallest part upward, not the per-part counts the metadata stores.
            var ordered = list.OrderBy(p => (long)p.Width * p.Height).ToList();
            var parts = new List<StreamedImagePart>(ordered.Count);
            int running = 0;
            foreach (var p in ordered)
            {
                running += p.Levels;
                parts.Add(new StreamedImagePart(running, p.Size, p.DataHash, p.Width, p.Height, 0, p.Size, 0, true));
            }
            var largest = ordered[^1];
            images.Add(new ZoneImage(largest.Name, nameHash, 0, 0, largest.Width, largest.Height, 1,
                                     running, largest.Size, false, parts, 0, gpu));
        }

        log?.Invoke($"CAT   {images.Count} images from {records} metadata records"
                    + (unsupported > 0 ? $"  |  {unsupported} unsupported format" : "")
                    + (skipped > 0 ? $"  |  {skipped} unreadable" : ""));
        return images;
    }

    public static IReadOnlyList<ZoneImage> FromIndexOnly(IPakReader ipak, Action<string>? log = null)
    {
        var images = new List<ZoneImage>();
        int failed = 0, ambiguous = 0;

        foreach (var entry in ipak.Entries.OrderBy(e => e.NameHash).ThenBy(e => e.DataHash))
        {
            byte[] payload;
            try { payload = ipak.Extract(entry); }
            catch (Exception e)
            {
                failed++;
                log?.Invoke($"SKIP  {entry.NameHash:x8}:{entry.DataHash:x8}: {e.Message}");
                continue;
            }

            var inferred = Infer(payload.Length);
            if (inferred is null)
            {
                failed++;
                log?.Invoke($"SKIP  {entry.NameHash:x8}:{entry.DataHash:x8}: cannot infer DDS shape from {payload.Length} bytes");
                continue;
            }

            if (inferred.Value.Ambiguous) ambiguous++;
            string name = $"hash_{entry.NameHash:x8}_{entry.DataHash:x8}";
            var part = new StreamedImagePart(inferred.Value.Levels, payload.Length, entry.DataHash,
                                             inferred.Value.Width, inferred.Value.Height, 0,
                                             payload.Length, 0, true);
            images.Add(new ZoneImage(name, entry.NameHash, 0, 0, inferred.Value.Width, inferred.Value.Height,
                                     1, inferred.Value.Levels, payload.Length, false, [part], 0,
                                     inferred.Value.GpuFormat));
        }

        log?.Invoke($"CAT   {images.Count} hash-named images inferred from IPAK index only"
                    + (ambiguous > 0 ? $"  |  {ambiguous} DXT5/DXN ambiguous, wrote DXT5" : "")
                    + (failed > 0 ? $"  |  {failed} skipped" : ""));
        return images;
    }

    static (int Width, int Height, int Levels, int GpuFormat, bool Ambiguous)? Infer(int payloadLength)
    {
        if (payloadLength <= 64) return null;

        var candidates = new List<(int Width, int Height, int Levels, int GpuFormat, bool Ambiguous, int Score)>();
        int[] dims = [1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096];
        foreach (int bpb in new[] { 16, 8 })
        foreach (int width in dims)
        foreach (int height in dims)
        {
            int maxLevels = 1 + (int)Math.Log2(Math.Max(width, height));
            int tightTotal = 64;
            int pageTotal = 0;
            for (int levels = 1; levels <= maxLevels; levels++)
            {
                int lw = Math.Max(1, width >> (levels - 1));
                int lh = Math.Max(1, height >> (levels - 1));
                int linear = Math.Max(1, (lw + 3) / 4) * Math.Max(1, (lh + 3) / 4) * bpb;
                tightTotal += linear;
                pageTotal += (linear + 0xfff) & ~0xfff;
                if (tightTotal == payloadLength || pageTotal == payloadLength)
                {
                    int aspect = Math.Abs((int)Math.Round(Math.Log2((double)width / height) * 100.0));
                    int area = width * height;
                    int formatPenalty = bpb == 16 ? 0 : 50;
                    int mipPenalty = levels == maxLevels ? 0 : 25;
                    int score = aspect + formatPenalty + mipPenalty - Math.Min(area, 1 << 20) / 4096;
                    candidates.Add((width, height, levels, bpb == 8 ? 0x12 : 0x14, bpb == 16, score));
                }
                if (tightTotal > payloadLength && pageTotal > payloadLength) break;
            }
        }

        if (candidates.Count == 0) return null;
        var best = candidates.OrderBy(c => c.Score).ThenByDescending(c => c.Width * c.Height).First();
        return (best.Width, best.Height, best.Levels, best.GpuFormat, best.Ambiguous);
    }

    static (string Name, string Format, int Size, int Width, int Height, int Levels, int Mip)?
        Parse(string text)
    {
        string? name = null, format = null;
        int size = 0, width = 0, height = 0, levels = 1, mip = 0;
        foreach (string line in text.Split('\n'))
        {
            int colon = line.IndexOf(':');
            if (colon < 0) continue;
            string key = line[..colon].Trim(), value = line[(colon + 1)..].Trim();
            switch (key)
            {
                case "iwi": name = ImageName(value); break;
                case "format": format = value; break;
                case "size": size = Int(value); break;
                case "width": width = Int(value); break;
                case "height": height = Int(value); break;
                case "levels": levels = Int(value); break;
                case "mip": mip = Int(value); break;
            }
        }
        return name is null || format is null ? null : (name, format, size, width, height, levels, mip);
    }

    /// <summary>"images/~-gshells_green_c.iwi" -> "~-gshells_green_c".</summary>
    static string ImageName(string iwi)
    {
        string s = iwi.Replace('\\', '/');
        int slash = s.LastIndexOf('/');
        if (slash >= 0) s = s[(slash + 1)..];
        return s.EndsWith(".iwi", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;
    }

    static int Int(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
    static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o, 4));
}

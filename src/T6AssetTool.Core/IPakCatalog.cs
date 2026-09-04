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
        int failed = 0;

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

            var iwi = ParseIwi(payload);
            if (iwi is null)
            {
                failed++;
                log?.Invoke($"SKIP  {entry.NameHash:x8}:{entry.DataHash:x8}: decoded payload is not a supported IWI texture");
                continue;
            }

            string name = $"hash_{entry.NameHash:x8}_{entry.DataHash:x8}";
            var part = new StreamedImagePart(iwi.Value.Levels, payload.Length, entry.DataHash,
                                             iwi.Value.Width, iwi.Value.Height, 0,
                                             payload.Length, 0, true);
            images.Add(new ZoneImage(name, entry.NameHash, 0, 0, iwi.Value.Width, iwi.Value.Height,
                                     1, iwi.Value.Levels, payload.Length, false, [part], 0,
                                     iwi.Value.GpuFormat));
        }

        log?.Invoke($"CAT   {images.Count} hash-named images inferred from IPAK index only"
                    + (failed > 0 ? $"  |  {failed} skipped" : ""));
        return images;
    }

    static (int Width, int Height, int Levels, int GpuFormat)? ParseIwi(byte[] payload)
    {
        if (payload.Length <= 64 || payload[0] != 0x49 || payload[1] != 0x57 || payload[2] != 0x69) return null;
        int gpuFormat = payload[4] switch { 0x0B => 0x12, 0x0C => 0x13, 0x0D => 0x14, 0x0E => 0x1A, _ => 0 };
        if (gpuFormat == 0) return null;
        int width = payload[6] | (payload[7] << 8), height = payload[8] | (payload[9] << 8);
        if (width <= 0 || height <= 0) return null;
        return (width, height, Math.Max(1, 1 + (int)Math.Log2(Math.Max(width, height))), gpuFormat);
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

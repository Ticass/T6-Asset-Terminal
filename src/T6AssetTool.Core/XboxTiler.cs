namespace T6AssetTool.Core;

/// <summary>
/// The inverse of <see cref="DdsWriter"/>'s untile step: takes linear BC blocks and produces the
/// tiled, byte-swapped layout an Xbox 360 IPAK payload stores. Mip levels are padded to 0x1000,
/// matching the page stride the extractor walks when it reads them back.
/// </summary>
public static class XboxTiler
{
    public static byte[] Tile(ReadOnlySpan<byte> linear, int width, int height, int mipCount, int bytesPerBlock)
    {
        var output = new MemoryStream();
        int w = width, h = height, src = 0;
        for (int level = 0; level < Math.Max(1, mipCount); level++)
        {
            int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4), size = bw * bh * bytesPerBlock;
            if (src + size > linear.Length) break;
            output.Write(TileLevel(linear.Slice(src, size), bw, bh, bytesPerBlock));
            src += size;
            w = Math.Max(1, w / 2); h = Math.Max(1, h / 2);
        }
        return output.ToArray();
    }

    public static byte[] TileLevel(ReadOnlySpan<byte> linear, int widthBlocks, int heightBlocks, int bytesPerBlock)
    {
        int log = bytesPerBlock == 8 ? 3 : 4;
        int linearSize = widthBlocks * heightBlocks * bytesPerBlock;
        int tiledSize = Math.Max(linearSize, Extent(widthBlocks, heightBlocks, log, bytesPerBlock));
        byte[] dst = new byte[(tiledSize + 0xFFF) & ~0xFFF];
        for (int y = 0; y < heightBlocks; y++)
            for (int x = 0; x < widthBlocks; x++)
            {
                int to = XgAddress2D(x, y, widthBlocks, log), si = (y * widthBlocks + x) * bytesPerBlock;
                if (to + bytesPerBlock > dst.Length || si + bytesPerBlock > linear.Length) continue;
                for (int j = 0; j < bytesPerBlock; j += 2) { dst[to + j] = linear[si + j + 1]; dst[to + j + 1] = linear[si + j]; }
            }
        return dst;
    }

    static int Extent(int widthBlocks, int heightBlocks, int log, int bytesPerBlock)
    {
        int max = 0;
        for (int y = 0; y < heightBlocks; y++)
            for (int x = 0; x < widthBlocks; x++)
                max = Math.Max(max, XgAddress2D(x, y, widthBlocks, log) + bytesPerBlock);
        return max;
    }

    // Kept byte-identical in behaviour to DdsWriter.XgAddress2D so tile/untile are exact inverses.
    static int XgAddress2D(int x, int y, int width, int log)
    {
        int aligned = (width + 31) & ~31;
        int macro = ((x >> 5) + (y >> 5) * (aligned >> 5)) << (log + 7);
        int micro = ((x & 7) + ((y & 6) << 2)) << log;
        int offset = macro + ((micro & ~15) << 1) + (micro & 15) + ((y & 8) << (3 + log)) + ((y & 1) << 4);
        return ((offset & ~511) << 3) + ((offset & 448) << 2) + (offset & 63) + ((y & 16) << 7) + ((((((y & 8) >> 2) + (x >> 3)) & 3)) << 6);
    }
}

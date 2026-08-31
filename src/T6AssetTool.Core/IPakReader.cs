using System.Buffers.Binary;
using lzo.net;
using System.IO.Compression;

namespace T6AssetTool.Core;

public sealed record IPakEntry(uint NameHash, uint DataHash, uint Offset, uint StoredSize);
public sealed record IPakSection(uint Type, uint Offset, uint Size, uint Count);

public sealed class IPakReader : IDisposable
{
    const int Chunk=0x8000, BlockHeader=0x80;
    readonly FileStream stream;
    readonly bool big;
    readonly uint dataOffset;
    public IReadOnlyList<IPakEntry> Entries { get; }
    public IReadOnlyList<IPakSection> Sections { get; }
    public bool BigEndian=>big;
    public uint TotalSize { get; }

    public IPakReader(string path)
    {
        stream=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        Span<byte> h=stackalloc byte[16]; ReadExact(h);
        big=h[..4].SequenceEqual("IPAK"u8); if(!big && !h[..4].SequenceEqual("KAPI"u8)) throw new InvalidDataException("Not an IPAK file");
        if(U32(h[4..])!=0x50000) throw new InvalidDataException("Unsupported IPAK version");TotalSize=U32(h[8..]);
        int sectionCount=checked((int)U32(h[12..])); uint indexOffset=0,indexCount=0; dataOffset=0;
        Span<byte> s=stackalloc byte[16];var sections=new List<IPakSection>(sectionCount);
        for(int i=0;i<sectionCount;i++){ReadExact(s);uint type=U32(s),off=U32(s[4..]),size=U32(s[8..]),count=U32(s[12..]);sections.Add(new(type,off,size,count));if(type==1){indexOffset=off;indexCount=count;}else if(type==2)dataOffset=off;}
        Sections=sections;
        if(indexOffset==0||dataOffset==0)throw new InvalidDataException("Missing IPAK data/index section");
        stream.Position=indexOffset; var entries=new List<IPakEntry>(checked((int)indexCount));
        for(int i=0;i<indexCount;i++){ReadExact(s);entries.Add(new(U32(s),U32(s[4..]),U32(s[8..]),U32(s[12..])));}
        Entries=entries;
    }

    public byte[] Extract(IPakEntry entry)
    {
        long pos=dataOffset+entry.Offset,end=pos+entry.StoredSize; using var output=new MemoryStream(); int expectedOffset=0;
        while(pos<end)
        {
            pos=(pos+0x7f)&~0x7fL; if(pos+BlockHeader>end)break; stream.Position=pos;
            byte[] header=new byte[BlockHeader];ReadExact(header);uint first=U32(header);int fileOffset=(int)(first&0xffffff),count=(int)(first>>24);
            if(count>31)throw new InvalidDataException($"Invalid command count {count}");
            var commands=new (int size,int mode)[count]; long payload=pos+BlockHeader;
            for(int i=0;i<count;i++){uint c=U32(header.AsSpan(4+i*4));commands[i]=((int)(c&0xffffff),(int)(c>>24));}
            bool hasData=commands.Any(c=>c.mode is 0 or 1 or 2); if(hasData && fileOffset!=expectedOffset)throw new InvalidDataException($"Discontinuous IPAK image at 0x{pos:X}");
            stream.Position=payload;
            foreach(var c in commands)
            {
                byte[] input=new byte[c.size];ReadExact(input);pos+=c.size;
                byte[]? decoded=c.mode switch{0=>input,1=>DecompressLzo(input),_=>null};
                if(decoded!=null){output.Write(decoded);expectedOffset+=decoded.Length;}
            }
            pos=payload+commands.Sum(c=>(long)c.size);
        }
        return output.ToArray();
    }
    /// <summary>The entry's stored block bytes exactly as they sit in the file, codec and all.</summary>
    public byte[] ReadStored(IPakEntry entry){byte[] b=new byte[entry.StoredSize];stream.Position=dataOffset+entry.Offset;ReadExact(b);return b;}

    /// <summary>Raw bytes of a whole section, used by the repacker to carry sections it does not rewrite.</summary>
    public byte[] SectionBytes(uint type){var s=Sections.FirstOrDefault(x=>x.Type==type);if(s is null)return[];byte[] b=new byte[s.Size];stream.Position=s.Offset;ReadExact(b);return b;}

    uint U32(ReadOnlySpan<byte> b)=>big?BinaryPrimitives.ReadUInt32BigEndian(b):BinaryPrimitives.ReadUInt32LittleEndian(b);
    static byte[] DecompressLzo(byte[] input){using var source=new MemoryStream(input);using var lzo=new LzoStream(source,CompressionMode.Decompress);using var output=new MemoryStream();lzo.CopyTo(output);return output.ToArray();}
    void ReadExact(Span<byte> b){stream.ReadExactly(b);} void ReadExact(byte[] b){stream.ReadExactly(b);}
    public void Dispose()=>stream.Dispose();
}

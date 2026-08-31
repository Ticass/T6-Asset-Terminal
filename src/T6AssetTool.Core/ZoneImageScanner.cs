using System.Buffers.Binary;
using System.Text;

namespace T6AssetTool.Core;

public sealed record StreamedImagePart(int LevelCount,int LevelSize,uint DataHash,int Width,int Height,uint Offset,int StoredSize,int IPakIndex,bool Valid);
// GpuFormat is the Xenos texture format at +0x4F (0x12 DXT1, 0x13 DXT2/3, 0x14 DXT4/5, 0x1A/0x1B DXN,
// 0x06 8_8_8_8), verified against the "format:" line every IPAK stores for the same image. Format at
// +0x29 does not distinguish DXT1 from DXT5 -- it reads 13 for both -- so GpuFormat wins where set.
public sealed record ZoneImage(string Name,uint NameHash,int Format,int Semantic,int Width,int Height,int Depth,int LevelCount,int BaseSize,bool Inline,IReadOnlyList<StreamedImagePart> Parts,long ZoneOffset,int GpuFormat=0);

public static class ZoneImageScanner
{
    const int RootSize=0xD4;
    public static IReadOnlyList<ZoneImage> Scan(string zonePath,ISet<uint>? wantedHashes=null)
    {
        byte[] z=File.ReadAllBytes(zonePath);var result=new List<ZoneImage>();
        for(int marker=0;marker<=z.Length-4;marker++)
        {
            if(z[marker]!=0xff||z[marker+1]!=0xff||z[marker+2]!=0xff||z[marker+3]!=0xff)continue;
            int o=marker-0xCC;if(o<0||o+RootSize>=z.Length)continue;
            uint nameHash=U32(z,o+0xD0);if(wantedHashes!=null&&!wantedHashes.Contains(nameHash))continue;
            int end=Array.IndexOf(z,(byte)0,o+RootSize,Math.Min(240,z.Length-o-RootSize));if(end<0)continue;
            int len=end-(o+RootSize);if(len is <4 or >180)continue;bool printable=true;for(int i=o+RootSize;i<end;i++)if(z[i]<32||z[i]>126){printable=false;break;}if(!printable)continue;
            string name=Encoding.ASCII.GetString(z,o+RootSize,len);if(HashName(name)!=nameHash)continue;
            int format=z[o+0x29],gpuFormat=z[o+0x4F],semantic=z[o+0x35],baseSize=checked((int)U32(z,o+0x38));int w=U16(z,o+0x3C),h=U16(z,o+0x3E),d=U16(z,o+0x40),levels=z[o+0x42];uint pixels=U32(z,o+0x48);
            var parts=new List<StreamedImagePart>();int partCount=Math.Min(z[o+0xC8],(byte)5);
            for(int i=0;i<partCount;i++)
            {
                int p=o+0x50+i*24;uint ls=U32(z,p);int lc=(int)(ls&15),levelSize=(int)(ls>>4);uint dh=U32(z,p+4);int pw=U16(z,p+8),ph=U16(z,p+10);uint po=U32(z,p+12),si=U32(z,p+16),adj=U32(z,p+20);
                int stored=(int)(si&0x0fffffff),ipak=(int)(si>>28);bool valid=(adj&1)!=0||dh!=0;
                if(dh!=0&&pw>0&&ph>0)parts.Add(new(lc,levelSize,dh,pw,ph,po,stored,ipak,valid));
            }
            bool plausible=parts.Count>0||(pixels==0xffffffff&&w is >0 and <=8192&&h is >0 and <=8192&&levels is >0 and <16&&baseSize>0);if(!plausible)continue;
            result.Add(new(name,nameHash,format,semantic,w,h,d,levels,baseSize,pixels==0xffffffff,parts,o,gpuFormat));marker=end;
        }
        return result.GroupBy(x=>(x.NameHash,x.ZoneOffset)).Select(g=>g.First()).ToList();
    }
    public static uint HashName(string name){uint h=0;foreach(char ch in name)h=unchecked(33*h^((byte)ch|0x20u));return h;}
    public static byte[] ReadInlinePayload(string zonePath,ZoneImage image){if(!image.Inline||image.BaseSize<=0)return[];using var f=File.OpenRead(zonePath);long start=image.ZoneOffset+RootSize+Encoding.ASCII.GetByteCount(image.Name)+1;f.Position=start;byte[] data=new byte[image.BaseSize];f.ReadExactly(data);return data;}
    static ushort U16(byte[] b,int o)=>BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(o,2));static uint U32(byte[] b,int o)=>BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o,4));
}

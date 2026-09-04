using System.Buffers.Binary;

namespace T6AssetTool.Core;

public static class DdsWriter
{
    public static void WriteBc(string path,int width,int height,int mipCount,int formatCode,IReadOnlyList<(byte[] Data,int Width,int Height,int Levels)> bundles,int gpuFormat=0)
    {
        if(bundles.Count==1&&IsIwi(bundles[0].Data)){WriteIwi(path,bundles[0].Data);return;}
        string fourCc=FourCc(formatCode,gpuFormat);int bpb=fourCc=="DXT1"?8:16;using var fs=File.Create(path);Span<byte> h=stackalloc byte[128];h.Clear();"DDS "u8.CopyTo(h);W(h,4,124);W(h,8,0x000A1007);W(h,12,(uint)height);W(h,16,(uint)width);W(h,20,(uint)(Math.Max(1,(width+3)/4)*bpb));W(h,28,(uint)mipCount);W(h,76,32);W(h,80,4);Encoding(fourCc).CopyTo(h[84..]);W(h,108,mipCount>1?0x401008u:0x1000u);fs.Write(h);
        foreach(var bundle in bundles)
        {
            int w=bundle.Width,hgt=bundle.Height;int levels=Math.Max(1,bundle.Levels);
            int expectedLinear=0,expectedPaged=0,tw=w,th=hgt;
            for(int level=0;level<levels;level++)
            {
                int linear=Math.Max(1,(tw+3)/4)*Math.Max(1,(th+3)/4)*bpb;
                expectedLinear+=linear;expectedPaged+=(linear+0xfff)&~0xfff;
                tw=Math.Max(1,tw/2);th=Math.Max(1,th/2);
            }
            int src=bundle.Data.Length>=expectedLinear+64?64:0;
            bool tight=bundle.Data.Length-src>=expectedLinear&&bundle.Data.Length-src<expectedPaged;
            for(int level=0;level<levels;level++)
            {
                int bw=Math.Max(1,(w+3)/4),bh=Math.Max(1,(hgt+3)/4),linear=bw*bh*bpb;int page=(linear+0xfff)&~0xfff;
                if(src>=bundle.Data.Length)break;byte[] untiled=Untile(bundle.Data.AsSpan(src,Math.Min(page,bundle.Data.Length-src)),bw,bh,bpb);fs.Write(untiled,0,Math.Min(linear,untiled.Length));src+=page;w=Math.Max(1,w/2);hgt=Math.Max(1,hgt/2);
                if(tight)src-=page-linear;
            }
        }
    }
    static bool IsIwi(byte[] data)=>data.Length>64&&data[0]==0x49&&data[1]==0x57&&data[2]==0x69;
    static void WriteIwi(string path,byte[] data)
    {
        int width=data[6]|(data[7]<<8),height=data[8]|(data[9]<<8),format=data[4];
        string fourCc=format switch{0x0B=>"DXT1",0x0C=>"DXT3",0x0D=>"DXT5",0x0E=>"ATI2",_=>"DXT5"};
        int bpb=fourCc=="DXT1"?8:16,mips=MaxMipCount(width,height);
        using var fs=File.Create(path);Span<byte> h=stackalloc byte[128];h.Clear();"DDS "u8.CopyTo(h);W(h,4,124);W(h,8,0x000A1007);W(h,12,(uint)height);W(h,16,(uint)width);W(h,20,(uint)(Math.Max(1,(width+3)/4)*bpb));W(h,28,(uint)mips);W(h,76,32);W(h,80,4);Encoding(fourCc).CopyTo(h[84..]);W(h,108,mips>1?0x401008u:0x1000u);fs.Write(h);
        fs.Write(data,64,data.Length-64);
    }
    static int MaxMipCount(int width,int height)=>Math.Max(1,1+(int)Math.Log2(Math.Max(width,height)));
    /// <summary>Xenos format at GfxImage+0x4F when known, else the legacy +0x29 code.</summary>
    static string FourCc(int formatCode,int gpuFormat)=>(gpuFormat&0x3F) switch{0x12=>"DXT1",0x13=>"DXT3",0x14=>"DXT5",0x1A or 0x1B=>"ATI2",
        _=>(formatCode&0x0F) switch{0xB=>"DXT1",0xE=>"ATI2",_=>"DXT5"}};

    static byte[] Untile(ReadOnlySpan<byte> input,int widthBlocks,int heightBlocks,int bpb)
    {
        int log=bpb==8?3:4;byte[] dst=new byte[widthBlocks*heightBlocks*bpb];
        for(int y=0;y<heightBlocks;y++)for(int x=0;x<widthBlocks;x++){int so=XgAddress2D(x,y,widthBlocks,log),di=(y*widthBlocks+x)*bpb;if(so+bpb>input.Length)continue;for(int j=0;j<bpb;j+=2){dst[di+j]=input[so+j+1];dst[di+j+1]=input[so+j];}}
        return dst;
    }
    static int XgAddress2D(int x,int y,int width,int log){int aligned=(width+31)&~31;int macro=((x>>5)+(y>>5)*(aligned>>5))<<(log+7);int micro=((x&7)+((y&6)<<2))<<log;int offset=macro+((micro&~15)<<1)+(micro&15)+((y&8)<<(3+log))+((y&1)<<4);offset=((offset&~511)<<3)+((offset&448)<<2)+(offset&63)+((y&16)<<7)+(((((y&8)>>2)+(x>>3))&3)<<6);return offset;}
    static int Tiled2D(int x,int y,int pitch,int log){int outer=(((y>>5)*(pitch>>5)+(x>>5))<<6);int inner=(((y>>1)&7)<<3)|(x&7);int oi=(outer|inner)<<log;uint bank=(uint)((y>>4)&1),pipe=(uint)(((x>>3)&3)^(((y>>3)&1)<<1)),yl=(uint)(y&1);return (int)(((yl<<4)|(pipe<<6)|(bank<<11))|((uint)oi&15)|((((uint)oi>>4)&1)<<5)|((((uint)oi>>5)&7)<<8)|((uint)oi>>8<<12));}
    static void W(Span<byte>b,int o,uint v)=>BinaryPrimitives.WriteUInt32LittleEndian(b[o..],v);static byte[] Encoding(string s)=>System.Text.Encoding.ASCII.GetBytes(s);
}

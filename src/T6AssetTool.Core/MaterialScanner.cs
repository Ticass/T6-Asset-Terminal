using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace T6AssetTool.Core;

public sealed record MaterialTexture(uint SlotHash,byte NameStart,byte NameEnd,byte SamplerState,byte Semantic,bool Mature,uint ImagePointer,string? Image,uint? ImageHash);
public sealed record MaterialConstant(uint NameHash,string Name,float X,float Y,float Z,float W);
public sealed record MaterialState(uint BlendBits,uint DepthStencilBits);
public sealed record ZoneMaterial(string Name,uint GameFlags,byte SortKey,byte AtlasRows,byte AtlasColumns,ulong DrawSurf,uint SurfaceTypeBits,uint LayeredSurfaceTypes,ushort HashIndex,int SurfaceFlags,int Contents,byte TextureCount,byte ConstantCount,byte StateBitsCount,byte StateFlags,byte CameraRegion,byte ProbeMipBits,uint TechniquePointer,uint ThermalPointer,byte[] StateBitsEntry,IReadOnlyList<MaterialTexture> Textures,IReadOnlyList<MaterialConstant> Constants,IReadOnlyList<MaterialState> States,long ZoneOffset);

public static class MaterialScanner
{
    static readonly string[] Semantic={"2d","function","color_map","unused_1","unused_2","normal_map","unused_3","unused_4","specular_map","unused_5","unused_6","water_map","color0_map","color1_map","color2_map","color3_map","color4_map","color5_map","color6_map","color7_map","color8_map","color9_map","color10_map","color11_map","color12_map","color13_map","color14_map","color15_map","throw_map"};
    static readonly string[] Filter={"default","nearest","linear","anisotropic_2x","anisotropic_4x","compare","reserved_6","reserved_7"};
    static readonly string[] Blend={"disabled","zero","one","src_color","inv_src_color","src_alpha","inv_src_alpha","dst_alpha","inv_dst_alpha","dst_color","inv_dst_color","src_alpha_sat","reserved_12","reserved_13","reserved_14","reserved_15"};
    static readonly string[] BlendOp={"disabled","add","subtract","reverse_subtract","min","max","reserved_6","reserved_7"};

    public static IReadOnlyList<ZoneMaterial> Scan(string zonePath,IReadOnlyList<ZoneImage> images)
    {
        byte[] z=File.ReadAllBytes(zonePath);var byOffset=images.ToDictionary(i=>(int)i.ZoneOffset);var result=new List<ZoneMaterial>();
        for(int o=0;o+0x68<z.Length;o++)
        {
            if(U32(z,o)!=0xffffffff)continue;byte tc=z[o+0x4C],cc=z[o+0x4D],sc=z[o+0x4E];if(tc is 0 or >32||cc>32||sc>64||U32(z,o+0x58)!=0xffffffff)continue;
            int end=Array.IndexOf(z,(byte)0,o+0x68,Math.Min(220,z.Length-o-0x68));if(end<0)continue;string name=Ascii(z,o+0x68,end-o-0x68);if(!name.StartsWith("mc/")||name.Any(c=>c<32||c>126))continue;
            int table=end+1;if(table+tc*16>z.Length)continue;var defs=new List<(uint hash,byte start,byte finish,byte sampler,byte semantic,bool mature,uint ptr)>();for(int i=0;i<tc;i++){int d=table+i*16;defs.Add((U32(z,d),z[d+4],z[d+5],z[d+6],z[d+7],z[d+8]!=0,U32(z,d+12)));}
            int cursor=table+tc*16;var textures=new List<MaterialTexture>();
            foreach(var d in defs){ZoneImage? image=null;if(d.ptr==0xffffffff&&byOffset.TryGetValue(cursor,out image)){cursor+=0xD4+Encoding.ASCII.GetByteCount(image.Name)+1;if(image.Inline)cursor+=image.BaseSize;}textures.Add(new(d.hash,d.start,d.finish,d.sampler,d.semantic,d.mature,d.ptr,image?.Name,image?.NameHash));}
            if(cursor+cc*32+sc*8>z.Length)continue;var constants=new List<MaterialConstant>();for(int i=0;i<cc;i++){int c=cursor+i*32;string cn=Ascii(z,c+4,12).TrimEnd('\0');constants.Add(new(U32(z,c),cn,F32(z,c+16),F32(z,c+20),F32(z,c+24),F32(z,c+28)));}cursor+=cc*32;var states=new List<MaterialState>();for(int i=0;i<sc;i++){states.Add(new(U32(z,cursor+i*8),U32(z,cursor+i*8+4)));}
            result.Add(new(name,U32(z,o+4),z[o+9],z[o+10],z[o+11],U64(z,o+12),U32(z,o+20),U32(z,o+24),U16(z,o+28),I32(z,o+32),I32(z,o+36),tc,cc,sc,z[o+0x4F],z[o+0x50],z[o+0x51],U32(z,o+0x54),U32(z,o+0x64),z.AsSpan(o+0x28,36).ToArray(),textures,constants,states,o));o=end;
        }
        return result.GroupBy(m=>m.ZoneOffset).Select(g=>g.First()).ToList();
    }

    public static void Write(string output,ZoneMaterial m,ISet<string>? availableDds=null)
    {
        Directory.CreateDirectory(output);string safe=Safe(m.Name[3..]);var b=new StringBuilder();b.AppendLine($"material \"{m.Name}\"").AppendLine("{");b.AppendLine("  // BO2 Xbox 360 material asset");b.AppendLine($"  game_flags          = 0x{m.GameFlags:X8}");b.AppendLine($"  sort_key            = {m.SortKey}");b.AppendLine($"  atlas               = {m.AtlasColumns} x {m.AtlasRows}");b.AppendLine($"  draw_surface        = 0x{m.DrawSurf:X16}");b.AppendLine($"  surface_type_bits   = 0x{m.SurfaceTypeBits:X8}");b.AppendLine($"  layered_types       = 0x{m.LayeredSurfaceTypes:X8}");b.AppendLine($"  surface_flags       = 0x{m.SurfaceFlags:X8}");b.AppendLine($"  contents            = 0x{m.Contents:X8}");b.AppendLine($"  hash_index          = {m.HashIndex}");b.AppendLine($"  state_flags         = 0x{m.StateFlags:X2}");b.AppendLine($"  camera_region       = {m.CameraRegion}");b.AppendLine($"  probe_mip_bits      = 0x{m.ProbeMipBits:X2}");b.AppendLine($"  technique_set       = alias(0x{m.TechniquePointer:X8})");if(m.ThermalPointer!=0)b.AppendLine($"  thermal_material    = alias(0x{m.ThermalPointer:X8})");
        b.AppendLine().AppendLine("  textures").AppendLine("  {");for(int i=0;i<m.Textures.Count;i++){var t=m.Textures[i];string semantic=t.Semantic<Semantic.Length?Semantic[t.Semantic]:$"unknown_{t.Semantic}";string image=t.Image!=null?$"\"{t.Image}\"":$"alias(0x{t.ImagePointer:X8})";string? dds=t.Image!=null&&(availableDds==null||availableDds.Contains(t.Image))?$"../textures/{Safe(t.Image)}.dds":null;char cu=(t.SamplerState&0x20)!=0?'u':'-',cv=(t.SamplerState&0x40)!=0?'v':'-',cw=(t.SamplerState&0x80)!=0?'w':'-';b.AppendLine($"    [{i}] {semantic}").AppendLine("    {").AppendLine($"      image       = {image}");if(dds!=null)b.AppendLine($"      dds         = \"{dds}\"");else if(t.Image!=null)b.AppendLine("      dds         = external (not stored in supplied IPAK)");b.AppendLine($"      slot_hash   = 0x{t.SlotHash:X8}").AppendLine($"      name_range  = '{Chr(t.NameStart)}' .. '{Chr(t.NameEnd)}'").AppendLine($"      filter      = {Filter[t.SamplerState&7]}").AppendLine($"      mip_filter  = {Mip((t.SamplerState>>3)&3)}").AppendLine($"      clamp       = {cu}{cv}{cw}").AppendLine($"      mature      = {t.Mature.ToString().ToLowerInvariant()}").AppendLine("    }");}b.AppendLine("  }");
        b.AppendLine().AppendLine("  constants").AppendLine("  {");foreach(var c in m.Constants)b.AppendLine($"    {c.Name,-12} = ({F(c.X)}, {F(c.Y)}, {F(c.Z)}, {F(c.W)})  // 0x{c.NameHash:X8}");b.AppendLine("  }");
        b.AppendLine().AppendLine("  render_states").AppendLine("  {");for(int i=0;i<m.States.Count;i++){var s=m.States[i];b.AppendLine($"    [{i}] {{ {DecodeBlend(s.BlendBits)}; {DecodeDepth(s.DepthStencilBits)} }}");}b.AppendLine("  }");b.AppendLine().AppendLine("  technique_state_entries = [");for(int i=0;i<m.StateBitsEntry.Length;i+=12)b.AppendLine("    "+string.Join(", ",m.StateBitsEntry.Skip(i).Take(12).Select(x=>x==0xff?"none":x.ToString()))+",");b.AppendLine("  ]").AppendLine("}");File.WriteAllText(Path.Combine(output,safe+".material"),b.ToString());
    }
    static string DecodeBlend(uint v){string at=((v>>11)&1)==1?"off":((v>>12)&3).ToString();return $"blend rgb={Blend[v&15]}/{Blend[(v>>4)&15]}/{BlendOp[(v>>8)&7]}, alpha={Blend[(v>>16)&15]}/{Blend[(v>>20)&15]}/{BlendOp[(v>>24)&7]}, alpha_test={at}, cull={((v>>14)&3)}, write_rgb={((v>>27)&1)}, write_a={((v>>28)&1)}, gamma={((v>>30)&1)}";}
    static string DecodeDepth(uint v){string dt=((v>>1)&1)==1?"off":((v>>2)&3).ToString();return $"depth write={v&1}, test={dt}, polygon_offset={(v>>4)&3}, stencil_front={(v>>6)&1}, stencil_back={(v>>7)&1}, raw=0x{v:X8}";}
    static string Mip(int n)=>n switch{0=>"disabled",1=>"nearest",2=>"linear",_=>"reserved"};static char Chr(byte c)=>c is >=32 and <127?(char)c:'?';static string F(float v)=>v.ToString("0.######",CultureInfo.InvariantCulture);static string Safe(string s)=>string.Concat(s.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c));static string Ascii(byte[]b,int o,int n)=>Encoding.ASCII.GetString(b,o,n);static ushort U16(byte[]b,int o)=>BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(o,2));static uint U32(byte[]b,int o)=>BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o,4));static int I32(byte[]b,int o)=>BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(o,4));static ulong U64(byte[]b,int o)=>BinaryPrimitives.ReadUInt64BigEndian(b.AsSpan(o,8));static float F32(byte[]b,int o)=>BitConverter.Int32BitsToSingle(I32(b,o));
}

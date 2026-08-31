using T6AssetTool.Core;
using System.Text.Json;
if(args.Length==4&&args[0]=="repack-add")
{
    using var doc=JsonDocument.Parse(File.ReadAllText(args[2]));
    string manifestDir=Path.GetDirectoryName(Path.GetFullPath(args[2]))!;
    var additions=new List<IPakAddition>();
    foreach(var item in doc.RootElement.EnumerateArray())
    {
        string name=item.GetProperty("name").GetString()!;
        string dds=Path.GetFullPath(Path.Combine(manifestDir,item.GetProperty("dds").GetString()!));
        uint nameHash=item.TryGetProperty("name_hash",out var nh)
            ? Convert.ToUInt32(nh.GetString(),16) : ZoneImageScanner.HashName(name);
        uint dataHash=Convert.ToUInt32(item.GetProperty("data_hash").GetString(),16);
        int width=item.GetProperty("width").GetInt32(),height=item.GetProperty("height").GetInt32();
        int levels=item.GetProperty("levels").GetInt32();
        string format=item.GetProperty("format").GetString()!;
        byte[] payload=IPakRepacker.DdsToPayload(dds);
        string metadata=IPakWriter.Metadata(name,format,payload.Length,width,height,levels);
        additions.Add(new(nameHash,dataHash,payload,metadata));
        Console.WriteLine($"LOAD  {name} {nameHash:x8}:{dataHash:x8} <- {Path.GetFileName(dds)}");
    }
    IPakRepacker.Repack(args[1],args[3],new Dictionary<string,byte[]>(),Console.WriteLine,additions);
    return 0;
}
if(args.Length==4&&args[0]=="catalog"){using var p=new IPakReader(args[1]);var wanted=p.Entries.Select(e=>e.NameHash).ToHashSet();var images=ZoneImageScanner.Scan(args[2],wanted);File.WriteAllText(args[3],System.Text.Json.JsonSerializer.Serialize(images,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"Catalog: {images.Count}");return 0;}
if(args.Length is 3 or 4&&args[0]=="zone"){var r=AssetExtractor.RunZoneInline(args[1],args[2],args.Length==4?args[3]:null,Console.WriteLine);return r.Failed==0?0:1;}
if(args.Length is 3 or 4&&args[0]=="repack"){var swaps=args.Length==4?IPakRepacker.FromFolder(args[2],Console.WriteLine):new Dictionary<string,byte[]>();IPakRepacker.Repack(args[1],args[^1],swaps,Console.WriteLine);return 0;}
if(args.Length==3&&args[0]=="ipak"){var r=AssetExtractor.RunIPak(args[1],args[2],Console.WriteLine);return r.Failed==0?0:1;}
if(args.Length<3){Console.Error.WriteLine("""
  T6AssetTool.Cli <input.ipak> <output> <zone.dat> [...]   extract using zone metadata
  T6AssetTool.Cli ipak <input.ipak> <output>              extract using the embedded catalog
  T6AssetTool.Cli catalog <input.ipak> <zone.dat> <out.json>
  T6AssetTool.Cli zone <zone.dat> <output> [name_filter]   extract images stored inline in a .ff
  T6AssetTool.Cli repack <input.ipak> [swaps_dir] <out.ipak>
  T6AssetTool.Cli repack-add <input.ipak> <manifest.json> <out.ipak>
""");return 2;}
try{var r=AssetExtractor.Run(args[0],args.Skip(2),args[1],Console.WriteLine);return r.Failed==0?0:1;}catch(Exception e){Console.Error.WriteLine(e);return 1;}

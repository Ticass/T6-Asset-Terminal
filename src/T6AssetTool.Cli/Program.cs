using T6AssetTool.Core;
if(args.Length==4&&args[0]=="catalog"){using var p=new IPakReader(args[1]);var wanted=p.Entries.Select(e=>e.NameHash).ToHashSet();var images=ZoneImageScanner.Scan(args[2],wanted);File.WriteAllText(args[3],System.Text.Json.JsonSerializer.Serialize(images,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"Catalog: {images.Count}");return 0;}
if(args.Length==3&&args[0]=="ipak"){var r=AssetExtractor.RunIPak(args[1],args[2],Console.WriteLine);return r.Failed==0?0:1;}
if(args.Length<3){Console.Error.WriteLine("Usage: T6AssetTool.Cli <input.ipak> <output> <zone_decompressed.dat> [...]");return 2;}
try{var r=AssetExtractor.Run(args[0],args.Skip(2),args[1],Console.WriteLine);return r.Failed==0?0:1;}catch(Exception e){Console.Error.WriteLine(e);return 1;}

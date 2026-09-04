namespace T6AssetTool.Core;

public sealed record ExtractionResult(int Textures,int Materials,int Failed);
public static class AssetExtractor
{
    public static ExtractionResult RunIPak(string ipakPath,string output,Action<string>? log=null,CancellationToken token=default)
    {
        string full=Path.GetFullPath(output);if(Directory.Exists(full))Directory.Delete(full,true);Directory.CreateDirectory(full);using var ipak=new IPakReader(ipakPath);var images=LoadCatalog(Path.GetFileNameWithoutExtension(ipakPath),ipak,log);var lookup=ipak.Entries.ToDictionary(e=>(e.NameHash,e.DataHash));int ok=0,failed=0;log?.Invoke($"INDEX {ipak.Entries.Count} streamed parts");log?.Invoke($"GROUP {images.Count} complete textures");
        foreach(var image in images){token.ThrowIfCancellationRequested();try{var parts=image.Parts.Where(p=>lookup.ContainsKey((image.NameHash,p.DataHash))).OrderByDescending(p=>p.Width*p.Height).ToList();if(parts.Count==0)continue;var bundles=new List<(byte[],int,int,int)>();for(int i=0;i<parts.Count;i++){var p=parts[i];int next=i+1<parts.Count?parts[i+1].LevelCount:0;bundles.Add((ipak.Extract(lookup[(image.NameHash,p.DataHash)]),p.Width,p.Height,Math.Max(1,p.LevelCount-next)));}DdsWriter.WriteBc(Path.Combine(full,Safe(image.Name)+".dds"),parts[0].Width,parts[0].Height,parts[0].LevelCount,image.Semantic==5?0xE:image.Format,bundles,image.GpuFormat);ok++;if(ok%25==0)log?.Invoke($"DDS   {ok}/{images.Count}");}catch(Exception e){failed++;log?.Invoke($"FAIL  {image.Name}: {e.Message}");}}
        log?.Invoke($"DONE  {ok} DDS textures  |  {failed} failed");return new(ok,0,failed);
    }
    /// <summary>
    /// Image metadata for the package being opened. An embedded catalog wins where one exists
    /// (those were built and verified against a zone), otherwise the catalog is derived from
    /// the package's own metadata section -- see IPakCatalog -- so no fastfile is needed.
    /// </summary>
    static IReadOnlyList<ZoneImage> LoadCatalog(string ipakName,IPakReader ipak,Action<string>? log)
    {
        string resource=$"T6AssetTool.Core.Catalogs.{ipakName}.json";
        using(var s=typeof(AssetExtractor).Assembly.GetManifestResourceStream(resource))
            if(s is not null)
            {
                var embedded=System.Text.Json.JsonSerializer.Deserialize<List<ZoneImage>>(s)??throw new InvalidDataException("Invalid embedded catalog");
                log?.Invoke($"CAT   embedded catalog for {ipakName}.ipak: {embedded.Count} images");
                return embedded;
            }
        var built=IPakCatalog.FromPackage(ipak,log);
        if(built.Count>0)return built;
        log?.Invoke($"CAT   {ipakName}.ipak carries no image metadata section; using hash-named standalone fallback");
        return IPakCatalog.FromIndexOnly(ipak,log);
    }
    public static ExtractionResult Run(string ipakPath,IEnumerable<string> zoneFiles,string output,Action<string>? log=null,CancellationToken token=default)
    {
        string full=Path.GetFullPath(output);if(Directory.Exists(full))Directory.Delete(full,true);string textureOut=Path.Combine(full,"textures"),materialOut=Path.Combine(full,"materials");Directory.CreateDirectory(textureOut);Directory.CreateDirectory(materialOut);
        using var ipak=new IPakReader(ipakPath);var lookup=ipak.Entries.ToDictionary(e=>(e.NameHash,e.DataHash));var wanted=ipak.Entries.Select(e=>e.NameHash).ToHashSet();int ok=0,failed=0,materialsCount=0;
        foreach(string zone in zoneFiles)
        {
            token.ThrowIfCancellationRequested();log?.Invoke($"SCAN  {Path.GetFileName(Path.GetDirectoryName(zone)) ?? Path.GetFileName(zone)}");var allImages=ZoneImageScanner.Scan(zone);var images=allImages.Where(i=>wanted.Contains(i.NameHash)).ToList();int matchedParts=images.Sum(i=>i.Parts.Count(p=>lookup.ContainsKey((i.NameHash,p.DataHash))));var availableDds=images.Select(i=>i.Name).ToHashSet();var materials=MaterialScanner.Scan(zone,allImages);foreach(var material in materials)MaterialScanner.Write(materialOut,material,availableDds);materialsCount+=materials.Count;log?.Invoke($"META  {images.Count} package images / {matchedParts} parts  |  {materials.Count} materials");
            foreach(var image in images)
            {
                token.ThrowIfCancellationRequested();try{var parts=image.Parts.Where(p=>lookup.ContainsKey((image.NameHash,p.DataHash))).OrderByDescending(p=>p.Width*p.Height).ToList();if(parts.Count==0)continue;var bundles=new List<(byte[],int,int,int)>();for(int i=0;i<parts.Count;i++){var p=parts[i];int next=i+1<parts.Count?parts[i+1].LevelCount:0;bundles.Add((ipak.Extract(lookup[(image.NameHash,p.DataHash)]),p.Width,p.Height,Math.Max(1,p.LevelCount-next)));}string safe=Safe(image.Name);DdsWriter.WriteBc(Path.Combine(textureOut,safe+".dds"),parts[0].Width,parts[0].Height,parts[0].LevelCount,image.Semantic==5?0xE:image.Format,bundles,image.GpuFormat);ok++;if(ok%25==0)log?.Invoke($"DDS   {ok} textures written");}catch(Exception e){failed++;log?.Invoke($"FAIL  {image.Name}: {e.Message}");}
            }
        }
        log?.Invoke($"DONE  {ok} textures  |  {materialsCount} materials  |  {failed} failed");return new(ok,materialsCount,failed);
    }
    /// <summary>
    /// Writes the images a zone stores inline -- the ones whose pixels sit in the .ff itself rather
    /// than in an IPAK. ui_zm.ff and patch_ui_zm.ff are entirely inline, so this is the only way to
    /// reach the TranZit globe art.
    /// </summary>
    public static ExtractionResult RunZoneInline(string zonePath,string output,string? filter=null,Action<string>? log=null,CancellationToken token=default)
    {
        string full=Path.GetFullPath(output);Directory.CreateDirectory(full);int ok=0,failed=0;
        var images=ZoneImageScanner.Scan(zonePath).Where(i=>i.Inline&&i.BaseSize>0).ToList();
        if(filter is not null)images=images.Where(i=>i.Name.Contains(filter,StringComparison.OrdinalIgnoreCase)).ToList();
        log?.Invoke($"SCAN  {images.Count} inline images in {Path.GetFileName(Path.GetDirectoryName(zonePath))??Path.GetFileName(zonePath)}");
        foreach(var image in images)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                byte[] pixels=ZoneImageScanner.ReadInlinePayload(zonePath,image);
                if(pixels.Length==0){failed++;log?.Invoke($"FAIL  {image.Name}: no payload");continue;}
                DdsWriter.WriteBc(Path.Combine(full,Safe(image.Name)+".dds"),image.Width,image.Height,image.LevelCount,
                                  image.Semantic==5?0xE:image.Format,new[]{(pixels,image.Width,image.Height,image.LevelCount)},image.GpuFormat);
                ok++;log?.Invoke($"DDS   {image.Name}  {image.Width}x{image.Height}  {image.BaseSize} bytes");
            }
            catch(Exception e){failed++;log?.Invoke($"FAIL  {image.Name}: {e.Message}");}
        }
        log?.Invoke($"DONE  {ok} inline textures  |  {failed} failed");return new(ok,0,failed);
    }

    static string Safe(string name)=>string.Concat(name.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c));
}

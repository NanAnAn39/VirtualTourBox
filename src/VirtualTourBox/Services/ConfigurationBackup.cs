using System.IO;
using System.IO.Compression;
using System.Text.Json;
using VirtualTourBox.Models;

namespace VirtualTourBox.Services;

public static class ConfigurationBackup
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VirtualTourBox");

    public static void Export(string destination)
    {
        var files = new List<string>();
        if (File.Exists(Path.Combine(Root, "settings.json"))) files.Add(Path.Combine(Root, "settings.json"));
        var profiles = Path.Combine(Root, "profiles");
        if (Directory.Exists(profiles)) files.AddRange(Directory.GetFiles(profiles, "*.json"));
        if (files.Count == 0) throw new InvalidDataException("没有可备份的配置");
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
                foreach (var file in files)
                    archive.CreateEntryFromFile(file, Path.GetRelativePath(Root, file).Replace('\\','/'));
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static string Restore(string source)
    {
        using var archive = ZipFile.OpenRead(source);
        var payload = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            string name = entry.FullName;
            if (entry.Length > 2_000_000 || (total += entry.Length) > 20_000_000)
                throw new InvalidDataException("备份文件过大");
            bool settings = name == "settings.json";
            bool profile = name.StartsWith("profiles/", StringComparison.Ordinal) &&
                name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                name[9..].IndexOfAny(new[]{'/','\\',':'}) < 0 && !name.Contains("..");
            if (!settings && !profile) throw new InvalidDataException("备份包含不支持的文件：" + name);
            if (payload.ContainsKey(name)) throw new InvalidDataException("备份存在重名文件");
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            byte[] bytes = memory.ToArray();
            if (settings)
            {
                var value = JsonSerializer.Deserialize<AppSettings>(bytes) ?? throw new InvalidDataException("设置为空");
                if (value.Devices is null) throw new InvalidDataException("校准设置无效");
            }
            else
            {
                var value = JsonSerializer.Deserialize<MappingProfile>(bytes) ?? throw new InvalidDataException("场景为空");
                if (value.SchemaVersion != 1 || value.Target is not ("Windows" or "iPad" or "Game") || value.Bindings is null)
                    throw new InvalidDataException("场景版本或输出目标无效");
            }
            payload.Add(name,bytes);
        }
        if (!payload.ContainsKey("settings.json")) throw new InvalidDataException("备份缺少 settings.json");
        Directory.CreateDirectory(Root);
        string backup = Path.Combine(Root, "before-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6] + ".zip");
        Export(backup);
        var previous = new Dictionary<string,byte[]?>();
        try
        {
            // Merge profiles so existing scenes absent from the backup remain available.
            foreach (var file in payload)
            {
                string target = Path.Combine(Root,file.Key.Replace('/',Path.DirectorySeparatorChar));
                previous[target] = File.Exists(target) ? File.ReadAllBytes(target) : null;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string temporary = target + ".restore";
                File.WriteAllBytes(temporary,file.Value);
                File.Move(temporary,target,true);
            }
        }
        catch
        {
            foreach (var file in previous)
            {
                if (file.Value is null) File.Delete(file.Key);
                else File.WriteAllBytes(file.Key,file.Value);
            }
            throw;
        }
        return backup;
    }
}


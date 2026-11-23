using System.IO;
using System.Text.Json;
using VirtualTourBox.Models;
namespace VirtualTourBox.Services;

public sealed class ProfileStore
{
    public static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VirtualTourBox", "profiles");
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public ProfileStore() { Directory.CreateDirectory(DirectoryPath); }
    public IReadOnlyList<string> Names() => Directory.EnumerateFiles(DirectoryPath, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(x => x).ToArray()!;
    static string PathFor(string name) => Path.Combine(DirectoryPath, Sanitize(name) + ".json");
    public static string Sanitize(string name)
    {
        string safe = string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ' ? c : '_')).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "未命名" : safe;
    }
    public MappingProfile Load(string name) => JsonSerializer.Deserialize<MappingProfile>(File.ReadAllText(PathFor(name)), JsonOptions) ?? throw new InvalidDataException("配置为空");
    public void Save(MappingProfile profile)
    {
        string path = PathFor(profile.Name), temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(profile, JsonOptions));
        File.Move(temp, path, true);
    }
    public static MappingProfile ProcreateDefault() => new() {Name="Procreate",Target="iPad",Bindings=new()
    {
        ["A"]=new(){Action="Redo"},["A.Hold"]=new(){Action="RedoRepeat"},
        ["B"]=new(){Action="Undo"},["B.Hold"]=new(){Action="UndoRepeat"},
        ["X"]=new(){Action="Brush"},["X.Hold"]=new(){Action="Smudge"},
        ["Y"]=new(){Action="Eraser"},["Y.Hold"]=new(){Action="TemporaryEraser"},
        ["L"]=new(){Action="Layers"},["ZL"]=new(){Action="Colors"},["Start"]=new(){Action="QuickMenu"},
        ["DPadUp"]=new(){Action="BrushLarger"},["DPadDown"]=new(){Action="BrushSmaller"},
        ["DPadLeft"]=new(){Action="Selection"},["DPadRight"]=new(){Action="Transform"}
    }};
    public void EnsureDefaults()
    {
        if (Names().Count > 0) return;
        Save(ProcreateDefault());
        if (!File.Exists(PathFor("Windows 绘画"))) Save(new MappingProfile { Name="Windows 绘画", Target="Windows", Bindings = new()
        {
            ["A"] = new(){Action="Redo"}, ["B"] = new(){Action="Undo"}, ["X"] = new(){Action="Brush"}, ["Y"] = new(){Action="Eraser"}
        }});
        if (!File.Exists(PathFor("原生游戏"))) Save(new MappingProfile { Name="原生游戏", Target="Game" });
    }
}


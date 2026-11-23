using System.IO;
using System.Text.Json;

namespace VirtualTourBox.Services;
public sealed class AppSettings
{
    public string BridgeExecutablePath { get; set; } = "";
    public bool MinimizeToTray { get; set; } = true;
    public bool AutoStartBridge { get; set; } = false;
    public bool StartWithWindows { get; set; } = false;
    public Dictionary<string, StickCalibration> Devices { get; set; } = new();
    public bool AutoRestartBridge { get; set; } = false;
    public bool AutoReconnect { get; set; } = true;
    public string ConfirmedHostId { get; set; } = "";
    public int RepeatDelayMs { get; set; } = 500;
    public int RepeatIntervalMs { get; set; } = 150;
    public string ControllerLayout { get; set; } = "Nintendo";
    public string LastProfile { get; set; } = "Windows 绘画";
}
public sealed class StickCalibration
{
    public Dictionary<string,int> Buttons { get; set; } = new()
    {
        ["A"]=1, ["B"]=2, ["X"]=3, ["Y"]=4, ["L"]=5, ["R"]=6,
        ["Select"]=7, ["Start"]=8, ["L3"]=9, ["R3"]=10
    };
    public string LeftXAxis { get; set; } = "X";
    public string LeftYAxis { get; set; } = "Y";
    public string RightXAxis { get; set; } = "Auto";
    public string RightYAxis { get; set; } = "Auto";
    public bool InvertLeftX { get; set; }
    public bool InvertLeftY { get; set; }
    public bool InvertRightX { get; set; }
    public bool InvertRightY { get; set; }
    public uint AxisMinimum { get; set; } = 0;
    public uint AxisMaximum { get; set; } = 65535;
    public int PovRotationDegrees { get; set; }
    public uint SharedZHigh { get; set; } = 41000;
    public uint SharedZLow { get; set; } = 24500;
    public uint TriggerThreshold { get; set; } = 17694;
    public double LeftDeadzone { get; set; } = 0.25;
    public double RightDeadzone { get; set; } = 0.25;
    public uint LeftX { get; set; } = 32768;
    public uint LeftY { get; set; } = 32768;
    public uint RightX { get; set; } = 32768;
    public uint RightY { get; set; } = 32768;
}
public static class SettingsStore
{
    private static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VirtualTourBox", "settings.json");
    public static AppSettings Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new() : new(); }
        catch { return new(); }
    }
    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tmp=FilePath+".tmp";
        File.WriteAllText(tmp,JsonSerializer.Serialize(settings,new JsonSerializerOptions { WriteIndented=true }));
        if (File.Exists(FilePath)) File.Copy(FilePath, FilePath+".bak", true);
        File.Move(tmp,FilePath,true);
    }
    public static void SetStartWithWindows(bool enabled)
    {
        // HKCU only; no admin privileges or scheduled tasks required.
        using var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true)
            ?? throw new InvalidOperationException("无法打开当前用户启动项注册表键");
        if (enabled)
        {
            string exe=Environment.ProcessPath ?? throw new InvalidOperationException("无法确定程序位置");
            // dotnet run launches the apphost from bin; for source/testing this will be that apphost.
            key.SetValue("VirtualTourBox", "\"" + exe + "\"");
        }
        else key.DeleteValue("VirtualTourBox",false);
    }
}


namespace VirtualTourBox.Models;

public sealed class MappingProfile
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "新配置";
    // Windows is the only implemented output in MVP; iPad is intentionally inactive.
    public string Target { get; set; } = "Windows";
    public Dictionary<string, MappingBinding> Bindings { get; set; } = new();
}
public sealed class MappingBinding
{
    // None, Undo, Redo, Brush, Eraser, CustomKey, ZoomIn, ZoomOut, PanCanvas, RotateCanvas
    public string Action { get; set; } = "None";
    public string Shortcut { get; set; } = "";
}
public sealed class BindingRow : System.ComponentModel.INotifyPropertyChanged
{
    public static Dictionary<string,string> Labels { get; set; } = new();
    public string Input { get; set; } = "";
    string action = "None";
    string shortcut = "";
    public string Action { get => action; set { action=value; Changed(nameof(Action)); Changed(nameof(ActionLabel)); } }
    public string Shortcut { get => shortcut; set { shortcut=value; Changed(nameof(Shortcut)); Changed(nameof(ActionLabel)); } }
    public string ActionLabel => Action == "CustomKey" ? Shortcut : Labels.GetValueOrDefault(Action, Action);
    public static string ControllerLayout { get; set; } = "Nintendo";
    public void RefreshLayout() => Changed(nameof(DisplayInput));
    public static string InputLabel(string input) => ControllerLayout == "PlayStation" && input is "A" or "B" or "X" or "Y" ? input switch { "A" => "×", "B" => "○", "X" => "□", _ => "△" } : input
        .Replace("LeftStick", "左摇杆").Replace("RightStick", "右摇杆")
        .Replace("DPad", "十字键").Replace("Up", "上").Replace("Down", "下")
        .Replace("Left", "左").Replace("Right", "右")
        .Replace("Select", "选择").Replace("Start", "开始");
    public string DisplayInput => Input.EndsWith(".Hold") ? InputLabel(Input[..^5]) + " 长按" : Input.EndsWith(".Double") ? InputLabel(Input[..^7]) + " 双击" :
        Input.Contains('+') || Input.Contains("Stick") || Input.StartsWith("DPad") ? InputLabel(Input) : InputLabel(Input) + " 单击";
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    void Changed(string name) => PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(name));
}
public sealed class CalibrationSettings
{
    public string DeviceName { get; set; } = "IINE L1807";
    // joy.cpl buttons are 1-based; WinMM bitmasks are 0-based.
    public Dictionary<string,int> Buttons { get; set; } = new()
    {
        ["A"] = 1, ["B"] = 2, ["X"] = 3, ["Y"] = 4,
        ["L"] = 5, ["R"] = 6, ["Select"] = 7, ["Start"] = 8,
        ["L3"] = 9, ["R3"] = 10
    };
    public double TriggerDeadzone { get; set; } = 0.27;
    public double StickDeadzone { get; set; } = 0.18;
}


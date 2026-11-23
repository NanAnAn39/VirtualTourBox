using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Threading;
using VirtualTourBox.Input;
using VirtualTourBox.Models;
using VirtualTourBox.Services;
// Explicit WPF aliases: UseWindowsForms also injects WinForms types with the same names.
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace VirtualTourBox;
public partial class MainWindow : Window
{
    readonly ProfileStore store = new();
    readonly MappingEngine engine = new();
    readonly BleHidCliBridge ipadBridge = new();
    readonly DispatcherTimer pollTimer = new() { Interval = TimeSpan.FromMilliseconds(25) };
    readonly ObservableCollection<BindingRow> rows = new();
    ControllerDevice? selectedDevice;
    MappingProfile? currentProfile;
    bool initializing;
    bool restoringBackup;
    bool revertingSelection;
    StickCalibration calibration = new();
    bool leftActive, rightActive;
    readonly StickDirection leftDirection = new();
    readonly StickDirection rightDirection = new();
    readonly StickGuard leftGuard = new();
    readonly StickGuard rightGuard = new();
    readonly AppSettings settings = SettingsStore.Load();
    TrayIcon? trayIcon;

    bool bridgeWasRunning;
    bool bridgeStoppedByUser = true;
    int bridgeRestartAttempts;
    DateTime nextBridgeRestart = DateTime.MaxValue;
    DateTime healthySince = DateTime.MaxValue;
    DateTime lastDeviceRetry = DateTime.MinValue;

    // Ordered explicitly so frequently used edge options stay easy to locate.
    public List<KeyValuePair<string,string>> ActionOptions { get; } = new()
    {
        new("None", "不绑定"),
        new("Undo", "撤销"), new("UndoRepeat", "按住连续撤销"), new("RedoRepeat", "按住连续重做"), new("Redo", "重做"), new("Brush", "画笔"), new("Eraser", "橡皮擦"), new("TemporaryEraser", "按住橡皮擦，松开回画笔"), new("Smudge", "按住涂抹（iPadOS 18及以上，实验）"), new("SmudgeLegacy", "按住涂抹（iPadOS 17及以下，实验）"), new("BrushSmaller", "减小笔刷"), new("BrushLarger", "增大笔刷"), new("QuickMenu", "快捷菜单（需在 iPad 点选）"),
        new("Selection", "选区"), new("Transform", "变换（箭头）"),
        new("Layers", "打开图层"), new("Colors", "打开颜色面板"),
        new("ZoomIn", "放大画布（待实现）"), new("ZoomOut", "缩小画布（待实现）"),
        new("PanCanvas", "移动画布（待实现）"), new("RotateCanvas", "旋转画布（待实现）"),
        new("CustomKey", "自定义快捷键")
    };
    static readonly string[] Inputs = { "A", "B", "X", "Y", "DPadUp", "DPadDown", "DPadLeft", "DPadRight", "LeftStick", "LeftStickUp", "LeftStickDown", "LeftStickLeft", "LeftStickRight", "RightStick", "RightStickUp", "RightStickDown", "RightStickLeft", "RightStickRight", "L", "R", "ZL", "ZR", "Select", "Start", "L3", "R3" };
    static IEnumerable<string> InputVariants(string input)
    {
        yield return input;
        if(new[]{"A","B","X","Y","L","R","Select","Start"}.Contains(input))yield return input+".Double";
        if(!input.Contains("Stick"))yield return input+".Hold";
    }
    readonly Dictionary<string,int> numbers = new() { ["A"]=1,["B"]=2,["X"]=3,["Y"]=4,["L"]=5,["R"]=6,["Select"]=7,["Start"]=8,["L3"]=9,["R3"]=10 };
    public MainWindow()
    {
        BindingRow.Labels = ActionOptions.ToDictionary(item => item.Key, item => item.Value);
        InitializeComponent();
        DataContext = this;
        BindingsList.ItemsSource = rows;
        ipadBridge.ConfirmedHostId = settings.ConfirmedHostId;
        ipadBridge.AutoReconnect = settings.AutoReconnect;
        pollTimer.Tick += Poll;
        ipadBridge.Changed += () => Dispatcher.BeginInvoke(() => {
            var log = ipadBridge.RecentOutput;
            IpadBridgeLog.Text = log.Length > 2400 ? log[^2400..] : log;
            IpadBridgeState.Text = ipadBridge.Running
                ? $"CLI 运行中 · 键盘订阅：{(ipadBridge.KeyboardSubscriptions?.ToString() ?? "待检查")} · 目标：{(ipadBridge.HostConfirmed ? $"Host {ipadBridge.SelectedHostNumber}" : "未选择")}"
                : "蓝牙桥接未运行";
            TestIpadButton.IsEnabled = ipadBridge.ReadyForInput;
            CompactConnection.Text = ipadBridge.ReadyForInput ? "●  iPad · 已连接" : ipadBridge.Running ? "iPad · 等待连接" : "iPad · 未连接";
        });
        engine.SendIpadKey = key => ipadBridge.SendSingleKey(key);
        engine.SetIpadSmudge = (pressed, legacy) => ipadBridge.SetSmudge(pressed, legacy);
        engine.SendIpadCombo = combo => ipadBridge.SendCombo(combo);

    }
    void Window_Loaded(object sender, RoutedEventArgs e)
    {
        initializing = true;
        try {
            store.EnsureDefaults();
            string bundledCli = Path.Combine(AppContext.BaseDirectory, "BleHid", "BleHid.Cli.exe");
            if (File.Exists(bundledCli) && File.Exists(Path.Combine(Path.GetDirectoryName(bundledCli)!, "VirtualTourBox.hold-enabled")))
            {
                settings.BridgeExecutablePath = bundledCli;
                SettingsStore.Save(settings);
            }
            BridgePath.Text = settings.BridgeExecutablePath;
            ControllerLayoutCombo.SelectedIndex = settings.ControllerLayout switch { "Xbox" => 1, "PlayStation" => 2, _ => 0 };
            BindingRow.ControllerLayout = settings.ControllerLayout;
            TrayCheckbox.IsChecked = settings.MinimizeToTray;
            AutoBridgeCheckbox.IsChecked = settings.AutoStartBridge;
            settings.AutoRestartBridge = false;
            AutoRestartCheckbox.IsChecked = false;
            StartupCheckbox.IsChecked = settings.StartWithWindows;
            RefreshProfiles(settings.LastProfile); RefreshDevices();
        }
        catch(Exception ex) { MessageBox.Show("配置初始化失败：" + ex.Message); }
        finally { initializing=false; }
        engine.RepeatDelayMs = Math.Clamp(settings.RepeatDelayMs, 100, 2000);
        engine.RepeatIntervalMs = Math.Clamp(settings.RepeatIntervalMs, 50, 1000);
        RepeatDelay.Value = engine.RepeatDelayMs;
        RepeatInterval.Value = engine.RepeatIntervalMs;
        LoadSelectedProfile();
        trayIcon ??= new TrayIcon(this, ShowMainWindow, ToggleMappingFromTray, ShowTraySettings, ExitFromTray);
        trayIcon.Register();
        pollTimer.Start();
        if (settings.AutoStartBridge && File.Exists(settings.BridgeExecutablePath))
            Dispatcher.BeginInvoke(new Action(() => StartBridge_Click(this, new RoutedEventArgs())), DispatcherPriority.Background);
    }
    string LastDeviceDiagnostics = "";
    void RefreshDevices()
    {
        uint? currentId = selectedDevice?.Id;
        bool? wasXInput = selectedDevice?.IsXInput;
        List<ControllerDevice> devices;
        try { var result = WinMmController.DiscoverDetailed(); devices = result.Devices; LastDeviceDiagnostics = result.Diagnostics; }
        catch (Exception ex)
        {
            selectedDevice = null;
            DeviceCombo.ItemsSource = new List<ControllerDevice>();
            InputStatus.Text = "扫描手柄失败：" + ex.Message;
            return;
        }
        // Prefer the previously selected input interface; otherwise use XInput.
        var previous = devices.FirstOrDefault(d => d.Id == currentId && d.IsXInput == wasXInput);
        var preferred = previous ?? devices.FirstOrDefault(d => d.IsXInput) ?? devices.FirstOrDefault();
        DeviceCombo.ItemsSource = null;
        DeviceCombo.ItemsSource = devices;
        DeviceCombo.SelectedItem = preferred;
        selectedDevice = preferred;
        InputStatus.Text = selectedDevice is null
            ? "未找到手柄。XInput / WinMM 诊断：" + LastDeviceDiagnostics
            : $"已选择 {selectedDevice.Name}（{(selectedDevice.IsXInput ? "XInput" : "WinMM")}） · 发现 {devices.Count} 个输入接口";
    }
    void RefreshProfiles(string? select=null)
    {
        var names=store.Names().ToList();
        ProfileCombo.ItemsSource=names;
        ProfileCombo.SelectedItem=names.Contains(select ?? "") ? select : names.FirstOrDefault();
    }
    void LoadSelectedProfile()
    {
        if (ProfileCombo.SelectedItem is not string name) return;
        engine.Enabled = false;
        engine.Profile = null;
        engine.Reset();
        settings.LastProfile = name;
        SettingsStore.Save(settings);
        try
        {
            engine.Enabled=false; engine.Reset(); ToggleButton.Content="启动映射";
            currentProfile=store.Load(name); engine.Profile=currentProfile;
            selectedInput=null; inspectorDraft=null; InspectorPanel.Children.Clear();InspectorActions.Children.Clear();
            InspectorPanel.Children.Add(new TextBlock {Text="选择一个按键",FontSize=22,Margin=new Thickness(0,20,0,0)});
            rows.Clear();
            foreach (string key in Inputs.SelectMany(InputVariants).Concat(new[] { "L+A", "L+B", "L+X", "L+Y", "R+A", "R+B", "R+X", "R+Y" }))
            {
                currentProfile.Bindings.TryGetValue(key,out var binding);
                rows.Add(new BindingRow { Input=key,Action=binding?.Action ?? "None",Shortcut=binding?.Shortcut ?? "" });
            }
            TargetNotice.Text=currentProfile.Target switch
            {
                "iPad" => "iPad：画笔和橡皮擦支持单键；支持内置工具、按住涂抹与临时橡皮擦；画布移动、缩放、旋转待实现。",
                "Game" => "原生游戏模式：不发送模拟键盘输入。",
                _ => "Windows：快捷键发送到当前前台应用。请启用映射后切回目标窗口。"
            };
            BuildControllerDiagram();
            OutputStatus.Text="映射未启动";
            EditControllerInput("A");
        }
        catch(Exception ex) { MessageBox.Show("读取配置失败："+ex.Message); }
    }
    void DeviceCombo_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        selectedDevice=DeviceCombo.SelectedItem as ControllerDevice;
        engine.Reset();
        leftActive = rightActive = false;
        leftGuard.Reset(); rightGuard.Reset();
        string key = DeviceCalibrationKey();
        calibration = settings.Devices.TryGetValue(key, out var saved) ? saved : new();
        if (LeftDeadzone != null)
        {
            LeftDeadzone.Value = calibration.LeftDeadzone * 100;
            RightDeadzone.Value = calibration.RightDeadzone * 100;
        }
        if (selectedDevice != null)
            InputStatus.Text=$"已选择 {selectedDevice.Name}（{(selectedDevice.IsXInput ? "XInput" : "WinMM")}），请按键测试";
    }
    void ProfileCombo_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if (initializing || revertingSelection) return;
        if (!ResolveUnsaved())
        {
            revertingSelection = true;
            ProfileCombo.SelectedItem = currentProfile?.Name;
            revertingSelection = false;
            return;
        }
        LoadSelectedProfile();
    }
    void RefreshDevices_Click(object sender,RoutedEventArgs e) => RefreshDevices();
    void ToggleButton_Click(object sender,RoutedEventArgs e)
    {
        if(currentProfile is null) return;
        if(currentProfile.Target=="Game")
        {
            MessageBox.Show("原生游戏模式不发送模拟输入。","提示");
            engine.Enabled=false; return;
        }
        if(!engine.Enabled && currentProfile.Target=="iPad" && !ipadBridge.ReadyForInput)
        {
            MessageBox.Show("请先启动 CLI、检查订阅数 > 0、核实并选择你的 iPad Host。","iPad 尚未就绪");
            return;
        }
        if(!engine.Enabled && selectedDevice is null) { MessageBox.Show("请先连接并选择手柄。"); return; }
        engine.Enabled=!engine.Enabled;
        engine.Reset();
        ToggleButton.Content=engine.Enabled?"暂停映射":"启动映射";
        OutputStatus.Text=engine.Enabled?(currentProfile.Target=="iPad" ? "iPad 映射已开启；组合键需增强版 CLI" : "映射已启用；按手柄按钮发送 Windows 快捷键"):"映射已暂停";
    }
    void SaveProfile_Click(object sender,RoutedEventArgs e)
    {
        if(currentProfile is null) return;
        try
        {
            if (rows.Any(r => r.Input.EndsWith(".Double") && r.Action is "Smudge" or "SmudgeLegacy" or "TemporaryEraser"))
            {
                MessageBox.Show("按住工具请绑定在普通按键行，双击行请选择单次动作。");
                return;
            }
            if(rows.Any(r=> !r.Input.EndsWith(".Hold") && r.Action is "Smudge" or "SmudgeLegacy" or "TemporaryEraser" &&
                rows.Any(h=>h.Input==r.Input+".Hold"&&h.Action!="None")))
            {ShowToast("同一按键已有长按功能，请将按住工具放在长按页");return;}
            var updated = new MappingProfile
            {
                Name = currentProfile.Name,
                Target = currentProfile.Target,
                SchemaVersion = currentProfile.SchemaVersion,
                Bindings = currentProfile.Bindings.Where(pair => !rows.Any(row => row.Input == pair.Key)).Concat(rows.Where(r=>r.Action!="None").Select(r => new KeyValuePair<string, MappingBinding>(r.Input, new MappingBinding { Action=r.Action,Shortcut=r.Shortcut }))).ToDictionary(pair => pair.Key, pair => pair.Value)
            };
            store.Save(updated);
            currentProfile = updated;
            engine.Profile = updated;
            engine.Reset();
            OutputStatus.Text="配置保存成功："+currentProfile.Name;
            ShowToast("已保存按键映射");
        }
        catch(Exception ex) { ShowToast("保存失败："+ex.Message); }
    }
    void CreateProfile_Click(object sender,RoutedEventArgs e) => NewProfile(false);
    void DuplicateProfile_Click(object sender,RoutedEventArgs e) => NewProfile(true);
    void NewProfile(bool copy)
    {
        if (!ResolveUnsaved()) return;
        string name=ProfileStore.Sanitize(NewProfileName.Text);
        if(store.Names().Contains(name, StringComparer.OrdinalIgnoreCase)) { MessageBox.Show("配置名称已存在，请更换名称。"); return; }
        try
        {
            var profile=new MappingProfile { Name=name,Target=copy && currentProfile != null ? currentProfile.Target : ((ComboBoxItem)NewTarget.SelectedItem).Tag.ToString()! };
            if(copy) profile.Bindings=rows.Where(r=>r.Action!="None").ToDictionary(r=>r.Input,r=>new MappingBinding{Action=r.Action,Shortcut=r.Shortcut});
            store.Save(profile);
            RefreshProfiles(name);
            LoadSelectedProfile();
        }
        catch(Exception ex) { MessageBox.Show("新建失败："+ex.Message); }
    }
    void GameMode_Click(object sender,RoutedEventArgs e)
    {
        if (!ResolveUnsaved()) return;
        engine.Enabled=false; engine.Reset();
        ProfileCombo.SelectedItem="原生游戏";
        LoadSelectedProfile();
    }
    void OpenFolder_Click(object sender,RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("explorer.exe",$"\"{ProfileStore.DirectoryPath}\"") { UseShellExecute=true });
    }
    DateTime lastBridgeCheck = DateTime.MinValue;
    void Poll(object? sender,EventArgs e)
    {
        if ((DateTime.UtcNow-lastBridgeCheck).TotalSeconds >= 1)
        {
            lastBridgeCheck = DateTime.UtcNow;
            MaintainBridgeRestart();
            ipadBridge.MaintainConnection();
            if (settings.ConfirmedHostId != ipadBridge.ConfirmedHostId)
            {
                settings.ConfirmedHostId = ipadBridge.ConfirmedHostId;
                try { SettingsStore.Save(settings); }
                catch (Exception ex) { OutputStatus.Text = "连接记忆保存失败：" + ex.Message; }
            }
        }
        if (currentProfile?.Target == "iPad" && !ipadBridge.ReadyForInput)
        {
            engine.Reset();
        }
        if (selectedDevice is null || !WinMmController.TryRead(selectedDevice,out var state))
        {
            engine.Reset();
            leftActive = rightActive = false;
        leftGuard.Reset(); rightGuard.Reset();
            StickPreview.Text = "手柄断开，摇杆已停止";
            InputStatus.Text=selectedDevice is null ? "等待手柄连接（每 2 秒扫描）" : "已选手柄暂时读取失败（尝试重新扫描）";
            UpdateControllerHighlights(new HashSet<string>(), true);
            leftDirection.Update(0,0,false); rightDirection.Update(0,0,false);
            AxisStatus.Text="";
            if ((DateTime.UtcNow-lastDeviceRetry).TotalSeconds >= 2)
            {
                lastDeviceRetry=DateTime.UtcNow;
                RefreshDevices();
            }
            return;
        }
        var active=new HashSet<string>();
        foreach (var entry in calibration.Buttons) if(state.Pressed(entry.Value)) active.Add(entry.Key);
        int calibratedPov = state.Pov < 0 ? -1 : (state.Pov + calibration.PovRotationDegrees * 100 + 36000) % 36000;
        if(calibratedPov>=0)
        {
            // Windows POV in hundredths of a degree: 0=up, 9000=right, 18000=down, 27000=left.
            if(calibratedPov >= 31500 || calibratedPov < 4500) active.Add("DPadUp");
            if(calibratedPov >= 4500 && calibratedPov < 13500) active.Add("DPadRight");
            if(calibratedPov >= 13500 && calibratedPov < 22500) active.Add("DPadDown");
            if(calibratedPov >= 22500 && calibratedPov < 31500) active.Add("DPadLeft");
        }
        // L1807 reported a shared Z axis. The normalized center/endpoints are approximate
        // until a future calibration wizard measures neutral and endpoints.
        if (selectedDevice.IsXInput)
        {
            if (state.Z > calibration.TriggerThreshold) active.Add("ZL");
            if (state.V > calibration.TriggerThreshold) active.Add("ZR");
        }
        else
        {
            if (state.Z > calibration.SharedZHigh) active.Add("ZL");
            if (state.Z < calibration.SharedZLow) active.Add("ZR");
        }
        double lx = AxisValue(ReadAxis(state, calibration.LeftXAxis, state.X), calibration.LeftX);
        double ly = AxisValue(ReadAxis(state, calibration.LeftYAxis, state.Y), calibration.LeftY);
        double rx = AxisValue(ReadAxis(state, calibration.RightXAxis, selectedDevice.IsXInput ? state.R : state.U), calibration.RightX);
        double ry = AxisValue(ReadAxis(state, calibration.RightYAxis, selectedDevice.IsXInput ? state.U : state.R), calibration.RightY);
        lx *= calibration.InvertLeftX ? -1 : 1;
        ly *= calibration.InvertLeftY ? -1 : 1;
        rx *= calibration.InvertRightX ? -1 : 1;
        ry *= calibration.InvertRightY ? -1 : 1;
        double left = Math.Min(1, Math.Sqrt(lx*lx + ly*ly));
        double right = Math.Min(1, Math.Sqrt(rx*rx + ry*ry));
        leftActive = leftGuard.Update(left, calibration.LeftDeadzone, active.Contains("L3"));
        rightActive = rightGuard.Update(right, calibration.RightDeadzone, active.Contains("R3"));
        StickPreview.Text = $"方向 左({lx:F2},{ly:F2}) 右({rx:F2},{ry:F2})\n左 {left:P0} / 阈值 {calibration.LeftDeadzone:P0} · {(leftGuard.Blocked ? "按压保护，等待回中" : leftActive ? "已触发" : "未触发")}\n右 {right:P0} / 阈值 {calibration.RightDeadzone:P0} · {(rightGuard.Blocked ? "按压保护，等待回中" : rightActive ? "已触发" : "未触发")}";
        var leftKey = leftDirection.Update(lx, ly, leftActive);
        var rightKey = rightDirection.Update(rx, ry, rightActive);
        if (leftKey != null) active.Add("LeftStick" + leftKey);
        if (rightKey != null) active.Add("RightStick" + rightKey);
        if (leftActive && !HasDirectionalBindings("LeftStick")) active.Add("LeftStick");
        if (rightActive && !HasDirectionalBindings("RightStick")) active.Add("RightStick");
        UpdateControllerHighlights(active);
        // Stick axes are monitored only: no unverified canvas gestures are emitted.
        InputStatus.Text=active.Count==0?"无按键按下":string.Join(" · ",active.OrderBy(x=>x));
        AxisStatus.Text=$"X:{state.X,5}  Y:{state.Y,5}\nZ:{state.Z,5}  R:{state.R,5}\nU:{state.U,5}  V:{state.V,5}\nPOV:{state.Pov}";
        if (System.Windows.Input.Keyboard.FocusedElement is TextBox { IsReadOnly: true } recorder &&
            recorder.DataContext is BindingRow)
        {
            engine.Reset();
            return;
        }
        engine.Update(active);
        if (engine.Enabled) OutputStatus.Text=engine.Status;
    }

    // The shortcut field is deliberately read-only: users record keys rather than typing syntax.
    // Recording is allowed while mappings are enabled because keyboard events are never sent
    // back through the controller output module.
    void ShortcutRecorder_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box)
        {
            box.SelectAll();
            box.ToolTip = "直接按快捷键组合（例如 Ctrl+A）；Esc 取消，Backspace/Delete 清除";
        }
    }

    void ShortcutRecorder_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not BindingRow row) return;
        e.Handled = true; // Never insert normal text into this read-only recorder.

        // Chinese IMEs can replace Key with ImeProcessed, which must never be recorded as text.
        Key key = e.Key;
        if (key == Key.ImeProcessed) key = e.ImeProcessedKey;
        if (key == Key.System) key = e.SystemKey;
        if (key is Key.ImeProcessed or Key.DeadCharProcessed or Key.None)
        {
            box.ToolTip = "输入法未提供实体按键；可切换英文输入法后重试";
            return;
        }
        if (key == Key.Escape) { Keyboard.ClearFocus(); return; }
        if ((key == Key.Back || key == Key.Delete) && Keyboard.Modifiers == ModifierKeys.None)
        {
            row.Shortcut = "";
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            Keyboard.ClearFocus();
            return;
        }

        // Wait until the user presses a non-modifier key; otherwise Ctrl alone could be recorded.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin || key == Key.None)
            return;

        var parts = new List<string>();
        var modifiers = Keyboard.Modifiers;
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString()); // Key names (D1, OemPlus, F5 etc.) match KeyboardOutput parser.
        string shortcut = string.Join("+", parts);
        // Don't save a shortcut that our Windows output backend can't understand.
        if (!KeyboardOutput.IsValidShortcut(shortcut))
        {
            box.ToolTip = "此按键暂不支持映射，请改用其他组合键";
            return;
        }
        row.Shortcut = shortcut;
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        Keyboard.ClearFocus();
    }

    void SelectBridge_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog {
            Title = "选择官方 BleHid.Cli.exe（可选第三方组件）",
            Filter = "BleHid CLI|BleHid.Cli.exe|Executable files|*.exe"
        };
        if (dialog.ShowDialog(this) == true) {
            BridgePath.Text = dialog.FileName;
            settings.BridgeExecutablePath = dialog.FileName;
            SettingsStore.Save(settings);
        }
    }
    void StartBridge_Click(object sender, RoutedEventArgs e)
    {
        try {
            if (ipadBridge.Running) { ipadBridge.RefreshStatus(); return; }
            settings.BridgeExecutablePath=BridgePath.Text.Trim(); SettingsStore.Save(settings);
            ipadBridge.Start(BridgePath.Text.Trim()); ipadBridge.RefreshStatus();
            bridgeStoppedByUser = false; bridgeWasRunning = true;
            bridgeRestartAttempts = 0; nextBridgeRestart = DateTime.MaxValue;
        }
        catch(Exception ex) { MessageBox.Show("启动失败：" + ex.Message + "\n请确认 BleHid App/CLI 已完全退出；不要修改蓝牙驱动。", "BLE HID 诊断"); }
    }
    void StopBridge_Click(object sender, RoutedEventArgs e) { bridgeStoppedByUser = true; nextBridgeRestart = DateTime.MaxValue; engine.Enabled = false; engine.Reset(); ToggleButton.Content="启动映射"; ipadBridge.Stop(); }
    void BridgeStatus_Click(object sender, RoutedEventArgs e)
    {
        if(!ipadBridge.Running) MessageBox.Show("请先启动桥接服务。");
        else ipadBridge.RefreshStatus();
    }
    void SelectFirstHost_Click(object sender, RoutedEventArgs e)
    {
        if(MessageBox.Show("只在 iPad 已配对，并且日志的 host 列表里确认第 1 项就是你的 iPad 时继续。是否选择 host 1？",
            "核对目标设备",MessageBoxButton.YesNo,MessageBoxImage.Warning) == MessageBoxResult.Yes)
            if (!ipadBridge.SelectHost(1)) MessageBox.Show("Host 命令发送失败，请检查 CLI 状态。");
    }
    void ToolSetup_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("""
            按住涂抹（Alt 模式已在 iPadOS 18.7.8 实测通过；M 模式待验证）
            在 Procreate 扳手 → 偏好设置 → 手势控制 → 涂抹，
            开启“□ + Apple Pencil”。如提示冲突，请核对其他工具手势。
            在软件将空闲按钮绑定“按住涂抹”，保存并启用。
            按住该按钮并用笔绘制，松开应回到原工具。

            临时涂抹时右上角工具图标可能仍显示原工具，请以实际笔触为准。
            临时橡皮擦：按住切到橡皮擦，松开固定切回画笔。与涂抹同时按下时橡皮擦优先。
            笔刷大小：绑定“减小笔刷”和“增大笔刷”，可按住连续调整。
            涂抹保持期间其他映射暂缓，避免组合键相互干扰。

            液化与翻转仍通过 QuickMenu 后在 iPad 点选。
            iPadOS 18 及以上选择 Option/Alt 模式；17 及以下选择 M 模式。版本范围来自官方 Modify 键说明，涂抹仍需单独验证。
            """, "工具设置与验收");
    }

    void DragProbe_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string direction) return;
        if (MessageBox.Show("请使用测试画布，先开启 Procreate 单指 Move 或 Zoom 模式，并把指针放在画布内部。测试会按住鼠标左键短距离拖动，可能留下笔迹。继续？",
            "单指画布实验", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (!ipadBridge.SendDragProbe(direction))
            MessageBox.Show("连接未就绪，或当前 CLI 不支持实验拖动。请选用 V0.1.11 内的增强 CLI。");
        else OutputStatus.Text = "已提交短距离拖动，请观察 iPad 画布实际变化";
    }

    void TestIpadKey_Click(object sender, RoutedEventArgs e)
    {
        if (!ipadBridge.ReadyForInput) { MessageBox.Show("请先检查状态、确认订阅数 > 0，并选择正确的 iPad Host。"); return; }
        if (MessageBox.Show("请先打开 iPad 的任意文字输入框并让其获得焦点。测试会发送字母 b。继续？",
            "蓝牙单键测试",MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            if(!ipadBridge.SendSingleKey("b")) MessageBox.Show("发送失败：请先启动桥接程序。");
    }
    void ViewFullLog_Click(object sender, RoutedEventArgs e)
    {
        var window = new Window
        {
            Title = "BleHid 完整日志", Owner = this, Width = 850, Height = 620,
            MinWidth = 550, MinHeight = 350, Background = System.Windows.Media.Brushes.White,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var root = new DockPanel { Margin = new Thickness(16) };
        var copy = new Button { Content = "复制全部日志", Height = 35, Width = 130, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,0,0,8) };
        var text = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"), Text = ipadBridge.RecentOutput };
        copy.Click += (_, _) => { CopyTextSafely(ipadBridge.RecentOutput); text.Text = ipadBridge.RecentOutput; };
        DockPanel.SetDock(copy, Dock.Top); root.Children.Add(copy); root.Children.Add(text);
        window.Content = root;
        window.ShowDialog();
    }
    void CopyBridgeLog_Click(object sender, RoutedEventArgs e)
    {
        CopyTextSafely(ipadBridge.RecentOutput);
        OutputStatus.Text = "完整蓝牙日志已复制";
    }
    void Window_Closing(object? sender,System.ComponentModel.CancelEventArgs e)
    {
        if (!restoringBackup && !ResolveUnsaved()) { e.Cancel = true; return; }

        engine.Enabled = false;
        engine.Reset();
        pollTimer.Stop();
        trayIcon?.Dispose();
        ipadBridge.Dispose();
    }

    void Background_Click(object sender, RoutedEventArgs e) => TryHideToTray();
    void TryHideToTray()
    {
        if (trayIcon?.Register() == true && trayIcon.HasIconLocation())
        {
            Hide();
            return;
        }
        // Keep a taskbar button when Windows cannot locate the notification icon.
        WindowState = WindowState.Minimized;
    }

    void ToggleMappingFromTray()
    {
        if (!engine.Enabled && (selectedDevice is null || currentProfile?.Target != "Windows"))
            ShowMainWindow();
        ToggleButton_Click(this, new RoutedEventArgs());
    }

    void ShowTraySettings()
    {
        ShowMainWindow();
        TrayCheckbox.BringIntoView();
        TrayCheckbox.Focus();
    }

    void ExitFromTray()
    {

        Close();
    }

    void ShowMainWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    void TrayCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (initializing || !IsLoaded) return;
        settings.MinimizeToTray = TrayCheckbox.IsChecked == true;
        SettingsStore.Save(settings);
    }
    void AutoRestartCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (initializing || !IsLoaded) return;
        bool previous = settings.AutoRestartBridge;
        settings.AutoRestartBridge = AutoRestartCheckbox.IsChecked == true;
        try { SettingsStore.Save(settings); }
        catch (Exception ex) { settings.AutoRestartBridge = previous; MessageBox.Show("保存失败：" + ex.Message); }
        if (!settings.AutoRestartBridge) nextBridgeRestart = DateTime.MaxValue;
    }

    void MaintainBridgeRestart()
    {
        var now = DateTime.UtcNow;
        if (ipadBridge.Running)
        {
            bridgeWasRunning = true;
            if (ipadBridge.ReadyForInput)
            {
                if (healthySince == DateTime.MaxValue) healthySince = now;
                if ((now-healthySince).TotalSeconds >= 30) bridgeRestartAttempts = 0;
            }
            else healthySince = DateTime.MaxValue;
            return;
        }
        healthySince = DateTime.MaxValue;
        if (bridgeWasRunning)
        {
            bridgeWasRunning = false;
            engine.Enabled = false; engine.Reset(); ToggleButton.Content = "启动映射";
            if (!bridgeStoppedByUser && settings.AutoRestartBridge && bridgeRestartAttempts < 3)
                nextBridgeRestart = now.AddSeconds(2*(bridgeRestartAttempts+1));
            OutputStatus.Text = "蓝牙服务已退出，映射已暂停";
        }
        if (bridgeStoppedByUser || !settings.AutoRestartBridge || now < nextBridgeRestart) return;
        nextBridgeRestart = DateTime.MaxValue;
        if (bridgeRestartAttempts >= 3) return;
        bridgeRestartAttempts++;
        try
        {
            ipadBridge.Start(settings.BridgeExecutablePath); ipadBridge.RefreshStatus();
            bridgeWasRunning = true;
            OutputStatus.Text = $"重启尝试 {bridgeRestartAttempts}/3；等待 iPad，映射保持暂停";
        }
        catch (Exception ex)
        {
            OutputStatus.Text = $"服务重启 {bridgeRestartAttempts}/3 失败：{ex.Message}";
            if (bridgeRestartAttempts < 3) nextBridgeRestart = now.AddSeconds(2*(bridgeRestartAttempts+1));
        }
    }

    void AutoBridgeCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (initializing || !IsLoaded) return;
        settings.AutoStartBridge = AutoBridgeCheckbox.IsChecked == true;
        SettingsStore.Save(settings);
    }
    void StartupCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (initializing || !IsLoaded) return;
        bool enabled = StartupCheckbox.IsChecked == true;
        try { SettingsStore.SetStartWithWindows(enabled); settings.StartWithWindows=enabled; SettingsStore.Save(settings); }
        catch (Exception ex) { MessageBox.Show("设置开机自启失败："+ex.Message); StartupCheckbox.IsChecked=settings.StartWithWindows; }
    }
    bool HasDirectionalBindings(string stick) => currentProfile != null &&
        new[] {"Up","Down","Left","Right"}.Any(direction =>
            currentProfile.Bindings.TryGetValue(stick+direction, out var binding) && binding.Action != "None");

    string DeviceCalibrationKey() => selectedDevice is null ? "" :
        $"{selectedDevice.Name}|{(selectedDevice.IsXInput ? "XInput" : "WinMM")}";

    static uint ReadAxis(ControllerState state, string axis, uint fallback) => axis switch
    {
        "X" => state.X, "Y" => state.Y, "Z" => state.Z, "R" => state.R,
        "U" => state.U, "V" => state.V, _ => fallback
    };

    double AxisValue(uint value, uint center)
    {
        double delta = (double)value - center;
        return Math.Clamp(delta / Math.Max(1, delta >= 0 ? (double)calibration.AxisMaximum-center : (double)center-calibration.AxisMinimum), -1, 1);
    }

    void SaveCalibration_Click(object sender, RoutedEventArgs e)
    {
        if (selectedDevice is null) { MessageBox.Show("请先连接手柄。"); return; }
        var previousLeft = calibration.LeftDeadzone;
        var previousRight = calibration.RightDeadzone;
        calibration.LeftDeadzone = LeftDeadzone.Value / 100;
        calibration.RightDeadzone = RightDeadzone.Value / 100;
        settings.Devices[DeviceCalibrationKey()] = calibration;
        try { SettingsStore.Save(settings); }
        catch (Exception ex)
        {
            calibration.LeftDeadzone = previousLeft;
            calibration.RightDeadzone = previousRight;
            MessageBox.Show("保存失败：" + ex.Message);
            return;
        }
        leftActive = rightActive = false;
        leftGuard.Reset(); rightGuard.Reset();
        engine.Reset();
        OutputStatus.Text = "摇杆设置已保存；设备按名称及输入接口匹配";
    }

    void CenterCalibration_Click(object sender, RoutedEventArgs e)
    {
        if (selectedDevice is null || !WinMmController.TryRead(selectedDevice, out var state)) return;
        engine.Enabled = false;
        engine.Reset();
        ToggleButton.Content = "启动映射";
        var previous = calibration;
        calibration = System.Text.Json.JsonSerializer.Deserialize<StickCalibration>(System.Text.Json.JsonSerializer.Serialize(previous))!;
        calibration.LeftDeadzone = LeftDeadzone.Value / 100;
        calibration.RightDeadzone = RightDeadzone.Value / 100;
        calibration.LeftX = ReadAxis(state, calibration.LeftXAxis, state.X);
        calibration.LeftY = ReadAxis(state, calibration.LeftYAxis, state.Y);
        calibration.RightX = ReadAxis(state, calibration.RightXAxis, selectedDevice.IsXInput ? state.R : state.U);
        calibration.RightY = ReadAxis(state, calibration.RightYAxis, selectedDevice.IsXInput ? state.U : state.R);

        settings.Devices[DeviceCalibrationKey()] = calibration;
        try { SettingsStore.Save(settings); }
        catch (Exception ex)
        {
            calibration = previous;
            settings.Devices[DeviceCalibrationKey()] = previous;
            MessageBox.Show("校准保存失败：" + ex.Message);
        }
        leftActive = rightActive = false;
        leftGuard.Reset(); rightGuard.Reset();
    }

    bool MappingDirty()
    {
        if (currentProfile is null) return false;
        return rows.Any(row =>
        {
            currentProfile.Bindings.TryGetValue(row.Input, out var saved);
            string action = saved?.Action ?? "None";
            return row.Action != action ||
                (row.Action == "CustomKey" && row.Shortcut != (saved?.Shortcut ?? ""));
        });
    }

    bool ResolveUnsaved()
    {
        if(!ResolveInspectorDraft())return false;
        bool mapping = MappingDirty();
        bool repeat = (int)RepeatDelay.Value != settings.RepeatDelayMs ||
            (int)RepeatInterval.Value != settings.RepeatIntervalMs;
        bool sticks = selectedDevice != null &&
            (Math.Abs(LeftDeadzone.Value / 100 - calibration.LeftDeadzone) > 0.0001 ||
             Math.Abs(RightDeadzone.Value / 100 - calibration.RightDeadzone) > 0.0001);
        if (!mapping && !repeat && !sticks) return true;
        var result = MessageBox.Show("有未保存的设置。是：保存后继续；否：放弃修改后继续；取消：留在当前操作。",
            "未保存设置", MessageBoxButton.YesNoCancel);
        if (result == MessageBoxResult.Cancel) return false;
        if (result == MessageBoxResult.No)
        {
            if (currentProfile != null)
                foreach (var row in rows)
                {
                    currentProfile.Bindings.TryGetValue(row.Input, out var saved);
                    row.Action = saved?.Action ?? "None"; row.Shortcut = saved?.Shortcut ?? "";
                }
            BindingsList.Items.Refresh();
            RepeatDelay.Value = settings.RepeatDelayMs; RepeatInterval.Value = settings.RepeatIntervalMs;
            LeftDeadzone.Value = calibration.LeftDeadzone * 100; RightDeadzone.Value = calibration.RightDeadzone * 100;
            return true;
        }
        if (mapping) SaveProfile_Click(this, new RoutedEventArgs());
        if (repeat) RepeatSettings_Click(this, new RoutedEventArgs());
        if (sticks) SaveCalibration_Click(this, new RoutedEventArgs());
        return !MappingDirty() && (int)RepeatDelay.Value == settings.RepeatDelayMs &&
            (int)RepeatInterval.Value == settings.RepeatIntervalMs &&
            Math.Abs(LeftDeadzone.Value / 100 - calibration.LeftDeadzone) < 0.0001 &&
            Math.Abs(RightDeadzone.Value / 100 - calibration.RightDeadzone) < 0.0001;
    }

    void ResetMapping_Click(object sender, RoutedEventArgs e)
    {
        if (currentProfile is null || !ResolveUnsaved()) return;
        string preset = Path.Combine(AppContext.BaseDirectory, "Defaults", currentProfile.Target + ".json");
        if (!File.Exists(preset))
        {
            MessageBox.Show("最终默认映射尚未配置。等你提供每个按键的正式默认功能后启用此入口。当前映射不会被覆盖。");
            return;
        }
        if (MessageBox.Show("将恢复当前场景的默认映射并保存。继续？", "恢复默认映射",
            MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try
        {
            var defaults = System.Text.Json.JsonSerializer.Deserialize<MappingProfile>(File.ReadAllText(preset))
                ?? throw new InvalidDataException("默认映射为空");
            if (defaults.Target != currentProfile.Target || defaults.Bindings is null)
                throw new InvalidDataException("默认映射目标不一致");
            ConfigurationBackup.Export(Path.Combine(ConfigurationBackup.Root,
                "before-reset-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip"));
            var updated = new MappingProfile { Name = currentProfile.Name, Target = currentProfile.Target,
                SchemaVersion = currentProfile.SchemaVersion, Bindings = defaults.Bindings };
            store.Save(updated); LoadSelectedProfile();
        }
        catch (Exception ex) { MessageBox.Show("恢复默认映射失败：" + ex.Message); }
    }

    void ExportConfiguration_Click(object sender, RoutedEventArgs e)
    {
        if (!ResolveUnsaved()) return;
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "配置备份|*.zip", FileName = "VirtualTourBox-config.zip" };
        if (dialog.ShowDialog(this) != true) return;
        try { ConfigurationBackup.Export(dialog.FileName); OutputStatus.Text = "已备份磁盘上的配置，请确保映射修改已保存"; }
        catch (Exception ex) { MessageBox.Show("备份失败：" + ex.Message); }
    }

    void RestoreConfiguration_Click(object sender, RoutedEventArgs e)
    {
        if (!ResolveUnsaved()) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "配置备份|*.zip" };
        if (dialog.ShowDialog(this) != true) return;
        if (MessageBox.Show("将覆盖备份中的同名场景及设置，并自动备份当前配置。备份之外的场景保留。恢复成功后程序退出，请重新打开。继续？",
            "恢复配置", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        engine.Enabled = false; engine.Reset(); ToggleButton.Content = "启动映射";
        try
        {
            string backup = ConfigurationBackup.Restore(dialog.FileName);
            restoringBackup = true;
            MessageBox.Show("恢复成功。原配置备份位于：" + backup + "。重新打开软件后生效。开机启动项请在设置中重新核对。");
            Close();
        }
        catch (Exception ex) { MessageBox.Show("恢复失败：" + ex.Message); }
    }
    void RepeatSettings_Click(object sender, RoutedEventArgs e)
    {
        int delay = (int)RepeatDelay.Value, interval = (int)RepeatInterval.Value;
        var oldDelay = settings.RepeatDelayMs;
        var oldInterval = settings.RepeatIntervalMs;
        settings.RepeatDelayMs = delay; settings.RepeatIntervalMs = interval;
        try { SettingsStore.Save(settings); }
        catch (Exception ex)
        {
            settings.RepeatDelayMs = oldDelay; settings.RepeatIntervalMs = oldInterval;
            MessageBox.Show("保存失败：" + ex.Message); return;
        }
        engine.Reset();
        engine.RepeatDelayMs = delay; engine.RepeatIntervalMs = interval;
        OutputStatus.Text = $"连发设置已保存：等待 {delay} 毫秒，间隔 {interval} 毫秒";
    }

    void AdvancedCalibration_Click(object sender, RoutedEventArgs e)
    {
        if (selectedDevice is null) { MessageBox.Show("请先连接手柄"); return; }
        engine.Enabled = false; engine.Reset(); ToggleButton.Content = "启动映射";
        var dialog = new CalibrationWindow(calibration) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;
        var previous = calibration;
        settings.Devices[DeviceCalibrationKey()] = dialog.Result;
        try { SettingsStore.Save(settings); calibration = dialog.Result; }
        catch (Exception ex)
        {
            settings.Devices[DeviceCalibrationKey()] = previous;
            MessageBox.Show("校准保存失败：" + ex.Message);
        }
        leftGuard.Reset(); rightGuard.Reset();
    }

    async void CopyTextSafely(string text)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                Clipboard.SetText(string.IsNullOrEmpty(text) ? "暂无诊断记录" : text);
                OutputStatus.Text = "诊断已复制";
                return;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                await Task.Delay(100);
            }
        }
        var dialog = new Window { Title = "剪贴板忙，请手动选择并复制", Owner = this, Width = 720, Height = 480 };
        dialog.Content = new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        dialog.ShowDialog();
    }

    void CopyDiagnostics_Click(object sender,RoutedEventArgs e)
    {
        CopyTextSafely(LastDeviceDiagnostics);

    }

}
















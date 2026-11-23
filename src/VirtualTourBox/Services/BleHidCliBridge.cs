using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VirtualTourBox.Services;

/// <summary>Runs the separately-downloaded upstream BleHid CLI in interactive mode.
/// No radio driver changes and no implicit/guessed host selection.</summary>
public sealed class BleHidCliBridge : IDisposable
{
    public string ConfirmedHostId { get; set; } = "";
    public bool AutoReconnect { get; set; }
    public bool StructuredStatus { get; private set; }
    DateTime lastStatus = DateTime.MinValue;
    string? selectedIdentity;
    bool awaitingConfirmation;
    public void MaintainConnection()
    {
        if (!Running || !StructuredStatus) return;
        SendCommand("vtbstatus");
    }
    private Process? process;
    private readonly object sync = new();
    private readonly StringBuilder output = new();
    public event Action? Changed;
    public string ExecutablePath { get; private set; } = "";
    public bool Running => process is { HasExited: false };
    public bool ComboSupported { get; private set; }
    public bool HostConfirmed { get; private set; }
    public int? SelectedHostNumber { get; private set; }
    public int? KeyboardSubscriptions { get; private set; }
    public string RecentOutput { get { lock (sync) return output.ToString(); } }

    public void Start(string exe)
    {
        if (Running) throw new InvalidOperationException("桥接服务已经运行，无需重复启动。");
        if (!File.Exists(exe) || !string.Equals(Path.GetFileName(exe), "BleHid.Cli.exe", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("请先选择从官方发行包解压的 BleHid.Cli.exe。", exe);
        // Refuse to start a second BLE provider (including an independently launched App/CLI).
        if (Process.GetProcessesByName("BleHid.Cli").Length != 0 || Process.GetProcessesByName("BleHid.App").Length != 0)
            throw new InvalidOperationException("检测到 BleHid App/CLI 已在运行。请从系统托盘退出 App，或关闭先前的 CLI，然后重试。");
        process?.Dispose();
        process = null;
        ExecutablePath = exe;
        StructuredStatus = File.Exists(Path.Combine(Path.GetDirectoryName(exe)!, "VirtualTourBox.status-enabled"));
        ComboSupported = File.Exists(Path.Combine(Path.GetDirectoryName(exe)!, "VirtualTourBox.combo-enabled"));
        HostConfirmed = false;
        SelectedHostNumber = null;
        KeyboardSubscriptions = null;
        lock (sync) output.Clear();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var codePage = System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
        Encoding encoding;
        try { encoding = Encoding.GetEncoding(codePage); }
        catch (ArgumentException) { encoding = Encoding.UTF8; }
        Append($"日志编码：{encoding.EncodingName} ({encoding.CodePage})");
        var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exe, WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = encoding, StandardErrorEncoding = encoding
            }, EnableRaisingEvents = true
        };
        p.OutputDataReceived += (_, e) => Append(e.Data);
        p.ErrorDataReceived += (_, e) => Append(e.Data);
        p.Exited += (_, _) => { KeyboardSubscriptions = null; HostConfirmed = false; SelectedHostNumber = null; Append("桥接进程已经退出。"); Changed?.Invoke(); };
        if (!p.Start()) throw new InvalidOperationException("无法启动 CLI 进程。");
        process = p;
        p.BeginOutputReadLine(); p.BeginErrorReadLine();
        Append(ComboSupported ? "已检测到增强版 CLI 标记：允许尝试 Cmd 组合键。" : "官方 CLI：仅支持单键；Cmd 撤销和重做将被禁用。");
        Append("已启动交互式 CLI。请先检查 host 和 status，再确认你的 iPad 编号。");
        Changed?.Invoke();
    }

    private void Append(string? line)
    {
        if (line is null) return;
        lock (sync)
        {
            output.AppendLine(line);
            // Keep a useful rolling log; UI shows the last part, full view allows copy.
            if (output.Length > 100_000) output.Remove(0, output.Length - 80_000);
            int stateIndex = line.IndexOf("VTBSTATE ", StringComparison.Ordinal);
            if (stateIndex >= 0)
            {
                try
                {
                    using var doc = JsonDocument.Parse(line[(stateIndex + 9)..]);
                    var root = doc.RootElement;
                    selectedIdentity = root.GetProperty("selected").GetString();
                    KeyboardSubscriptions = root.GetProperty("keyboard").GetInt32();
                    lastStatus = DateTime.UtcNow;
                    var hosts = root.GetProperty("hosts").EnumerateArray().Select(x => x.GetString()).ToList();
                    if (awaitingConfirmation && SelectedHostNumber is int expected && expected > 0 && expected <= hosts.Count && hosts[expected-1] == selectedIdentity && selectedIdentity != null)
                    {
                        ConfirmedHostId = selectedIdentity;
                        awaitingConfirmation = false;
                    }
                    HostConfirmed = selectedIdentity == ConfirmedHostId &&
                        hosts.Contains(ConfirmedHostId) && KeyboardSubscriptions > 0;
                    if (AutoReconnect && !HostConfirmed && !string.IsNullOrEmpty(ConfirmedHostId) &&
                        hosts.Contains(ConfirmedHostId))
                        SendCommand("vtbselect " + ConfirmedHostId);
                }
                catch (JsonException) { HostConfirmed = false; }
            }
            var match = Regex.Match(line, @"(?:keyboard\s+subscribers?|keyboard\s+subscriptions?|键盘订阅(?:数|者)?)[^\d]{0,15}(\d+)", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int count)) KeyboardSubscriptions = count;
        }
        Changed?.Invoke();
    }
    public bool SendCommand(string command)
    {
        if (!Running || process is null || command.IndexOfAny(['\r','\n']) >= 0) return false;
        if (command.StartsWith("vtbhold")) Append("发送：" + command);
        try { process.StandardInput.WriteLine(command); process.StandardInput.Flush(); return true; }
        catch (Exception ex) { Append("命令发送失败：" + ex.Message); return false; }
    }
    public void RefreshStatus()
    {
        KeyboardSubscriptions = null; // do not display stale subscription values as current
        SendCommand("host");
        SendCommand("status");
        if (StructuredStatus) SendCommand("vtbstatus");
    }
    public bool SelectHost(int hostNumber)
    {
        HostConfirmed = false;
        SelectedHostNumber = null;
        if (hostNumber < 1 || !SendCommand("host " + hostNumber)) return false;
        // Confirmation means the user verified which host number corresponds to their iPad;
        // it does not prove the CLI selected it or it has an active subscription.
        HostConfirmed = true;
        awaitingConfirmation = StructuredStatus;
        SelectedHostNumber = hostNumber;
        if (StructuredStatus) SendCommand("vtbstatus");
        Append($"用户已确认目标 Host {hostNumber}（仍须检查键盘订阅及实际输入）。");
        return true;
    }
    public bool ReadyForInput => Running && HostConfirmed && KeyboardSubscriptions is > 0 && (!StructuredStatus || (DateTime.UtcNow-lastStatus).TotalSeconds < 4);
    public bool SendSingleKey(string text)
    {
        if (!ReadyForInput) return false;
        if (text.Equals("OemOpenBrackets", StringComparison.OrdinalIgnoreCase)) return SendCommand("type [");
        if (text.Equals("OemCloseBrackets", StringComparison.OrdinalIgnoreCase)) return SendCommand("type ]");
        if (text.Length == 1 && char.IsAsciiLetterOrDigit(text[0])) return SendCommand("type " + text);
        string? key = text.ToLowerInvariant() switch
        {
            "enter" or "return" => "enter", "space" => "space", "tab" => "tab",
            "escape" or "esc" => "esc",
            "oemopenbrackets" => "leftbracket", "oemclosebrackets" => "rightbracket", "up" or "down" or "left" or "right" => text.ToLowerInvariant(),
            _ => null
        };
        return key is not null && SendCommand("key " + key);
    }
    public bool SetSmudge(bool pressed, bool legacy = false)
    {
        if (!pressed) return SendCommand("vtbhold off");
        if (!ReadyForInput || !File.Exists(Path.Combine(Path.GetDirectoryName(ExecutablePath)!, "VirtualTourBox.hold-enabled")))
            return false;
        Append("涂抹保持 CLI：" + ExecutablePath);
        return SendCommand(legacy ? "vtbhold m" : "vtbhold alt");
    }
    public bool SendDragProbe(string direction)
    {
        if (!ReadyForInput || !File.Exists(Path.Combine(Path.GetDirectoryName(ExecutablePath)!, "VirtualTourBox.drag-enabled")))
            return false;
        if (direction is not ("left" or "right" or "up" or "down")) return false;
        return SendCommand("vtbdrag " + direction);
    }
    public bool SendCombo(string combo)
    {
        if (!ReadyForInput || !ComboSupported || combo is not ("gui+z" or "gui+shift+z")) return false;
        return SendCommand("combo " + combo);
    }
    public void Stop()
    {
        var p = process;
        if (p is null) return;
        try
        {
            if (!p.HasExited)
            {
                SendCommand("quit");
                if (!p.WaitForExit(2000)) p.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) { Append("停止失败：" + ex.Message); }
        finally
        {
            process = null; HostConfirmed = false; SelectedHostNumber = null; KeyboardSubscriptions = null;
            p.Dispose(); Changed?.Invoke();
        }
    }
    public void Dispose() => Stop();
}





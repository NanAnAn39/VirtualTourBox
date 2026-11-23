using System.Runtime.InteropServices;
using System.Windows.Input;
namespace VirtualTourBox.Services;

public static class KeyboardOutput
{
    const uint InputKeyboard = 1;
    const uint KeyUp = 0x0002;
    const ushort VkControl=0x11, VkShift=0x10, VkAlt=0x12, VkWin=0x5B;
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public INPUTUNION U; }
    [StructLayout(LayoutKind.Explicit)] struct INPUTUNION { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public HARDWAREINPUT hi; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public UIntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx,dy; public uint mouseData,dwFlags,time; public UIntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct HARDWAREINPUT { public uint uMsg; public ushort wParamL,wParamH; }
    [DllImport("user32.dll", SetLastError=true)] static extern uint SendInput(uint cInputs, INPUT[] pInputs, int cbSize);

    static INPUT Make(ushort key, bool up) => new() { type=InputKeyboard, U=new INPUTUNION {ki=new KEYBDINPUT {wVk=key,dwFlags=up?KeyUp:0}} };
    // Validate recorded shortcuts with the same conversion used for SendInput.
    public static bool IsValidShortcut(string value) => TryParseShortcut(value, out _);

    static bool TryParseShortcut(string value, out List<ushort> keys)
    {
        keys = new List<ushort>();
        if (string.IsNullOrWhiteSpace(value)) return false;
        string[] parts = value.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Any(string.IsNullOrEmpty)) return false;
        foreach (string part in parts)
        {
            ushort? key = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => VkControl,
                "SHIFT" => VkShift,
                "ALT" => VkAlt,
                "WIN" or "WINDOWS" => VkWin,
                _ => Enum.TryParse<Key>(part, true, out var wpfKey)
                    ? (ushort)KeyInterop.VirtualKeyFromKey(wpfKey)
                    : null
            };
            if (key is null || key == 0) return false;
            keys.Add(key.Value);
        }
        return keys.Count > 0;
    }

    public static string SendShortcut(string value)
    {
        if (!TryParseShortcut(value, out var keys))
            return "快捷键格式无效";

        var inputs = keys.Select(k => Make(k, false))
            .Concat(keys.AsEnumerable().Reverse().Select(k => Make(k, true))).ToArray();
        var size = Marshal.SizeOf<INPUT>();
        var inserted = (int)SendInput((uint)inputs.Length, inputs, size);
        var error = Marshal.GetLastPInvokeError();
        if (inserted == inputs.Length)
            return $"已注入 {inserted}/{inputs.Length} 个事件";

        var pending = new List<ushort>();
        foreach (var input in inputs.Take(inserted))
        {
            if (input.U.ki.dwFlags == KeyUp)
                pending.Remove(input.U.ki.wVk);
            else
                pending.Add(input.U.ki.wVk);
        }
        pending.Reverse();
        var releases = pending.Select(k => Make(k, true)).ToArray();
        var released = releases.Length == 0 ? 0 : SendInput((uint)releases.Length, releases, size);
        return $"注入失败 {inserted}/{inputs.Length}，Win32={error}，补发释放 {released}/{releases.Length}";
    }
}

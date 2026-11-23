using System.Runtime.InteropServices;
namespace VirtualTourBox.Input;

public sealed record ControllerDevice(uint Id, string Name, bool IsXInput = false)
{
    public override string ToString() => IsXInput ? $"{Name}（XInput 插槽 {Id}）" : $"{Name}（WinMM ID {Id}）";
}
public sealed record ControllerState(uint Buttons, int Pov, uint X, uint Y, uint Z, uint R, uint U, uint V)
{
    public bool Pressed(int joyCplButtonNumber) => joyCplButtonNumber is >= 1 and <= 32 && (Buttons & (1u << (joyCplButtonNumber-1))) != 0;
}

public static class WinMmController
{
    const int JoyReturnAll = 0xFF;
    const uint JoyPovCentered = 0xFFFF;
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    struct JOYCAPS
    {
        public ushort wMid, wPid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string szPname;
        public uint wXmin,wXmax,wYmin,wYmax,wZmin,wZmax,wNumButtons,wPeriod,wRmin,wRmax,wUmin,wUmax,wVmin,wVmax,wCaps,wMaxAxes,wNumAxes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string szRegKey;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=260)] public string szOEMVxD;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct JOYINFOEX
    {
        public uint dwSize, dwFlags, dwXpos, dwYpos, dwZpos, dwRpos, dwUpos, dwVpos, dwButtons, dwButtonNumber, dwPOV, dwReserved1, dwReserved2;
    }
    [DllImport("winmm.dll", CharSet=CharSet.Unicode)] static extern uint joyGetDevCapsW(UIntPtr id, out JOYCAPS caps, uint size);
    [DllImport("winmm.dll")] static extern uint joyGetPosEx(uint id, ref JOYINFOEX info);
    [DllImport("winmm.dll")] static extern uint joyGetNumDevs();

    // Return a diagnostic record instead of discarding devices on scan errors.
    // When XInput works, don't enumerate WinMM: this matches the verified V0.1.2
    // behavior on the user's wired L1807 and avoids a problematic second enumeration.
    public static (List<ControllerDevice> Devices, string Diagnostics) DiscoverDetailed()
    {
        var found = new List<ControllerDevice>();
        var log = new List<string>();
        for (uint slot = 0; slot < 4; slot++)
        {
            try
            {
                bool connected = XInputController.TryRead(slot, out _);
                log.Add(XInputController.Probe(slot));
                if (connected) found.Add(new ControllerDevice(slot, "Xbox 兼容手柄", true));
            }
            catch (Exception ex)
            {
                log.Add($"XInput slot {slot}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        if (found.Count > 0)
        {
            log.Add("优先使用 XInput；已跳过 WinMM 重复枚举。");
            return (found, string.Join(Environment.NewLine, log));
        }
        try
        {
            uint slots = Math.Min(joyGetNumDevs(), 16);
            log.Add($"WinMM reported {slots} slot(s)");
            for (uint id = 0; id < slots; id++)
            {
                try
                {
                    uint capsResult = joyGetDevCapsW((UIntPtr)id, out var caps, (uint)Marshal.SizeOf<JOYCAPS>());
                    if (capsResult != 0) continue;
                    bool readable = TryRead(id, out _);
                    log.Add($"WinMM ID {id}: {caps.szPname}, readable={readable}");
                    if (readable) found.Add(new ControllerDevice(id, string.IsNullOrWhiteSpace(caps.szPname) ? "游戏手柄" : caps.szPname));
                }
                catch (Exception ex) { log.Add($"WinMM ID {id}: {ex.GetType().Name}: {ex.Message}"); }
            }
        }
        catch (Exception ex) { log.Add($"WinMM scan: {ex.GetType().Name}: {ex.Message}"); }
        return (found, string.Join(Environment.NewLine, log));
    }
    public static List<ControllerDevice> Discover() => DiscoverDetailed().Devices;

    public static bool TryRead(ControllerDevice device, out ControllerState state) =>
        device.IsXInput ? XInputController.TryRead(device.Id, out state) : TryRead(device.Id, out state);
    public static bool TryRead(uint id, out ControllerState state)
    {
        var info = new JOYINFOEX { dwSize=(uint)Marshal.SizeOf<JOYINFOEX>(), dwFlags=JoyReturnAll };
        bool ok = joyGetPosEx(id, ref info)==0;
        state = new ControllerState(info.dwButtons, info.dwPOV==JoyPovCentered ? -1 : (int)info.dwPOV, info.dwXpos,info.dwYpos,info.dwZpos,info.dwRpos,info.dwUpos,info.dwVpos);
        return ok;
    }
}

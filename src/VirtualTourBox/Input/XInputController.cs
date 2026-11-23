using System.Runtime.InteropServices;

namespace VirtualTourBox.Input;

// Read XInput independently of WinMM. Native error codes are exposed for diagnostics.
public static class XInputController
{
    [StructLayout(LayoutKind.Sequential)]
    struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger, bRightTrigger;
        public short sThumbLX, sThumbLY, sThumbRX, sThumbRY;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }
    [DllImport("xinput1_4.dll", EntryPoint="XInputGetState")]
    static extern uint GetState14(uint slot, out XINPUT_STATE state);
    [DllImport("xinput9_1_0.dll", EntryPoint="XInputGetState")]
    static extern uint GetState910(uint slot, out XINPUT_STATE state);
    [DllImport("xinput1_3.dll", EntryPoint="XInputGetState")]
    static extern uint GetState13(uint slot, out XINPUT_STATE state);

    static uint Query(uint slot, out XINPUT_STATE state, out string dll)
    {
        state = default;
        dll = "none";
        // If a DLL exists, trust its result. Falling through on ERROR_DEVICE_NOT_CONNECTED
        // to another version would not normally discover another controller.
        try { dll = "xinput1_4.dll"; return GetState14(slot, out state); }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        try { dll = "xinput9_1_0.dll"; return GetState910(slot, out state); }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        try { dll = "xinput1_3.dll"; return GetState13(slot, out state); }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        throw new DllNotFoundException("找不到可用的 XInput DLL");
    }

    public static string Probe(uint slot)
    {
        try
        {
            uint code = Query(slot, out _, out var dll);
            return $"XInput slot {slot}: {(code == 0 ? "CONNECTED" : $"error {code}")} ({dll})";
        }
        catch (Exception e) { return $"XInput slot {slot}: {e.GetType().Name}: {e.Message}"; }
    }

    public static bool TryRead(uint slot, out ControllerState state)
    {
        state = new ControllerState(0,-1,32767,32767,32767,32767,32767,32767);
        if (slot > 3) return false;
        XINPUT_STATE s;
        try { if (Query(slot, out s, out _) != 0) return false; }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        var p = s.Gamepad;
        uint buttons = 0;
        void Put(int n, ushort mask) { if ((p.wButtons & mask) != 0) buttons |= 1u << (n-1); }
        Put(1,0x1000); Put(2,0x2000); Put(3,0x4000); Put(4,0x8000);
        Put(5,0x0100); Put(6,0x0200); Put(7,0x0020); Put(8,0x0010);
        Put(9,0x0040); Put(10,0x0080);
        bool up=(p.wButtons&0x0001)!=0, down=(p.wButtons&0x0002)!=0;
        bool left=(p.wButtons&0x0004)!=0, right=(p.wButtons&0x0008)!=0;
        int pov = up ? (right?4500:left?31500:0) : down ? (right?13500:left?22500:18000) : right?9000:left?27000:-1;
        static uint Normalize(short v) => (uint)((int)v + 32768);
        int z = p.bLeftTrigger * 257;
        state=new ControllerState(buttons,pov,Normalize(p.sThumbLX),Normalize((short)~p.sThumbLY),
            (uint)z,Normalize(p.sThumbRX),Normalize((short)~p.sThumbRY),(uint)(p.bRightTrigger * 257));
        return true;
    }
}

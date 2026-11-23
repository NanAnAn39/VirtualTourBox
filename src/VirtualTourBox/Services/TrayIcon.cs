using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace VirtualTourBox.Services;

internal sealed class TrayIcon : IDisposable
{
    const int CallbackMessage = 0x8001;
    const int LeftDoubleClick = 0x0203;
    const int RightButtonUp = 0x0205;
    const uint Add = 0;
    const uint Delete = 2;
    const uint MessageIconTip = 7;

    readonly HwndSource source;
    readonly Window owner;
    readonly Action show;
    readonly System.Drawing.Icon icon;
    bool disposed;
    readonly ContextMenu menu = new() { Placement = PlacementMode.MousePoint };
    readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    bool registered;

    public TrayIcon(Window owner, Action show, Action toggle, Action settings, Action exit)
    {
        this.owner = owner;
        this.show = show;
        using var iconStream = typeof(TrayIcon).Assembly.GetManifestResourceStream("VirtualTourBox.Tray.ico");
        icon = iconStream is null
            ? (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone()
            : new System.Drawing.Icon(iconStream);
        var handle = new WindowInteropHelper(owner).EnsureHandle();
        source = HwndSource.FromHwnd(handle);
        source.AddHook(HandleMessage);
        AddMenuItem("显示主窗口", show);
        AddMenuItem("暂停／恢复映射", toggle);
        AddMenuItem("打开设置", settings);
        AddMenuItem("彻底退出", exit);
    }

    void AddMenuItem(string label, Action action)
    {
        var item = new MenuItem { Header = label };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    NotifyIconData CreateData() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        Window = source.Handle,
        Id = 1,
        Flags = MessageIconTip,
        Callback = CallbackMessage,
        Icon = icon.Handle,
        Tip = "Virtual TourBox",
        Info = "",
        InfoTitle = ""
    };

    public bool Register()
    {
        if (disposed) return false;
        if (registered) return true;
        var data = CreateData();
        registered = Shell_NotifyIcon(Add, ref data);
        return registered;
    }

    public bool HasIconLocation()
    {
        if (!registered) return false;
        var id = new NotifyIconIdentifier
        {
            Size = (uint)Marshal.SizeOf<NotifyIconIdentifier>(),
            Window = source.Handle,
            Id = 1
        };
        return Shell_NotifyIconGetRect(ref id, out var rect) == 0 &&
            rect.Right > rect.Left && rect.Bottom > rect.Top;
    }

    IntPtr HandleMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == taskbarCreated && registered)
        {
            registered = false;
            owner.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!disposed && (!Register() || !HasIconLocation()) && !owner.IsVisible) show();
            }));
        }
        if (message == CallbackMessage)
        {
            if (lParam.ToInt64() == LeftDoubleClick)
                owner.Dispatcher.BeginInvoke(show);
            if (lParam.ToInt64() == RightButtonUp)
            {
                owner.Dispatcher.BeginInvoke(new Action(() =>
                {
                    SetForegroundWindow(hwnd);
                    menu.IsOpen = true;
                }));
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        menu.IsOpen = false;
        if (registered)
        {
            var data = CreateData();
            Shell_NotifyIcon(Delete, ref data);
            registered = false;
        }
        source.RemoveHook(HandleMessage);
        icon.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id, Flags, Callback;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid ItemGuid;
        public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NotifyIconIdentifier
    {
        public uint Size;
        public IntPtr Window;
        public uint Id;
        public Guid ItemGuid;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IconRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("shell32.dll")]
    static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out IconRect rect);

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetForegroundWindow(IntPtr hwnd);
}


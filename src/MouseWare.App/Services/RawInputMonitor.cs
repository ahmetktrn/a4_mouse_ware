using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using MouseWare.App.Native;

namespace MouseWare.App.Services;

/// <summary>
/// Listens to Raw Input from every mouse, keyboard and other HID device. Used only on the
/// button test page: it shows whether an extra button (like the small grey one) sends
/// anything at all to Windows, even data the normal mouse hook never sees.
/// </summary>
internal sealed class RawInputMonitor : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<IntPtr, string> _deviceNames = new();
    private List<NativeMethods.RAWINPUTDEVICE> _registered = new();
    private static readonly uint HeaderSize = (uint)Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>();

    /// <summary>Raised on the UI thread with one line of text.</summary>
    public event Action<string>? Observed;

    public RawInputMonitor(HwndSource source)
    {
        _source = source;
    }

    public bool IsRunning { get; private set; }

    public void Start()
    {
        if (IsRunning)
            return;

        var usages = new HashSet<(ushort Page, ushort Usage)> { (0x01, 0x02), (0x01, 0x06) };
        foreach (var device in EnumerateHidDevices())
        {
            // Generic desktop, consumer (media keys) and vendor-defined pages only. Touch, pen and
            // sensor devices are left alone so the window's own touch input is not disturbed.
            if (device.Page is 0x01 or 0x0C or >= 0xFF00)
                usages.Add((device.Page, device.Usage));
        }

        _registered = usages.Select(u => new NativeMethods.RAWINPUTDEVICE
        {
            usUsagePage = u.Page,
            usUsage = u.Usage,
            dwFlags = NativeMethods.RIDEV_INPUTSINK,
            hwndTarget = _source.Handle,
        }).ToList();

        // Register one by one: a single unsupported collection must not stop the others.
        var accepted = new List<NativeMethods.RAWINPUTDEVICE>();
        foreach (var device in _registered)
        {
            if (NativeMethods.RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>()))
                accepted.Add(device);
        }
        _registered = accepted;

        _source.AddHook(WndProc);
        IsRunning = true;
    }

    public void Stop()
    {
        if (!IsRunning)
            return;
        _source.RemoveHook(WndProc);
        foreach (var device in _registered)
        {
            var remove = device;
            remove.dwFlags = NativeMethods.RIDEV_REMOVE;
            remove.hwndTarget = IntPtr.Zero;
            NativeMethods.RegisterRawInputDevices(new[] { remove }, 1, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>());
        }
        _registered.Clear();
        IsRunning = false;
    }

    public void Dispose() => Stop();

    /// <summary>Lists the HID collections Windows exposes (vendor, product and usage).</summary>
    public IReadOnlyList<string> DescribeDevices()
    {
        return EnumerateHidDevices(includeMiceAndKeyboards: true)
            .Select(d => $"{d.Kind,-8} VID {d.Vendor:X4} PID {d.Product:X4}  page 0x{d.Page:X4} usage 0x{d.Usage:X4}")
            .Distinct()
            .ToList();
    }

    private sealed record DeviceInfo(IntPtr Handle, string Kind, uint Vendor, uint Product, ushort Page, ushort Usage);

    private IEnumerable<DeviceInfo> EnumerateHidDevices(bool includeMiceAndKeyboards = false)
    {
        uint count = 0;
        uint itemSize = (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICELIST>();
        NativeMethods.GetRawInputDeviceList(null, ref count, itemSize);
        if (count == 0)
            yield break;
        var list = new NativeMethods.RAWINPUTDEVICELIST[count];
        if (NativeMethods.GetRawInputDeviceList(list, ref count, itemSize) == uint.MaxValue)
            yield break;

        foreach (var item in list.Take((int)count))
        {
            if (item.dwType != NativeMethods.RIM_TYPEHID && !includeMiceAndKeyboards)
                continue;
            var info = ReadDeviceInfo(item.hDevice);
            if (info != null)
                yield return info;
        }
    }

    private static DeviceInfo? ReadDeviceInfo(IntPtr device)
    {
        const int InfoSize = 32; // sizeof(RID_DEVICE_INFO)
        var buffer = Marshal.AllocHGlobal(InfoSize);
        try
        {
            Marshal.WriteInt32(buffer, 0, InfoSize);
            uint size = InfoSize;
            if (NativeMethods.GetRawInputDeviceInfoW(device, NativeMethods.RIDI_DEVICEINFO, buffer, ref size) == uint.MaxValue)
                return null;
            uint type = (uint)Marshal.ReadInt32(buffer, 4);
            return type switch
            {
                NativeMethods.RIM_TYPEHID => new DeviceInfo(device, "HID",
                    (uint)Marshal.ReadInt32(buffer, 8), (uint)Marshal.ReadInt32(buffer, 12),
                    (ushort)Marshal.ReadInt16(buffer, 20), (ushort)Marshal.ReadInt16(buffer, 22)),
                NativeMethods.RIM_TYPEMOUSE => new DeviceInfo(device, "Mouse", 0, 0, 0x01, 0x02),
                _ => new DeviceInfo(device, "Keyboard", 0, 0, 0x01, 0x06),
            };
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private string DeviceName(IntPtr device)
    {
        if (_deviceNames.TryGetValue(device, out var name))
            return name;

        uint size = 0;
        NativeMethods.GetRawInputDeviceInfoW(device, NativeMethods.RIDI_DEVICENAME, IntPtr.Zero, ref size);
        name = "?";
        if (size > 0)
        {
            var buffer = Marshal.AllocHGlobal((int)size * 2);
            try
            {
                if (NativeMethods.GetRawInputDeviceInfoW(device, NativeMethods.RIDI_DEVICENAME, buffer, ref size) != uint.MaxValue)
                    name = ShortDeviceName(Marshal.PtrToStringUni(buffer) ?? "?");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        _deviceNames[device] = name;
        return name;
    }

    /// <summary>"\\?\HID#VID_09DA&amp;PID_xxxx&amp;Col02#..." → "VID_09DA&amp;PID_xxxx&amp;Col02".</summary>
    private static string ShortDeviceName(string path)
    {
        var parts = path.Split('#');
        return parts.Length > 1 ? parts[1] : path;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_INPUT)
            return IntPtr.Zero;

        try
        {
            var line = Parse(lParam);
            if (line != null)
                Observed?.Invoke(line);
        }
        catch (Exception ex)
        {
            Log.Error("Raw input parse failed", ex);
        }
        return IntPtr.Zero;
    }

    private string? Parse(IntPtr handle)
    {
        uint size = 0;
        NativeMethods.GetRawInputData(handle, NativeMethods.RID_INPUT, IntPtr.Zero, ref size, HeaderSize);
        if (size == 0)
            return null;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (NativeMethods.GetRawInputData(handle, NativeMethods.RID_INPUT, buffer, ref size, HeaderSize) == uint.MaxValue)
                return null;

            var header = Marshal.PtrToStructure<NativeMethods.RAWINPUTHEADER>(buffer);
            var data = buffer + (int)HeaderSize;
            var device = DeviceName(header.hDevice);

            switch (header.dwType)
            {
                case NativeMethods.RIM_TYPEMOUSE:
                {
                    ushort flags = (ushort)Marshal.ReadInt16(data, 4);
                    if (flags == 0)
                        return null; // movement only
                    return $"[raw mouse] {DescribeMouseFlags(flags, (short)Marshal.ReadInt16(data, 6))}  ({device})";
                }
                case NativeMethods.RIM_TYPEKEYBOARD:
                {
                    ushort makeCode = (ushort)Marshal.ReadInt16(data, 0);
                    ushort keyFlags = (ushort)Marshal.ReadInt16(data, 2);
                    ushort vkey = (ushort)Marshal.ReadInt16(data, 6);
                    string state = (keyFlags & 1) == 0 ? "down" : "up";
                    return $"[raw key] {Core.KeyNames.Name(vkey)} (VK 0x{vkey:X2}, scan 0x{makeCode:X2}) {state}  ({device})";
                }
                case NativeMethods.RIM_TYPEHID:
                {
                    int reportSize = Marshal.ReadInt32(data, 0);
                    int count = Marshal.ReadInt32(data, 4);
                    int total = Math.Min(reportSize * count, 64);
                    var bytes = new byte[total];
                    Marshal.Copy(data + 8, bytes, 0, total);
                    return $"[raw HID] {BitConverter.ToString(bytes).Replace('-', ' ')}  ({device})";
                }
                default:
                    return null;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string DescribeMouseFlags(ushort flags, short data)
    {
        var parts = new List<string>();
        void Check(ushort bit, string text)
        {
            if ((flags & bit) != 0)
                parts.Add(text);
        }
        Check(0x0001, "Left down");
        Check(0x0002, "Left up");
        Check(0x0004, "Right down");
        Check(0x0008, "Right up");
        Check(0x0010, "Middle down");
        Check(0x0020, "Middle up");
        Check(0x0040, "Button4 down");
        Check(0x0080, "Button4 up");
        Check(0x0100, "Button5 down");
        Check(0x0200, "Button5 up");
        if ((flags & 0x0400) != 0)
            parts.Add("Wheel " + data.ToString(CultureInfo.InvariantCulture));
        if ((flags & 0x0800) != 0)
            parts.Add("HWheel " + data.ToString(CultureInfo.InvariantCulture));
        if (parts.Count == 0)
            parts.Add("flags 0x" + flags.ToString("X4", CultureInfo.InvariantCulture));
        return string.Join(", ", parts);
    }
}

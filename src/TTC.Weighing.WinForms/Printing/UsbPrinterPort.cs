using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TTC.Weighing.WinForms.Printing;

/// <summary>
/// Ghi thẳng dữ liệu vào máy in USB qua giao diện thiết bị USBPRINT của Windows (usbprint.sys),
/// không cần driver máy in / hàng chờ in, không cần quyền Administrator.
/// Windows tự nạp usbprint.sys cho mọi máy in USB chuẩn (lớp USB Printer) khi cắm dây.
/// </summary>
public static class UsbPrinterPort
{
    /// <summary>GUID_DEVINTERFACE_USBPRINT.</summary>
    private static readonly Guid UsbPrintInterface = new("28d78fad-5a12-11d1-ae5b-0000f803a8c2");

    private const int DIGCF_PRESENT = 0x02;
    private const int DIGCF_DEVICEINTERFACE = 0x10;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x1;
    private const uint FILE_SHARE_WRITE = 0x2;
    private const uint OPEN_EXISTING = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData,
        ref Guid interfaceClassGuid, int memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetDeviceInterfaceDetailW")]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr detail, int detailSize, out int requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string fileName, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    /// <summary>Đường dẫn thiết bị của các máy in USB đang cắm (dạng \\?\usb#vid_0483&amp;pid_5743#...).</summary>
    public static List<string> FindDevices()
    {
        var result = new List<string>();
        var guid = UsbPrintInterface;
        var set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return result;
        try
        {
            for (var i = 0; ; i++)
            {
                var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data)) break;

                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out var size, IntPtr.Zero);
                if (size <= 0) continue;
                var buffer = Marshal.AllocHGlobal(size);
                try
                {
                    // cbSize của SP_DEVICE_INTERFACE_DETAIL_DATA_W: 8 trên 64-bit, 6 trên 32-bit.
                    Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(set, ref data, buffer, size, out _, IntPtr.Zero)
                        && Marshal.PtrToStringUni(buffer + 4) is { Length: > 0 } path)
                        result.Add(path);
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }

    /// <summary>
    /// Chọn máy in: thiết bị đầu tiên có đường dẫn chứa <paramref name="filter"/> (ví dụ "vid_0483"),
    /// không lọc thì lấy thiết bị đầu tiên. Null nếu không có máy in USB nào.
    /// </summary>
    public static string? Find(string? filter)
    {
        var devices = FindDevices();
        return string.IsNullOrWhiteSpace(filter)
            ? devices.FirstOrDefault()
            : devices.FirstOrDefault(d => d.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Ghi toàn bộ dữ liệu vào máy in. Lỗi ghi (máy tắt, rút dây...) ném <see cref="Win32Exception"/> / <see cref="IOException"/>.</summary>
    public static void Write(string devicePath, byte[] data)
    {
        using var handle = CreateFile(devicePath, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        using var stream = new FileStream(handle, FileAccess.Write, 1);
        // Ghi từng khối nhỏ: một số máy in nhiệt có bộ đệm USB nhỏ.
        const int chunk = 4096;
        for (var offset = 0; offset < data.Length; offset += chunk)
            stream.Write(data, offset, Math.Min(chunk, data.Length - offset));
        stream.Flush();
    }
}

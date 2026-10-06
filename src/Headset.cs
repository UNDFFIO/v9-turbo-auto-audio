using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace V9AutoAudio
{
    public sealed class HeadsetState
    {
        public string State;
        public int? Battery;
        public int? StatusCode;
        public string ChargeStatus;
        public string Error;
        public int Timeouts;
    }

    internal static class Headset
    {
        internal static HeadsetState Read()
        {
            try
            {
                List<string> paths = NativeHid.Paths();
                if (paths.Count == 0) return new HeadsetState { State = "receiver_missing" };
                if (paths.Count != 1) return new HeadsetState { State = "error", Error = "检测到多个 Turbo+ 接收器" };
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    byte[] response = NativeHid.Query(paths[0]);
                    if (response != null)
                    {
                        int status = response[3];
                        if (status == 26) return new HeadsetState { State = "offline", StatusCode = status };
                        if (response[2] > 100) return new HeadsetState { State = "error", Error = "耳机电量回应超出有效范围" };
                        return new HeadsetState { State = "online", Battery = response[2], StatusCode = status,
                            ChargeStatus = status == 2 ? "discharging" : status == 3 ? "charging" : status == 4 ? "full" : "unknown" };
                    }
                    if (attempt < 2) Thread.Sleep(10);
                }
                return new HeadsetState { State = "offline", Timeouts = 3 };
            }
            catch (Exception ex) { return new HeadsetState { State = "error", Error = ex.Message }; }
        }
    }

    internal static class NativeHid
    {
        private static readonly IntPtr Invalid = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)] private struct InterfaceData
        {
            public uint Size;
            public Guid ClassGuid;
            public uint Flags;
            public IntPtr Reserved;
        }
        [StructLayout(LayoutKind.Sequential)] private struct Attributes
        {
            public uint Size;
            public ushort Vendor;
            public ushort Product;
            public ushort Version;
        }
        [StructLayout(LayoutKind.Sequential)] private struct Overlap
        {
            public UIntPtr Internal;
            public UIntPtr InternalHigh;
            public uint Offset;
            public uint OffsetHigh;
            public IntPtr Event;
        }

        [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetAttributes(IntPtr handle, ref Attributes attributes);
        [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetPreparsedData(IntPtr handle, out IntPtr preparsed);
        [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_FreePreparsedData(IntPtr preparsed);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr preparsed, IntPtr caps);
        [DllImport("hid.dll")] private static extern int HidP_GetValueCaps(int type, IntPtr caps, ref ushort length, IntPtr preparsed);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr info, IntPtr device, ref Guid guid, uint index, ref InterfaceData data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr info, ref InterfaceData data, IntPtr detail, uint length, out uint required, IntPtr device);
        [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr info);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateEvent(IntPtr security, bool manualReset, bool initialState, string name);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadFile(IntPtr handle, IntPtr buffer, uint bytes, out uint transferred, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteFile(IntPtr handle, IntPtr buffer, uint bytes, out uint transferred, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetOverlappedResult(IntPtr handle, IntPtr overlapped, out uint transferred, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CancelIoEx(IntPtr handle, IntPtr overlapped);

        private static ushort Word(IntPtr pointer, int offset) { return unchecked((ushort)Marshal.ReadInt16(pointer, offset)); }

        private static bool Matches(IntPtr handle)
        {
            var attributes = new Attributes { Size = (uint)Marshal.SizeOf(typeof(Attributes)) };
            if (!HidD_GetAttributes(handle, ref attributes) || attributes.Vendor != 0x3837 || attributes.Product != 0x600A) return false;
            IntPtr preparsed;
            if (!HidD_GetPreparsedData(handle, out preparsed)) return false;
            IntPtr caps = Marshal.AllocHGlobal(64);
            IntPtr values = Marshal.AllocHGlobal(72 * 32);
            try
            {
                if (HidP_GetCaps(preparsed, caps) < 0 || Word(caps, 0) != 1 || Word(caps, 2) != 0xFF90 || Word(caps, 4) != 64 || Word(caps, 6) != 64 || Word(caps, 8) != 0) return false;
                ushort count = 32;
                if (HidP_GetValueCaps(0, values, ref count, preparsed) < 0) return false;
                for (int i = 0; i < count; i++) if (Marshal.ReadByte(values, 72 * i + 2) == 0x55) return true;
                return false;
            }
            finally { Marshal.FreeHGlobal(values); Marshal.FreeHGlobal(caps); HidD_FreePreparsedData(preparsed); }
        }

        internal static List<string> Paths()
        {
            Guid guid;
            HidD_GetHidGuid(out guid);
            IntPtr info = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, 0x12);
            if (info == Invalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            var paths = new List<string>();
            try
            {
                for (uint index = 0; ; index++)
                {
                    var data = new InterfaceData { Size = (uint)Marshal.SizeOf(typeof(InterfaceData)) };
                    if (!SetupDiEnumDeviceInterfaces(info, IntPtr.Zero, ref guid, index, ref data))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error == 259) break;
                        throw new Win32Exception(error);
                    }
                    uint size;
                    SetupDiGetDeviceInterfaceDetail(info, ref data, IntPtr.Zero, 0, out size, IntPtr.Zero);
                    if (size < 8) continue;
                    IntPtr detail = Marshal.AllocHGlobal((int)size);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        uint required;
                        if (!SetupDiGetDeviceInterfaceDetail(info, ref data, detail, size, out required, IntPtr.Zero)) continue;
                        string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                        if (path == null || path.IndexOf("vid_3837&pid_600a", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        IntPtr handle = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                        if (handle == Invalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                        try { if (Matches(handle)) paths.Add(path); }
                        finally { CloseHandle(handle); }
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(info); }
            return paths;
        }

        private sealed class Io : IDisposable
        {
            private readonly IntPtr file;
            private IntPtr buffer;
            private IntPtr operation;
            private IntPtr signal;
            private bool pending;
            internal Io(IntPtr handle, byte[] bytes)
            {
                file = handle;
                try
                {
                    buffer = Marshal.AllocHGlobal(64);
                    Marshal.Copy(bytes ?? new byte[64], 0, buffer, 64);
                    signal = CreateEvent(IntPtr.Zero, true, false, null);
                    if (signal == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                    operation = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Overlap)));
                    Marshal.StructureToPtr(new Overlap { Event = signal }, operation, false);
                }
                catch { Dispose(); throw; }
            }
            internal void Begin(bool write)
            {
                uint count;
                bool ok = write ? WriteFile(file, buffer, 64, out count, operation) : ReadFile(file, buffer, 64, out count, operation);
                int error = ok ? 0 : Marshal.GetLastWin32Error();
                if (!ok && error != 997) throw new Win32Exception(error);
                pending = true;
            }
            internal byte[] Complete(int timeout)
            {
                uint wait = WaitForSingleObject(signal, (uint)Math.Max(0, timeout));
                bool timedOut = wait == 258;
                if (timedOut) CancelIoEx(file, operation);
                else if (wait != 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                uint count;
                bool ok = GetOverlappedResult(file, operation, out count, true);
                pending = false;
                if (!ok)
                {
                    int error = Marshal.GetLastWin32Error();
                    if (timedOut && error == 995) return null;
                    throw new Win32Exception(error);
                }
                if (count != 64) throw new IOException("状态报文长度不匹配");
                byte[] bytes = new byte[64];
                Marshal.Copy(buffer, bytes, 0, 64);
                return bytes;
            }
            public void Dispose()
            {
                if (pending)
                {
                    CancelIoEx(file, operation);
                    uint count;
                    GetOverlappedResult(file, operation, out count, true);
                    pending = false;
                }
                if (operation != IntPtr.Zero) { Marshal.FreeHGlobal(operation); operation = IntPtr.Zero; }
                if (buffer != IntPtr.Zero) { Marshal.FreeHGlobal(buffer); buffer = IntPtr.Zero; }
                if (signal != IntPtr.Zero) { CloseHandle(signal); signal = IntPtr.Zero; }
            }
        }

        // 与官方 HUB 相同的唯一电量查询；不发送配置或固件命令。
        internal static byte[] Query(string path)
        {
            IntPtr file = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
            if (file == Invalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                if (!Matches(file)) throw new IOException("接收器状态接口不匹配");
                var request = new byte[64];
                request[0] = 0x55; request[1] = 0x65; request[2] = 0x01;
                var watch = Stopwatch.StartNew();
                using (var read = new Io(file, null))
                {
                    read.Begin(false);
                    using (var write = new Io(file, request))
                    {
                        write.Begin(true);
                        if (write.Complete(1000) == null) throw new IOException("状态查询发送超时");
                    }
                    byte[] bytes = read.Complete((int)Math.Max(0, 1000 - watch.ElapsedMilliseconds));
                    if (bytes == null) return null;
                    if (bytes[0] == 0x55 && bytes[1] == 0x65) return bytes;
                }
                // 忽略同一 HID 通道上不属于电量查询的通知，保留总超时上限。
                while (watch.ElapsedMilliseconds < 1000)
                {
                    using (var read = new Io(file, null))
                    {
                        read.Begin(false);
                        byte[] bytes = read.Complete((int)(1000 - watch.ElapsedMilliseconds));
                        if (bytes == null) return null;
                        if (bytes[0] == 0x55 && bytes[1] == 0x65) return bytes;
                    }
                }
                return null;
            }
            finally { CloseHandle(file); }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace V9AutoAudio
{
    public sealed class OutputDevice
    {
        public string Id;
        public string Name;
        public override string ToString() { return Name; }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class DeviceEnumeratorClass { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IntPtr collection);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr activation, out IntPtr result);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    internal class PolicyClass { }

    // IPolicyConfig 的前十个槽位不调用；第十一个负责设置默认输出。
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicy
    {
        [PreserveSig] int Reserved0();
        [PreserveSig] int Reserved1();
        [PreserveSig] int Reserved2();
        [PreserveSig] int Reserved3();
        [PreserveSig] int Reserved4();
        [PreserveSig] int Reserved5();
        [PreserveSig] int Reserved6();
        [PreserveSig] int Reserved7();
        [PreserveSig] int Reserved8();
        [PreserveSig] int Reserved9();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
    }

    internal static class Audio
    {
        private const string EndpointRoot = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render";
        internal static void Release(object value)
        {
            if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }

        internal static bool Active(IDeviceEnumerator enumerator, string id)
        {
            IDevice device = null;
            try
            {
                uint state;
                return enumerator.GetDevice(id, out device) >= 0 && device.GetState(out state) >= 0 && state == 1;
            }
            finally { Release(device); }
        }

        internal static List<OutputDevice> Devices()
        {
            var list = new List<OutputDevice>();
            var enumerator = (IDeviceEnumerator)new DeviceEnumeratorClass();
            try
            {
                using (var root = Registry.LocalMachine.OpenSubKey(EndpointRoot))
                {
                    if (root == null) return list;
                    foreach (string key in root.GetSubKeyNames())
                    {
                        string id = "{0.0.0.00000000}." + key;
                        if (!Active(enumerator, id)) continue;
                        using (var properties = root.OpenSubKey(key + @"\Properties"))
                        {
                            if (properties == null) continue;
                            string description = Convert.ToString(properties.GetValue("{a45c254e-df1c-4efd-8020-67d146a850e0},2"));
                            string hardware = Convert.ToString(properties.GetValue("{b3f8fa53-0004-438e-9003-51a46e139bfc},6"));
                            string friendly = Convert.ToString(properties.GetValue("{a45c254e-df1c-4efd-8020-67d146a850e0},14"));
                            list.Add(new OutputDevice { Id = id, Name = String.IsNullOrWhiteSpace(friendly) ? description + " (" + hardware + ")" : friendly });
                        }
                    }
                }
            }
            finally { Release(enumerator); }
            return list;
        }

        internal static string[] Defaults()
        {
            var values = new string[3];
            var enumerator = (IDeviceEnumerator)new DeviceEnumeratorClass();
            try
            {
                for (int role = 0; role < 3; role++)
                {
                    IDevice device = null;
                    try
                    {
                        string id;
                        if (enumerator.GetDefaultAudioEndpoint(0, role, out device) >= 0 && device.GetId(out id) >= 0) values[role] = id;
                    }
                    finally { Release(device); }
                }
            }
            finally { Release(enumerator); }
            return values;
        }

        internal static OutputDevice Find(string id, string preferredName)
        {
            List<OutputDevice> devices = Devices();
            foreach (var device in devices) if (device.Id == id) return device;
            var matches = devices.FindAll(d => d.Name.IndexOf(preferredName, StringComparison.OrdinalIgnoreCase) >= 0);
            return matches.Count == 1 ? matches[0] : null;
        }

        internal static void Switch(string id)
        {
            if (String.IsNullOrEmpty(id)) throw new InvalidOperationException("未找到目标输出设备");
            string[] before = Defaults();
            var changed = new List<int>();
            var enumerator = (IDeviceEnumerator)new DeviceEnumeratorClass();
            var policy = (IPolicy)new PolicyClass();
            try
            {
                if (!Active(enumerator, id)) throw new InvalidOperationException("目标声音设备当前不可用");
                for (int role = 0; role < 3; role++)
                {
                    if (before[role] == id) continue;
                    Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id, role));
                    changed.Add(role);
                }
                string[] after = Defaults();
                for (int role = 0; role < 3; role++)
                    if (after[role] != id) throw new InvalidOperationException("Windows 尚未应用目标输出");
            }
            catch
            {
                foreach (int role in changed)
                    if (!String.IsNullOrEmpty(before[role])) policy.SetDefaultEndpoint(before[role], role);
                throw;
            }
            finally { Release(policy); Release(enumerator); }
        }
    }
}

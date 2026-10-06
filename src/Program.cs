using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace V9AutoAudio
{
    internal static class Program
    {
        internal static readonly string InstanceName = @"Local\V9TurboAutoAudio." + WindowsIdentity.GetCurrent().User.Value;
        internal static readonly string StopName = InstanceName + ".Stop";
        internal static object Diagnostics(Settings settings)
        {
            return new { checked_at = DateTimeOffset.Now.ToString("o"), headset = Headset.Read(),
                outputs = Audio.Devices(), defaults = Audio.Defaults(), settings = settings,
                startup_enabled = Store.StartupEnabled(), executable = Store.ExePath };
        }

        [STAThread]
        private static int Main(string[] args)
        {
            string report = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(Store.DirectoryPath, "诊断结果.json");
            try
            {
                if (args.Length > 0 && args[0] == "--stop")
                {
                    EventWaitHandle stop;
                    if (EventWaitHandle.TryOpenExisting(StopName, out stop)) using (stop) stop.Set();
                    return 0;
                }
                Settings settings = Store.Load();
                if (args.Length > 0)
                {
                    if (args[0] == "--diagnose") { Store.Atomic(report, Diagnostics(settings)); return 0; }
                    if (args[0] == "--install-startup" || args[0] == "--remove-startup")
                    {
                        bool install = args[0] == "--install-startup";
                        Store.Startup(install);
                        bool enabled = Store.StartupEnabled();
                        Store.Atomic(report, new { startup_enabled = enabled, command = Store.StartupCommand });
                        if (enabled != install) return 1;
                        return 0;
                    }
                    if (args[0] == "--switch-headset" || args[0] == "--switch-speakers")
                    {
                        bool headphones = args[0] == "--switch-headset";
                        OutputDevice target = Audio.Find(headphones ? settings.HeadsetId : settings.SpeakersId, headphones ? settings.HeadsetName : settings.SpeakersName);
                        if (target == null) throw new InvalidOperationException("未找到目标输出设备");
                        string[] before = Audio.Defaults();
                        Audio.Switch(target.Id);
                        Store.Atomic(report, new { target = target, before = before, after = Audio.Defaults(), verified = true });
                        return 0;
                    }
                    throw new ArgumentException("不支持的程序参数");
                }
                bool created;
                using (var instance = new Mutex(true, InstanceName, out created))
                {
                    if (!created) return 0;
                    try
                    {
                        Application.EnableVisualStyles();
                        Application.SetCompatibleTextRenderingDefault(false);
                        Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { Store.Log("界面异常：" + e.Exception.Message); };
                        using (var context = new TrayContext(settings)) Application.Run(context);
                    }
                    finally { instance.ReleaseMutex(); }
                }
                return 0;
            }
            catch (Exception ex)
            {
                Store.Log("程序错误：" + ex);
                try { Store.Atomic(report, new { error = ex.ToString(), checked_at = DateTimeOffset.Now.ToString("o") }); } catch { }
                return 1;
            }
        }
    }
}

using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace V9AutoAudio
{
    public sealed class Settings
    {
        public bool Enabled = true;
        public string HeadsetId = "";
        public string SpeakersId = "";
        public string HeadsetName = "MCHOSE V9 Turbo+";
        public string SpeakersName = "Realtek(R) Audio";
    }

    internal static class Store
    {
        internal const string StartupName = "V9TurboAutoAudio";
        internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal static readonly string DirectoryPath = AppDomain.CurrentDomain.BaseDirectory;
        internal static readonly string ConfigPath = Path.Combine(DirectoryPath, "设置.json");
        internal static readonly string StatusPath = Path.Combine(DirectoryPath, "运行状态.json");
        internal static readonly string LogPath = Path.Combine(DirectoryPath, "运行日志.txt");
        internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static readonly object LogLock = new object();

        internal static string ExePath { get { return Assembly.GetExecutingAssembly().Location; } }
        internal static string StartupCommand { get { return "\"" + ExePath + "\""; } }

        internal static Settings Load()
        {
            if (!File.Exists(ConfigPath))
            {
                var settings = new Settings();
                var headphones = Audio.Find("", settings.HeadsetName);
                var speakers = Audio.Find("", settings.SpeakersName);
                if (headphones != null) { settings.HeadsetId = headphones.Id; settings.HeadsetName = headphones.Name; }
                if (speakers != null) { settings.SpeakersId = speakers.Id; settings.SpeakersName = speakers.Name; }
                Save(settings);
                return settings;
            }
            var result = Json.Deserialize<Settings>(File.ReadAllText(ConfigPath, Encoding.UTF8));
            if (result == null || String.IsNullOrWhiteSpace(result.HeadsetName) || String.IsNullOrWhiteSpace(result.SpeakersName))
                throw new InvalidDataException("设置文件无效，请检查耳机和扬声器选项");
            if (!String.IsNullOrEmpty(result.HeadsetId) && result.HeadsetId == result.SpeakersId)
                throw new InvalidDataException("耳机和扬声器不能选择同一个设备");
            return result;
        }

        internal static void Atomic(string path, object value)
        {
            string temp = path + "." + System.Diagnostics.Process.GetCurrentProcess().Id + ".tmp";
            File.WriteAllText(temp, Json.Serialize(value), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        internal static void Save(Settings settings) { Atomic(ConfigPath, settings); }

        internal static bool StartupEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
            {
                return key != null && String.Equals(Convert.ToString(key.GetValue(StartupName)), StartupCommand, StringComparison.OrdinalIgnoreCase);
            }
        }

        internal static void Startup(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) key.SetValue(StartupName, StartupCommand, RegistryValueKind.String);
                else
                {
                    string value = Convert.ToString(key.GetValue(StartupName));
                    if (String.Equals(value, StartupCommand, StringComparison.OrdinalIgnoreCase)) key.DeleteValue(StartupName, false);
                }
            }
        }

        internal static void Log(string message)
        {
            try
            {
                lock (LogLock)
                {
                    if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 512 * 1024)
                    {
                        File.Copy(LogPath, LogPath + ".1", true);
                        File.WriteAllText(LogPath, "", Encoding.UTF8);
                    }
                    File.AppendAllText(LogPath, DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz") + "  " + message + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch { }
        }
    }
}

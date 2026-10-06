using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace V9AutoAudio
{
    internal sealed class TrayContext : ApplicationContext
    {
        private Settings settings;
        private readonly NotifyIcon tray;
        private readonly ToolStripMenuItem stateMenu;
        private readonly ToolStripMenuItem automaticMenu;
        private readonly ToolStripMenuItem startupMenu;
        private readonly System.Windows.Forms.Timer timer;
        private readonly BackgroundWorker worker;
        private readonly Control dispatcher;
        private readonly EventWaitHandle stop;
        private readonly Icon onlineIcon;
        private readonly Icon offlineIcon;
        private readonly Icon pausedIcon;
        private HeadsetState last = new HeadsetState { State = "starting" };
        private string lastState;
        private string lastApplied;
        private string lastSwitch;
        private string switchError;
        private bool force = true;
        private bool closing;
        private bool disposed;
        private DateTime nextSwitchTry;
        private DateTime lastPublished;
        private SettingsForm settingsWindow;

        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

        private static Icon MakeIcon(Color color)
        {
            using (var bitmap = new Bitmap(32, 32))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var background = new SolidBrush(color))
            using (var pen = new Pen(Color.White, 3F))
            using (var white = new SolidBrush(Color.White))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                graphics.FillEllipse(background, 1, 1, 30, 30);
                graphics.DrawArc(pen, 8, 8, 16, 18, 180, 180);
                graphics.FillRectangle(white, 7, 16, 5, 9);
                graphics.FillRectangle(white, 20, 16, 5, 9);
                IntPtr handle = bitmap.GetHicon();
                try { using (var temporary = Icon.FromHandle(handle)) return (Icon)temporary.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }

        internal TrayContext(Settings initial)
        {
            settings = initial;
            dispatcher = new Control();
            IntPtr dispatchHandle = dispatcher.Handle;
            stop = new EventWaitHandle(false, EventResetMode.ManualReset, Program.StopName);
            stop.Reset();
            onlineIcon = MakeIcon(Color.FromArgb(45, 127, 249));
            offlineIcon = MakeIcon(Color.FromArgb(115, 127, 147));
            pausedIcon = MakeIcon(Color.FromArgb(218, 153, 43));
            stateMenu = new ToolStripMenuItem("正在读取耳机状态…") { Enabled = false };
            automaticMenu = new ToolStripMenuItem("启用自动切换") { Checked = settings.Enabled };
            startupMenu = new ToolStripMenuItem("登录后自动启动") { Checked = Store.StartupEnabled() };
            automaticMenu.Click += delegate
            {
                bool previous = settings.Enabled;
                try
                {
                    settings.Enabled = !settings.Enabled;
                    Store.Save(settings);
                    force = true; nextSwitchTry = DateTime.MinValue;
                    Store.Log(settings.Enabled ? "自动切换已启用" : "自动切换已暂停");
                    RefreshTray(); Publish(true);
                }
                catch (Exception ex) { settings.Enabled = previous; ShowError(ex); }
            };
            startupMenu.Click += delegate
            {
                try { Store.Startup(!Store.StartupEnabled()); startupMenu.Checked = Store.StartupEnabled(); Publish(true); }
                catch (Exception ex) { ShowError(ex); }
            };
            var menu = new ContextMenuStrip();
            menu.Items.Add(stateMenu);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(automaticMenu);
            menu.Items.Add(startupMenu);
            menu.Items.Add("设置…", null, delegate { OpenSettings(); });
            menu.Items.Add("查看运行日志", null, delegate { try { Process.Start(new ProcessStartInfo(Store.LogPath) { UseShellExecute = true }); } catch (Exception ex) { ShowError(ex); } });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { ExitThread(); });
            menu.Opening += delegate { automaticMenu.Checked = settings.Enabled; startupMenu.Checked = Store.StartupEnabled(); };
            tray = new NotifyIcon { Icon = offlineIcon, Text = "V9 自动声音切换", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { OpenSettings(); };
            worker = new BackgroundWorker();
            worker.DoWork += delegate(object sender, DoWorkEventArgs e) { e.Result = Headset.Read(); };
            worker.RunWorkerCompleted += Completed;
            timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += delegate
            {
                if (stop.WaitOne(0)) { ExitThread(); return; }
                if (!worker.IsBusy) worker.RunWorkerAsync();
            };
            Store.Log("程序已启动；PID=" + Process.GetCurrentProcess().Id + "；自动切换=" + settings.Enabled);
            Publish(true);
            timer.Start();
        }

        private void Completed(object sender, RunWorkerCompletedEventArgs e)
        {
            if (closing) return;
            last = e.Error == null ? (HeadsetState)e.Result : new HeadsetState { State = "error", Error = e.Error.Message };
            bool changed = lastState != last.State;
            if (changed)
            {
                Store.Log("耳机状态：" + last.State + (last.Battery.HasValue ? "；电量=" + last.Battery.Value + "%" : "") + (last.Error != null ? "；" + last.Error : ""));
                lastState = last.State;
                nextSwitchTry = DateTime.MinValue;
            }
            ApplyOutput();
            RefreshTray();
            Publish(changed);
        }

        private void ApplyOutput()
        {
            if (!settings.Enabled || DateTime.UtcNow < nextSwitchTry) return;
            bool headphones = last.State == "online";
            if (!headphones && last.State != "offline" && last.State != "receiver_missing") return;
            string route = headphones ? "headset" : "speakers";
            if (!force && lastApplied == route) return;
            try
            {
                OutputDevice target = Audio.Find(headphones ? settings.HeadsetId : settings.SpeakersId, headphones ? settings.HeadsetName : settings.SpeakersName);
                if (target == null) throw new InvalidOperationException("等待目标输出设备连接：" + (headphones ? settings.HeadsetName : settings.SpeakersName));
                Audio.Switch(target.Id);
                bool changedId = headphones ? settings.HeadsetId != target.Id : settings.SpeakersId != target.Id;
                if (headphones) settings.HeadsetId = target.Id; else settings.SpeakersId = target.Id;
                if (changedId) Store.Save(settings);
                lastApplied = route; force = false; switchError = null;
                lastSwitch = DateTimeOffset.Now.ToString("o") + " " + target.Name;
                Store.Log("默认声音输出已验证：" + target.Name + "；常规、媒体、通信三个输出角色一致");
                Publish(true);
            }
            catch (Exception ex)
            {
                switchError = ex.Message;
                nextSwitchTry = DateTime.UtcNow.AddSeconds(5);
                Store.Log("暂未完成声音切换：" + ex.Message);
            }
        }

        private string Description()
        {
            if (last.State == "online") return "耳机在线" + (last.Battery.HasValue ? " · " + last.Battery.Value + "%" : "");
            if (last.State == "offline") return "耳机离线或休眠";
            if (last.State == "receiver_missing") return "接收器未连接";
            if (last.State == "error") return "正在重试状态读取";
            return "正在读取耳机状态";
        }

        private void RefreshTray()
        {
            automaticMenu.Checked = settings.Enabled;
            stateMenu.Text = Description() + (settings.Enabled ? "" : " · 自动切换已暂停");
            string text = "V9 自动切换 | " + stateMenu.Text;
            tray.Text = text.Length < 63 ? text : text.Substring(0, 62);
            tray.Icon = !settings.Enabled || last.State == "error" || switchError != null ? pausedIcon : last.State == "online" ? onlineIcon : offlineIcon;
        }

        private void Publish(bool immediate)
        {
            if (!immediate && (DateTime.UtcNow - lastPublished).TotalSeconds < 10) return;
            try
            {
                Store.Atomic(Store.StatusPath, new { updated_at = DateTimeOffset.Now.ToString("o"), pid = Process.GetCurrentProcess().Id,
                    is_running = !closing, enabled = settings.Enabled, startup_enabled = Store.StartupEnabled(), state = last.State,
                    battery_percent = last.Battery, status_code = last.StatusCode, timeout_count = last.Timeouts,
                    selected_headset = settings.HeadsetId, selected_speakers = settings.SpeakersId,
                    current_defaults = Audio.Defaults(), last_switch = lastSwitch, error = last.Error ?? switchError });
                lastPublished = DateTime.UtcNow;
            }
            catch (Exception ex) { Store.Log("无法保存状态：" + ex.Message); }
        }

        private void OpenSettings()
        {
            if (settingsWindow != null) { settingsWindow.Activate(); return; }
            try
            {
                using (settingsWindow = new SettingsForm(settings))
                {
                    if (settingsWindow.ShowDialog() != DialogResult.OK) return;
                    Settings replacement = settingsWindow.Result;
                    Store.Save(replacement);
                    Store.Startup(settingsWindow.StartAtLogin);
                    settings = replacement;
                    force = true; nextSwitchTry = DateTime.MinValue;
                    ApplyOutput(); RefreshTray(); Publish(true);
                }
            }
            catch (Exception ex) { ShowError(ex); }
            finally { settingsWindow = null; }
        }

        private static void ShowError(Exception ex)
        {
            Store.Log(ex.Message);
            MessageBox.Show(ex.Message, "V9 自动声音切换", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        protected override void ExitThreadCore()
        {
            if (closing) return;
            closing = true;
            timer.Stop();
            tray.Visible = false;
            Publish(true);
            Store.Log("程序已退出");
            base.ExitThreadCore();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                timer.Dispose();
                worker.RunWorkerCompleted -= Completed;
                worker.Dispose();
                if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Dispose();
                tray.Dispose();
                dispatcher.Dispose(); stop.Dispose();
                onlineIcon.Dispose(); offlineIcon.Dispose(); pausedIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

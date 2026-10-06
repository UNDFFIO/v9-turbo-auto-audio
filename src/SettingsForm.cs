using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace V9AutoAudio
{
    internal sealed class SettingsForm : Form
    {
        internal Settings Result;
        internal bool StartAtLogin;
        private readonly CheckBox automatic;
        private readonly CheckBox startup;
        private readonly ComboBox headphones;
        private readonly ComboBox speakers;

        internal SettingsForm(Settings current)
        {
            Text = "V9 自动声音切换";
            ClientSize = new Size(420, 294);
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(247, 249, 252);
            var title = new Label { Text = "拿起用耳机，放回用扬声器", Location = new Point(22, 18), AutoSize = true, Font = new Font(Font.FontFamily, 12F, FontStyle.Bold) };
            automatic = new CheckBox { Text = "启用自动声音切换", Checked = current.Enabled, Location = new Point(24, 57), AutoSize = true };
            startup = new CheckBox { Text = "登录 Windows 后自动运行", Checked = Store.StartupEnabled(), Location = new Point(24, 84), AutoSize = true };
            var headphoneLabel = new Label { Text = "耳机连接时", Location = new Point(24, 121), AutoSize = true };
            var speakerLabel = new Label { Text = "耳机断开时", Location = new Point(24, 168), AutoSize = true };
            headphones = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(125, 116), Size = new Size(269, 28) };
            speakers = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(125, 163), Size = new Size(269, 28) };
            List<OutputDevice> devices = Audio.Devices();
            foreach (var device in devices)
            {
                headphones.Items.Add(device);
                speakers.Items.Add(device);
                if (device.Id == current.HeadsetId) headphones.SelectedItem = device;
                if (device.Id == current.SpeakersId) speakers.SelectedItem = device;
            }
            var note = new Label { Text = "切换时保留各设备的音量。应用输出选择“系统默认”即可跟随。", Location = new Point(24, 211), Size = new Size(372, 35), ForeColor = Color.FromArgb(90, 99, 115) };
            var save = new Button { Text = "保存", Location = new Point(307, 250), Size = new Size(87, 29) };
            var cancel = new Button { Text = "取消", Location = new Point(210, 250), Size = new Size(87, 29), DialogResult = DialogResult.Cancel };
            save.Click += delegate
            {
                var first = headphones.SelectedItem as OutputDevice;
                var second = speakers.SelectedItem as OutputDevice;
                if (first == null || second == null || first.Id == second.Id)
                {
                    MessageBox.Show(this, "请选择两个不同的声音输出设备。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Result = new Settings { Enabled = automatic.Checked, HeadsetId = first.Id, SpeakersId = second.Id, HeadsetName = first.Name, SpeakersName = second.Name };
                StartAtLogin = startup.Checked;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.AddRange(new Control[] { title, automatic, startup, headphoneLabel, speakerLabel, headphones, speakers, note, save, cancel });
            AcceptButton = save; CancelButton = cancel;
        }
    }
}

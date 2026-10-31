using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QuotaFloat {
    sealed partial class Widget {
        int[] shortcuts = { (int)(Keys.Control | Keys.Alt | Keys.Q), 0, 0, (int)(Keys.Control | Keys.Alt | Keys.R), (int)(Keys.Control | Keys.Alt | Keys.S) };
        static readonly string[] shortcutLabels = { "显示 / 隐藏浮窗", "显示浮窗", "隐藏浮窗", "立即刷新额度", "打开控制面板" };
        Form controlPanel;
        string hotkeyStatus = "";
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr handle, int id);
        internal static bool ValidShortcut(int value) {
            if (value == 0) return true;
            var keys = (Keys)value; var key = keys & Keys.KeyCode;
            return (keys & (Keys.Control | Keys.Alt)) != 0 && (value & ~((int)Keys.KeyCode | (int)Keys.Control | (int)Keys.Alt | (int)Keys.Shift)) == 0 &&
                key >= Keys.Back && key <= Keys.OemClear && key != Keys.ControlKey && key != Keys.ShiftKey && key != Keys.Menu && key != Keys.LWin && key != Keys.RWin;
        }
        static string ShortcutText(int value) { return value == 0 ? "未设置" : new KeysConverter().ConvertToString((Keys)value); }
        void UnregisterShortcuts() { if (IsHandleCreated) for (int i = 0; i < shortcuts.Length; i++) UnregisterHotKey(Handle, 6100 + i); }
        string RegisterShortcuts() {
            string errors = "";
            for (int i = 0; i < shortcuts.Length; i++) {
                var keys = (Keys)shortcuts[i]; if (keys == Keys.None) continue;
                uint modifiers = 0x4000; // MOD_NOREPEAT
                if ((keys & Keys.Control) != 0) modifiers |= 2;
                if ((keys & Keys.Alt) != 0) modifiers |= 1;
                if ((keys & Keys.Shift) != 0) modifiers |= 4;
                if (!RegisterHotKey(Handle, 6100 + i, modifiers, (uint)(keys & Keys.KeyCode))) errors += shortcutLabels[i] + "（" + ShortcutText(shortcuts[i]) + "）被占用或不可用。\r\n";
            }
            return errors;
        }
        protected override void OnShown(EventArgs e) {
            base.OnShown(e); hotkeyStatus = RegisterShortcuts();
            if (hotkeyStatus.Length > 0) { tray.ShowBalloonTip(5000, "部分快捷键不可用", "右键打开控制面板可修改。", ToolTipIcon.Warning); }
        }
        protected override void OnHandleDestroyed(EventArgs e) { UnregisterShortcuts(); base.OnHandleDestroyed(e); }
        protected override void WndProc(ref Message m) {
            if (m.Msg == 0x0312 && !closing) {
                switch (m.WParam.ToInt32() - 6100) {
                    case 0: if (Visible) Hide(); else { Show(); Activate(); } break;
                    case 1: Show(); Activate(); break;
                    case 2: Hide(); break;
                    case 3: var ignored = RefreshQuota(); break;
                    case 4: OpenControlPanel(); break;
                }
            }
            base.WndProc(ref m);
        }
        void OpenControlPanel() {
            if (controlPanel != null && !controlPanel.IsDisposed) { controlPanel.Show(); controlPanel.Activate(); return; }
            var panel = new Form { Text = "额度浮窗 · 控制面板", ClientSize = new Size(540, 480), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = true, StartPosition = FormStartPosition.CenterScreen, Font = new Font("Microsoft YaHei UI", 10), BackColor = Color.FromArgb(248, 248, 250), AutoScaleMode = AutoScaleMode.Dpi, Icon = appIcon };
            controlPanel = panel;
            panel.FormClosed += delegate { controlPanel = null; };
            panel.Controls.Add(new Label { Text = "浮窗与全局快捷键", Location = new Point(24, 18), AutoSize = true, Font = new Font(panel.Font.FontFamily, 15, FontStyle.Bold) });
            panel.Controls.Add(new Label { Text = "点击输入框后按组合键；须包含 Ctrl 或 Alt。清除表示禁用。", Location = new Point(24, 56), AutoSize = true });
            var boxes = new TextBox[shortcuts.Length];
            for (int i = 0; i < shortcuts.Length; i++) {
                int index = i; int y = 92 + i * 42;
                panel.Controls.Add(new Label { Text = shortcutLabels[i], Location = new Point(24, y + 4), AutoSize = true });
                var box = new TextBox { ReadOnly = true, Location = new Point(184, y), Width = 238, Text = ShortcutText(shortcuts[i]), Tag = shortcuts[i], ShortcutsEnabled = false };
                box.KeyDown += delegate(object sender, KeyEventArgs e) { e.SuppressKeyPress = true; e.Handled = true; if (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back) { box.Tag = 0; box.Text = "未设置"; } else if (ValidShortcut((int)e.KeyData)) { box.Tag = (int)e.KeyData; box.Text = ShortcutText((int)e.KeyData); } };
                boxes[i] = box; panel.Controls.Add(box);
                var clear = new Button { Text = "清除", Location = new Point(434, y - 1), Size = new Size(76, 30) };
                clear.Click += delegate { boxes[index].Tag = 0; boxes[index].Text = "未设置"; }; panel.Controls.Add(clear);
            }
            var top = new CheckBox { Text = "浮窗置顶", Checked = TopMost, Location = new Point(24, 308), AutoSize = true }; panel.Controls.Add(top);
            var rate = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(330, 306), Width = 180 }; rate.Items.AddRange(new object[] { "每 30 秒刷新", "每 60 秒刷新", "每 120 秒刷新" }); rate.SelectedIndex = Array.IndexOf(new[] { 30, 60, 120 }, interval); panel.Controls.Add(rate);
            var status = new Label { Text = hotkeyStatus.Length == 0 ? "快捷键在其他应用中也生效；关闭面板不退出浮窗。" : hotkeyStatus, Location = new Point(24, 346), Size = new Size(486, 62), ForeColor = Color.DimGray }; panel.Controls.Add(status);
            var show = new Button { Text = "显示 / 隐藏", Location = new Point(24, 425), Size = new Size(114, 34) }; show.Click += delegate { if (Visible) Hide(); else Show(); }; panel.Controls.Add(show);
            var reset = new Button { Text = "重置位置", Location = new Point(148, 425), Size = new Size(100, 34) }; reset.Click += delegate { DefaultPosition(); SaveSettings(); }; panel.Controls.Add(reset);
            var save = new Button { Text = "保存设置", Location = new Point(394, 425), Size = new Size(116, 34) }; panel.Controls.Add(save);
            save.Click += delegate {
                var next = boxes.Select(b => (int)b.Tag).ToArray();
                if (next.Where(v => v != 0).Distinct().Count() != next.Count(v => v != 0)) { status.Text = "快捷键重复，请为不同功能设置不同组合键。"; return; }
                var old = shortcuts; UnregisterShortcuts(); shortcuts = next; var error = RegisterShortcuts();
                if (error.Length > 0) { UnregisterShortcuts(); shortcuts = old; hotkeyStatus = RegisterShortcuts(); status.Text = "未保存：" + error; return; }
                hotkeyStatus = ""; TopMost = top.Checked; interval = new[] { 30, 60, 120 }[rate.SelectedIndex]; refreshTimer.Interval = interval * 1000;
                foreach (var item in menu.Items.OfType<ToolStripMenuItem>()) if (item.Text == "刷新间隔") UpdateRates(item);
                SaveSettings(); status.Text = "已保存，快捷键立即生效。";
            };
            FormClosed += delegate { if (!panel.IsDisposed) panel.Close(); };
            panel.Show(); panel.Activate();
        }
    }
}

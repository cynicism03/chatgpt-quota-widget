using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace QuotaFloat {
    static class Program {
        [STAThread] static int Main(string[] args) {
            if (args.Contains("--stop")) {
                try { using (var signal = EventWaitHandle.OpenExisting("Local\\ChatGPTQuotaFloat.Stop")) signal.Set(); } catch (WaitHandleCannotBeOpenedException) { }
                return 0;
            }
            if (args.Length == 2 && args[0] == "--check") {
                try {
                    using (var client = new QuotaClient()) {
                        var quota = client.Read().GetAwaiter().GetResult();
                        File.WriteAllText(args[1], Json.Encode(new { ok = true, primary = quota.Primary, secondary = quota.Secondary, fetchedAt = DateTimeOffset.Now.ToString("o") }), Encoding.UTF8);
                    }
                    return 0;
                } catch (Exception error) { File.WriteAllText(args[1], Json.Encode(new { ok = false, errorType = error.GetType().Name, message = error.Message })); return 1; }
            }
            bool created;
            using (var mutex = new Mutex(true, "Local\\CodexQuotaFloat.v1", out created)) {
                if (!created) return 0;
                using (var stop = new EventWaitHandle(false, EventResetMode.ManualReset, "Local\\ChatGPTQuotaFloat.Stop")) {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new CompanionContext(stop, args.Contains("--preview"), args.Contains("--standalone")));
                }
            }
            return 0;
        }
    }
    // Only desktop hosts count: the codex.exe CLI started for quota queries must never keep us alive.
    sealed class CompanionContext : ApplicationContext {
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly EventWaitHandle stop;
        readonly bool preview, standalone;
        readonly Func<bool> hostRunning;
        Widget widget;
        bool dismissed;
        public CompanionContext(EventWaitHandle signal, bool showPreview, bool independent, Func<bool> detector = null) {
            stop = signal; preview = showPreview; standalone = independent;
            hostRunning = detector ?? HostRunning;
            timer.Interval = 2000;
            timer.Tick += delegate { Tick(); };
            Tick(); timer.Start();
        }
        internal static bool HostRunning() {
            bool found = false;
            int session;
            using (var current = Process.GetCurrentProcess()) session = current.SessionId;
            foreach (string name in new[] { "ChatGPT", "Codex" }) {
                foreach (var p in Process.GetProcessesByName(name)) {
                    using (p) {
                        try {
                            if (p.SessionId != session) continue;
                            if (name == "ChatGPT" || p.MainWindowHandle != IntPtr.Zero) found = true;
                        } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
                    }
                }
            }
            return found;
        }
        void Tick() {
            if (stop.WaitOne(0)) { ExitThread(); return; }
            bool running = standalone || preview || hostRunning();
            if (!running) {
                dismissed = false;
                if (widget != null) { var old = widget; widget = null; old.Close(); }
                return;
            }
            if (widget != null || dismissed) return;
            widget = new Widget(preview);
            widget.FormClosed += delegate(object sender, FormClosedEventArgs e) {
                if (Object.ReferenceEquals(sender, widget)) {
                    widget = null; dismissed = true;
                    if (standalone || preview) ExitThread();
                }
            };
            widget.Show();
        }
        protected override void ExitThreadCore() {
            timer.Stop(); timer.Dispose();
            if (widget != null) { var old = widget; widget = null; old.Close(); }
            base.ExitThreadCore();
        }
    }
    static class Json {
        public static string Encode(object x) { return new JavaScriptSerializer().Serialize(x); }
        public static Dictionary<string, object> Decode(string x) { return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(x); }
        public static Dictionary<string, object> Obj(Dictionary<string, object> x, string key) { object v; return x != null && x.TryGetValue(key, out v) ? v as Dictionary<string, object> : null; }
    }
    public sealed class WindowQuota {
        public double Remaining;
        public int Minutes;
        public long ResetsAt;
        public string Label { get { return Minutes == 10080 ? "每周" : Minutes % 60 == 0 ? (Minutes / 60) + "h" : Minutes + "m"; } }
        public static WindowQuota Parse(Dictionary<string, object> d) {
            if (d == null || !d.ContainsKey("usedPercent") || d["usedPercent"] == null || !d.ContainsKey("windowDurationMins") || d["windowDurationMins"] == null) return null;
            return new WindowQuota { Remaining = Math.Max(0, Math.Min(100, 100 - Convert.ToDouble(d["usedPercent"]))), Minutes = Convert.ToInt32(d["windowDurationMins"]), ResetsAt = d.ContainsKey("resetsAt") && d["resetsAt"] != null ? Convert.ToInt64(d["resetsAt"]) : 0 };
        }
        public string ResetText() {
            return ResetsAt <= 0 ? "恢复时间未知" : "恢复于 " + new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(ResetsAt).ToLocalTime().ToString("MM-dd HH:mm");
        }
    }
    sealed class Quota {
        public WindowQuota Primary, Secondary;
        public static Quota Parse(Dictionary<string, object> result) {
            var buckets = Json.Obj(result, "rateLimitsByLimitId");
            var bucket = Json.Obj(buckets, "codex") ?? Json.Obj(result, "rateLimits");
            if (bucket == null) throw new IOException("No Codex quota available");
            var quota = new Quota { Primary = WindowQuota.Parse(Json.Obj(bucket, "primary")), Secondary = WindowQuota.Parse(Json.Obj(bucket, "secondary")) };
            if (quota.Primary == null && quota.Secondary == null) throw new IOException("No quota windows available");
            return quota;
        }
    }
    sealed class QuotaClient : IDisposable {
        Process process;
        StreamWriter input;
        readonly object sync = new object();
        readonly Dictionary<int, TaskCompletionSource<Dictionary<string, object>>> pending = new Dictionary<int, TaskCompletionSource<Dictionary<string, object>>>();
        int sequence;
        bool ready;
        public event Action<Quota> Updated;
        static string LocateCli() {
            var env = Environment.GetEnvironmentVariable("CODEX_CLI_PATH");
            if (!String.IsNullOrEmpty(env) && File.Exists(env)) return env;
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
            if (Directory.Exists(root)) {
                var exe = Directory.GetFiles(root, "codex.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if (exe != null) return exe;
            }
            throw new FileNotFoundException("Codex CLI not found");
        }
        async Task Connect() {
            if (ready && process != null && !process.HasExited) return;
            Stop();
            process = new Process { StartInfo = new ProcessStartInfo(LocateCli(), "app-server --listen stdio://") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory }, EnableRaisingEvents = true };
            process.OutputDataReceived += OnOutput;
            process.ErrorDataReceived += delegate { /* Never log credentials or raw server output. */ };
            process.Exited += delegate(object sender, EventArgs e) { if (Object.ReferenceEquals(sender, process)) { FailPending(); ready = false; } };
            process.Start();
            input = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false));
            input.AutoFlush = true;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await Request("initialize", new { clientInfo = new { name = "quota_float", title = "额度浮窗", version = "1.0.0" } });
            Send(new { method = "initialized", @params = new { } });
            ready = true;
        }
        void Send(object data) { lock (sync) { if (process == null || process.HasExited) throw new IOException("Server disconnected"); input.WriteLine(Json.Encode(data)); } }
        async Task<Dictionary<string, object>> Request(string method, object parameters) {
            int id = Interlocked.Increment(ref sequence);
            var tcs = new TaskCompletionSource<Dictionary<string, object>>();
            lock (sync) pending[id] = tcs;
            try {
                Send(new { method = method, id = id, @params = parameters });
                if (await Task.WhenAny(tcs.Task, Task.Delay(25000)) != tcs.Task) throw new TimeoutException();
                return await tcs.Task;
            } finally { lock (sync) pending.Remove(id); }
        }
        void OnOutput(object sender, DataReceivedEventArgs e) {
            if (e.Data == null) return;
            try {
                var message = Json.Decode(e.Data);
                object id, method;
                if (message.TryGetValue("id", out id) && id != null) {
                    TaskCompletionSource<Dictionary<string, object>> tcs;
                    lock (sync) pending.TryGetValue(Convert.ToInt32(id), out tcs);
                    if (tcs != null) {
                        if (message.ContainsKey("error")) tcs.TrySetException(new IOException("Quota request failed"));
                        else tcs.TrySetResult(Json.Obj(message, "result"));
                    }
                } else if (message.TryGetValue("method", out method) && Convert.ToString(method) == "account/rateLimits/updated") {
                    var quota = Quota.Parse(Json.Obj(message, "params"));
                    var handler = Updated; if (handler != null) handler(quota);
                }
            } catch { /* Ignore unrelated notifications and invalid JSON. */ }
        }
        public async Task<Quota> Read() {
            try { await Connect(); return Quota.Parse(await Request("account/rateLimits/read", new { })); }
            catch { Stop(); throw; }
        }
        void FailPending() {
            TaskCompletionSource<Dictionary<string, object>>[] all;
            lock (sync) { all = pending.Values.ToArray(); pending.Clear(); }
            foreach (var tcs in all) tcs.TrySetException(new IOException("Server disconnected"));
        }
        void Stop() {
            ready = false;
            if (input != null) { try { input.Dispose(); } catch { } input = null; }
            var old = process; process = null;
            if (old != null) { try { if (!old.HasExited) old.Kill(); } catch { } old.Dispose(); }
            FailPending();
        }
        public void Dispose() { Stop(); }
    }
    sealed partial class Widget : Form {
        readonly QuotaClient client = new QuotaClient();
        readonly System.Windows.Forms.Timer refreshTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer displayTimer = new System.Windows.Forms.Timer();
        readonly ToolTip tip = new ToolTip { AutoPopDelay = 20000, InitialDelay = 250, ReshowDelay = 100 };
        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".chatgpt-quota-widget", "settings.json");
        Quota quota;
        DateTime fetched;
        bool busy, failed, closing, dragging;
        Point mouseStart, formStart;
        Icon appIcon;
        int interval = 60;
        public Widget(bool preview) {
            Text = "ChatGPT 额度显示插件";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            MinimumSize = new Size(64, 116);
            MaximumSize = new Size(64, 116);
            ClientSize = new Size(64, 116);
            ShowInTaskbar = preview;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(32, 32, 32);
            StartPosition = FormStartPosition.Manual;
            DefaultPosition(); LoadSettings();
            using (var path = Round(new Rectangle(0, 0, Width, Height), 12)) Region = new Region(path);
            using (var bmp = new Bitmap(32, 32)) {
                using (var g = Graphics.FromImage(bmp)) {
                    g.Clear(Color.FromArgb(32, 32, 32));
                    using (var pen = new Pen(Color.FromArgb(240, 119, 175), 4)) g.DrawArc(pen, 5, 5, 22, 22, -90, 280);
                    using (var font = new Font("Segoe UI", 10, FontStyle.Bold)) g.DrawString("%", font, Brushes.White, 6, 8);
                }
                // Own a cloned icon; release the temporary native bitmap icon immediately.
                var handle = bmp.GetHicon();
                using (var temporary = Icon.FromHandle(handle)) appIcon = (Icon)temporary.Clone();
                DestroyIcon(handle);
            }
            Icon = appIcon;
            menu.Items.Add("控制面板 / 快捷键设置", null, delegate { OpenControlPanel(); });
            menu.Items.Add("立即刷新", null, async delegate { await RefreshQuota(); });
            menu.Items.Add("查看恢复时间", null, delegate { ShowDetails(); });
            var rates = new ToolStripMenuItem("刷新间隔");
            foreach (int seconds in new int[] { 30, 60, 120 }) {
                int selected = seconds;
                var item = new ToolStripMenuItem(seconds + " 秒"); item.Tag = selected;
                item.Click += delegate { interval = selected; refreshTimer.Interval = interval * 1000; SaveSettings(); UpdateRates(rates); };
                rates.DropDownItems.Add(item);
            }
            UpdateRates(rates); menu.Items.Add(rates);
            menu.Items.Add("重置位置", null, delegate { DefaultPosition(); SaveSettings(); });
            menu.Items.Add("隐藏到托盘", null, delegate { Hide(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("本次关闭（下次打开 ChatGPT 自动显示）", null, delegate { Close(); });
            ContextMenuStrip = menu;
            tray = new NotifyIcon { Icon = appIcon, Text = "Codex 额度浮窗", Visible = true, ContextMenuStrip = menu };
            tray.DoubleClick += delegate { Show(); Activate(); };
            MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { dragging = true; mouseStart = Cursor.Position; formStart = Location; Capture = true; } };
            MouseMove += delegate { if (dragging) Location = new Point(formStart.X + Cursor.Position.X - mouseStart.X, formStart.Y + Cursor.Position.Y - mouseStart.Y); };
            MouseUp += delegate { if (dragging) { dragging = false; Capture = false; KeepOnScreen(); SaveSettings(); } };
            MouseDoubleClick += async delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) await RefreshQuota(); };
            client.Updated += delegate(Quota value) { if (!closing && IsHandleCreated) { try { BeginInvoke(new Action(delegate { ApplyQuota(value); })); } catch (InvalidOperationException) { } } };
            refreshTimer.Interval = interval * 1000;
            refreshTimer.Tick += async delegate { await RefreshQuota(); };
            displayTimer.Interval = 10000;
            displayTimer.Tick += delegate { UpdateTooltip(); Invalidate(); };
            Shown += async delegate { refreshTimer.Start(); displayTimer.Start(); await RefreshQuota(); };
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        static GraphicsPath Round(Rectangle r, int radius) {
            var p = new GraphicsPath(); int d = radius * 2;
            p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }
        void DefaultPosition() { var area = Screen.PrimaryScreen.WorkingArea; Location = new Point(area.Left + 8, area.Bottom - Height - 85); }
        void KeepOnScreen() { var area = Screen.FromPoint(Location).WorkingArea; Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, Left)), Math.Max(area.Top, Math.Min(area.Bottom - Height, Top))); }
        void LoadSettings() { try { var d = Json.Decode(File.ReadAllText(settingsPath)); Location = new Point(Convert.ToInt32(d["x"]), Convert.ToInt32(d["y"])); int n = Convert.ToInt32(d["interval"]); if (n == 30 || n == 60 || n == 120) interval = n; if (d.ContainsKey("topMost")) TopMost = Convert.ToBoolean(d["topMost"]); if (d.ContainsKey("hotkeys")) { var values = ((System.Collections.IEnumerable)d["hotkeys"]).Cast<object>().Select(Convert.ToInt32).ToArray(); if (values.Length == shortcuts.Length && values.All(ValidShortcut) && values.Where(v => v != 0).Distinct().Count() == values.Count(v => v != 0)) shortcuts = values; } KeepOnScreen(); } catch { } }
        void SaveSettings() { try { Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)); File.WriteAllText(settingsPath, Json.Encode(new { x = Left, y = Top, interval = interval, topMost = TopMost, hotkeys = shortcuts })); } catch { } }
        void UpdateRates(ToolStripMenuItem parent) { foreach (ToolStripMenuItem item in parent.DropDownItems) item.Checked = Convert.ToInt32(item.Tag) == interval; }
        async Task RefreshQuota() {
            if (busy || closing) return;
            busy = true; Invalidate();
            try { var value = await client.Read(); if (!closing) ApplyQuota(value); }
            catch { if (!closing) { failed = true; UpdateTooltip(); Invalidate(); } }
            finally { busy = false; if (!closing) Invalidate(); }
        }
        void ApplyQuota(Quota value) { if (closing) return; quota = value; fetched = DateTime.Now; failed = false; UpdateTooltip(); Invalidate(); }
        bool Stale { get { return failed || (quota != null && (DateTime.Now - fetched).TotalSeconds > interval * 2 + 30); } }
        string Details() {
            string text = "Codex 剩余额度\n";
            if (quota == null) text += failed ? "读取失败，请确认 Codex 已登录，再右键刷新。\n" : "正在读取…\n";
            else {
                foreach (var w in new WindowQuota[] { quota.Primary, quota.Secondary }) if (w != null) text += w.Label + "：剩余 " + w.Remaining.ToString("0.#") + "%\n" + w.ResetText() + "\n";
                text += "更新于 " + fetched.ToString("HH:mm:ss") + (Stale ? "（数据可能已过期）" : "") + "\n";
                if (failed) text += "本次读取失败，将自动重试。\n";
            }
            return text + "拖动移动 · 双击刷新 · 右键菜单";
        }
        void UpdateTooltip() { tip.SetToolTip(this, Details()); tray.Text = quota == null ? "Codex 额度：" + (failed ? "读取失败" : "读取中") : "Codex 剩余 " + (quota.Primary == null ? "—" : quota.Primary.Remaining.ToString("0.#") + "%") + (Stale ? "（旧数据）" : ""); }
        void ShowDetails() { MessageBox.Show(this, Details(), "额度与恢复时间", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(Color.FromArgb(62, 62, 62))) using (var path = Round(new Rectangle(0, 0, Width - 1, Height - 1), 12)) g.DrawPath(p, path);
            DrawWindow(g, quota == null ? null : quota.Primary, "5h", 9);
            DrawWindow(g, quota == null ? null : quota.Secondary, "每周", 57);
            using (var b = new SolidBrush(Stale ? Color.FromArgb(255, 186, 92) : busy ? Color.FromArgb(240, 119, 175) : Color.FromArgb(112, 112, 112))) g.FillEllipse(b, 29, 108, 5, 3);
        }
        void DrawWindow(Graphics g, WindowQuota w, string fallback, int y) {
            using (var labelFont = new Font("Microsoft YaHei UI", 10, FontStyle.Regular, GraphicsUnit.Pixel)) using (var valueFont = new Font("Segoe UI", 18, FontStyle.Bold, GraphicsUnit.Pixel)) {
                var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
                TextRenderer.DrawText(g, w == null ? fallback : w.Label, labelFont, new Rectangle(0, y, ClientSize.Width, 16), Color.FromArgb(165, 165, 165), flags);
                string value = w == null ? (failed ? "!" : "—") : w.Remaining.ToString("0") + "%";
                TextRenderer.DrawText(g, value, valueFont, new Rectangle(0, y + 13, ClientSize.Width, 25), Stale ? Color.FromArgb(195, 180, 158) : Color.FromArgb(245, 245, 245), flags);
                using (var track = new SolidBrush(Color.FromArgb(65, 65, 65))) g.FillRectangle(track, 12, y + 39, 40, 3);
                if (w != null) using (var fill = new SolidBrush(Stale ? Color.FromArgb(155, 142, 122) : w.Remaining <= 15 ? Color.FromArgb(255, 138, 121) : Color.FromArgb(240, 119, 175))) g.FillRectangle(fill, 12, y + 39, (float)(40 * w.Remaining / 100), 3);
            }
        }
        protected override void OnFormClosing(FormClosingEventArgs e) { closing = true; refreshTimer.Stop(); displayTimer.Stop(); SaveSettings(); client.Dispose(); tray.Visible = false; tray.Dispose(); tip.Dispose(); refreshTimer.Dispose(); displayTimer.Dispose(); appIcon.Dispose(); base.OnFormClosing(e); }
    }
}

using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
namespace QuotaFloat {
    static class LifecycleTests {
        static void Assert(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS: " + label); }
        [STAThread] static void Main() {
            bool running = false;
            using (var stop = new EventWaitHandle(false, EventResetMode.ManualReset))
            using (var context = new CompanionContext(stop, false, false, () => running)) {
                var type = typeof(CompanionContext);
                var field = type.GetField("widget", BindingFlags.Instance | BindingFlags.NonPublic);
                var tick = type.GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic);
                Func<Widget> widget = () => (Widget)field.GetValue(context);
                Action poll = () => tick.Invoke(context, null);
                Assert(widget() == null, "host absent: no widget");
                running = true; poll(); var first = widget();
                Assert(first != null && first.Visible, "host starts: widget appears");
                Assert(Widget.ValidShortcut((int)(Keys.Control | Keys.Alt | Keys.Q)) && !Widget.ValidShortcut((int)Keys.Q), "shortcut requires Ctrl or Alt");
                var dispatch = typeof(Widget).GetMethod("WndProc", BindingFlags.NonPublic | BindingFlags.Instance);
                Action<int> hotkey = id => { object[] parameters = { Message.Create(first.Handle, 0x0312, (IntPtr)(6100 + id), IntPtr.Zero) }; dispatch.Invoke(first, parameters); };
                hotkey(2); Assert(!first.Visible, "hide shortcut hides widget");
                hotkey(1); Assert(first.Visible, "show shortcut restores hidden widget");
                hotkey(0); Assert(!first.Visible, "toggle shortcut hides widget");
                hotkey(4);
                var panelField = typeof(Widget).GetField("controlPanel", BindingFlags.NonPublic | BindingFlags.Instance);
                var panel = (Form)panelField.GetValue(first);
                Assert(panel != null && panel.Visible && !first.Visible, "control panel opens while widget hidden");
                panel.Close(); hotkey(0); Assert(first.Visible, "closing panel preserves widget and shortcuts");
                poll(); Assert(Object.ReferenceEquals(first, widget()), "repeated detection: one widget");
                running = false; poll();
                Assert(widget() == null && first.IsDisposed, "host exits: widget disposed");
                running = true; poll(); var second = widget();
                Assert(second != null && !Object.ReferenceEquals(first, second), "host restarts: new widget");
                second.Close(); poll(); Assert(widget() == null, "manual close: suppressed for current session");
                running = false; poll(); running = true; poll();
                Assert(widget() != null, "next host session: widget returns");
                var last = widget(); stop.Set(); poll();
                Assert(last.IsDisposed, "stop signal: widget disposed");
            }
        }
    }
}

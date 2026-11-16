using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace LinkDesk
{
    static class Program
    {
        public const string ExitEventName = "LinkDesk-Exit-7f3c";
        public const string ShowEventName = "LinkDesk-Show-7f3c";

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Contains("--uninstall")) { Installer.Uninstall(); return; }
            if (args.Length >= 3 && args[0] == "--apply-update")
            {
                // Bao ban cu thoat (neu con chay) roi chep de
                try { using (var ev = EventWaitHandle.OpenExisting(ExitEventName)) ev.Set(); } catch { }
                int.TryParse(args[2], out int pid);
                Updater.Apply(args[1], pid);
                return;
            }
            bool minimized = args.Contains("--minimized");

            if (!args.Contains("--portable") && Installer.OfferInstall()) return;

            using (var mutex = new Mutex(true, "LinkDesk-Single-7f3c", out bool first))
            {
                if (!first)
                {
                    // Da co LinkDesk dang chay: dua cua so len truoc
                    try { using (var ev = EventWaitHandle.OpenExisting(ShowEventName)) ev.Set(); } catch { }
                    return;
                }
                using (var showEv = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                using (var exitEv = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName))
                {
                    var form = new MainForm(minimized);
                    var t = new Thread(() =>
                    {
                        var handles = new WaitHandle[] { showEv, exitEv };
                        while (true)
                        {
                            int i = WaitHandle.WaitAny(handles);
                            try
                            {
                                if (i == 0) form.BeginInvoke(new Action(form.ShowFromTray));
                                else { form.BeginInvoke(new Action(form.ExitApp)); return; }
                            }
                            catch { return; }
                        }
                    }) { IsBackground = true };
                    t.Start();
                    Application.Run(form);
                }
            }
        }
    }
}

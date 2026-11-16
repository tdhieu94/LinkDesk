using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LinkDesk
{
    /// <summary>Tu cai dat: chep exe vao %LOCALAPPDATA%\Programs\LinkDesk, tao loi tat, dang ky go cai dat.</summary>
    public static class Installer
    {
        public const string AppName = "LinkDesk";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\LinkDesk";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "LinkDesk");
        public static string InstalledExe => Path.Combine(InstallDir, "LinkDesk.exe");
        public static string CurrentExe => Assembly.GetExecutingAssembly().Location;
        public static string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "LinkDesk.lnk");
        static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "LinkDesk.lnk");

        public static bool IsRunningInstalled =>
            string.Equals(Path.GetFullPath(CurrentExe), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

        public static bool IsInstalled => File.Exists(InstalledExe);

        /// <summary>Hoi cai dat khi chay tu file tai ve. Tra ve true neu da chuyen sang ban da cai (thoat ban hien tai).</summary>
        public static bool OfferInstall()
        {
            if (IsRunningInstalled) return false;
            string q = IsInstalled
                ? "LinkDesk đã được cài trên máy này. Cập nhật bằng bản bạn vừa mở (phiên bản " + Version + ")?"
                : "Cài đặt LinkDesk vào máy tính?\n\nSẽ tạo biểu tượng ở menu Start và Desktop, có thể gỡ trong Cài đặt → Ứng dụng.\n\nChọn \"No\" để chạy thử mà không cài.";
            var r = MessageBox.Show(q, "LinkDesk", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return false;
            try
            {
                // Dong ban dang chay (neu co) de chep de
                try { using (var ev = EventWaitHandle.OpenExisting(Program.ExitEventName)) ev.Set(); Thread.Sleep(1500); } catch { }
                Directory.CreateDirectory(InstallDir);
                CopyWithRetry(CurrentExe, InstalledExe);
                string cfg = CurrentExe + ".config";
                if (File.Exists(cfg)) CopyWithRetry(cfg, InstalledExe + ".config");
                CreateShortcut(StartMenuLink, InstalledExe, "", "Xem và điều khiển điện thoại Android");
                CreateShortcut(DesktopLink, InstalledExe, "", "Xem và điều khiển điện thoại Android");
                using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
                {
                    k.SetValue("DisplayName", "LinkDesk");
                    k.SetValue("DisplayVersion", Version);
                    k.SetValue("Publisher", "LinkDesk");
                    k.SetValue("DisplayIcon", InstalledExe + ",0");
                    k.SetValue("InstallLocation", InstallDir);
                    k.SetValue("UninstallString", "\"" + InstalledExe + "\" --uninstall");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    k.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
                }
                if (IsStartupEnabled()) SetStartup(true); // cap nhat duong dan
                Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true });
                return true;
            }
            catch (Exception e)
            {
                MessageBox.Show("Không cài được: " + e.Message + "\n\nNếu LinkDesk đang chạy ở khay hệ thống, hãy thoát nó rồi thử lại.", "LinkDesk", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        /// <summary>Cap nhat phien ban trong muc Go cai dat (sau khi tu cap nhat).</summary>
        public static void RefreshUninstallEntry()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey, true))
                    k?.SetValue("DisplayVersion", Version);
            }
            catch { }
        }

        static void CopyWithRetry(string src, string dst)
        {
            for (int i = 0; ; i++)
            {
                try { File.Copy(src, dst, true); return; }
                catch (IOException) when (i < 10) { Thread.Sleep(500); }
            }
        }

        public static void Uninstall()
        {
            if (MessageBox.Show("Gỡ cài đặt LinkDesk khỏi máy tính?", "LinkDesk", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { using (var ev = EventWaitHandle.OpenExisting(Program.ExitEventName)) ev.Set(); Thread.Sleep(1000); } catch { }
            SetStartup(false);
            TryDelete(StartMenuLink);
            TryDelete(DesktopLink);
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            bool removeData = MessageBox.Show("Xoá luôn cài đặt đã lưu (danh sách điện thoại, mã PIN)?", "LinkDesk", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            if (removeData) { try { Directory.Delete(Config.Dir, true); } catch { } }
            // Xoa thu muc cai dat sau khi tien trinh nay thoat
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", "/c timeout /t 2 /nobreak >nul & rmdir /s /q \"" + InstallDir + "\"")
                {
                    CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            catch { }
            MessageBox.Show("Đã gỡ LinkDesk.", "LinkDesk", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }

        public static bool IsStartupEnabled()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k?.GetValue(AppName) != null; } catch { return false; }
        }

        public static void SetStartup(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(AppName, "\"" + (IsInstalled ? InstalledExe : CurrentExe) + "\" --minimized");
                    else k.DeleteValue(AppName, false);
                }
            }
            catch { }
        }

        static void CreateShortcut(string lnkPath, string target, string args, string desc)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            try
            {
                object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                Type lt = lnk.GetType();
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
                lt.InvokeMember("Arguments", BindingFlags.SetProperty, null, lnk, new object[] { args });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Path.GetDirectoryName(target) });
                lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { target + ",0" });
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { desc });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}

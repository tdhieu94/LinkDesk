using System;
using System.Windows.Forms;

namespace LinkDesk
{
    // Nghe am thanh dien thoai tren may tinh.
    public partial class MainForm
    {
        AudioPlayer player;
        readonly object playerLock = new object();
        bool playerFailed;
        RoundButton audioBtn;

        void BuildAudioButton()
        {
            audioBtn = BarBtn("", "Nghe âm thanh điện thoại trên máy tính", () => SetAudio(!Cfg.Audio));
            audioBtn.Toggled = Cfg.Audio;
        }

        public void SetAudio(bool on)
        {
            Cfg.Audio = on; Cfg.Save();
            if (audioBtn != null) { audioBtn.Toggled = on; audioBtn.Invalidate(); }
            playerFailed = false;
            client?.Send("t", "audio", "on", on);
            if (!on) StopPlayer();
        }

        public void SetAudioVolume(int v)
        {
            Cfg.AudioVolume = Math.Max(0, Math.Min(100, v)); Cfg.Save();
            lock (playerLock) player?.SetVolume(Cfg.AudioVolume / 100.0);
        }

        /// <summary>Goi tren luong nhan du lieu.</summary>
        void OnAudio(PhoneClient c, byte[] data)
        {
            if (client != c || !Cfg.Audio) return;
            recorder?.AddAudio(data);
            AudioPlayer p;
            lock (playerLock)
            {
                if (player == null && !playerFailed)
                {
                    player = AudioPlayer.TryCreate(out string err);
                    if (player == null)
                    {
                        playerFailed = true;
                        try { BeginInvoke(new Action(() => Toast("Không phát được âm thanh: " + err))); } catch { }
                    }
                    else player.SetVolume(Cfg.AudioVolume / 100.0);
                }
                p = player;
            }
            p?.Push(data, 1, data.Length - 1);
        }

        void StopPlayer()
        {
            lock (playerLock) { player?.Dispose(); player = null; }
        }
    }
}

using System.Media;
using System.Runtime.InteropServices;
using System.Text;

namespace StudentTimerClock;

/// <summary>Built-in synthesized alarm sounds (no external files needed) + custom wav/mp3 via MCI.</summary>
public static class Sound
{
    public static readonly string[] BuiltIn = { "Digital", "Chime", "Bell", "Soft", "Alarm", "Rooster Beep", "Windows Exclamation", "Windows Asterisk", "None" };
    public const string CustomLabel = "Custom file...";

    static SoundPlayer _player;
    static MemoryStream _stream;
    static bool _mciOpen;
    static readonly Dictionary<string, byte[]> _cache = new();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    static extern int mciSendString(string command, StringBuilder ret, int retLen, IntPtr hwnd);

    public static void Play(string sound, string customPath, bool loop, int volume)
    {
        Stop();
        try
        {
            if (string.IsNullOrEmpty(sound) || sound == "None") return;
            if (sound == "Windows Exclamation") { SystemSounds.Exclamation.Play(); return; }
            if (sound == "Windows Asterisk") { SystemSounds.Asterisk.Play(); return; }
            if (sound == CustomLabel)
            {
                if (File.Exists(customPath))
                {
                    mciSendString("close htcsound", null, 0, IntPtr.Zero);
                    string type = customPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? "waveaudio" : "mpegvideo";
                    if (mciSendString($"open \"{customPath}\" type {type} alias htcsound", null, 0, IntPtr.Zero) == 0)
                    {
                        _mciOpen = true;
                        if (type == "mpegvideo") mciSendString($"setaudio htcsound volume to {Math.Clamp(volume, 0, 100) * 10}", null, 0, IntPtr.Zero);
                        mciSendString(loop && type == "mpegvideo" ? "play htcsound repeat" : "play htcsound", null, 0, IntPtr.Zero);
                        return;
                    }
                }
                sound = "Digital";
            }
            var bytes = GetWav(sound, volume);
            _stream = new MemoryStream(bytes);
            _player = new SoundPlayer(_stream);
            if (loop) _player.PlayLooping(); else _player.Play();
        }
        catch { try { SystemSounds.Beep.Play(); } catch { } }
    }

    public static void Stop()
    {
        try { _player?.Stop(); } catch { }
        _player?.Dispose(); _player = null;
        _stream?.Dispose(); _stream = null;
        if (_mciOpen) { try { mciSendString("close htcsound", null, 0, IntPtr.Zero); } catch { } _mciOpen = false; }
    }

    static byte[] GetWav(string name, int volume)
    {
        string key = name + "|" + volume;
        if (_cache.TryGetValue(key, out var b)) return b;
        const int rate = 22050;
        var samples = new List<float>();
        void Tone(double freq, double secs, double amp = 0.6, double decay = 0, double freq2 = 0)
        {
            int n = (int)(rate * secs);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / rate;
                double env = decay > 0 ? Math.Exp(-t * decay) : 1.0;
                double attack = Math.Min(1, i / (rate * 0.004));
                double release = Math.Min(1, (n - i) / (rate * 0.004));
                double f = freq2 > 0 ? freq + (freq2 - freq) * t / secs : freq;
                double v = Math.Sin(2 * Math.PI * f * t) + 0.25 * Math.Sin(4 * Math.PI * f * t);
                samples.Add((float)(v * amp * env * attack * release / 1.25));
            }
        }
        void Silence(double secs) { for (int i = 0; i < (int)(rate * secs); i++) samples.Add(0); }

        switch (name)
        {
            case "Chime":
                Tone(1046.5, 0.45, 0.6, 5); Tone(784, 0.45, 0.6, 5); Tone(659.3, 0.45, 0.6, 5); Tone(523.3, 0.9, 0.6, 4); Silence(0.6);
                break;
            case "Bell":
                for (int k = 0; k < 2; k++) { Tone(880, 0.12, 0.7, 2); Tone(1320, 0.12, 0.6, 2); }
                for (int k = 0; k < 6; k++) { Tone(1200, 0.05, 0.6); Tone(900, 0.05, 0.6); }
                Silence(0.5);
                break;
            case "Soft":
                Tone(523.3, 0.5, 0.45, 3); Tone(659.3, 0.5, 0.45, 3); Tone(784, 0.9, 0.45, 2.5); Silence(0.9);
                break;
            case "Alarm":
                for (int k = 0; k < 3; k++) { Tone(600, 0.35, 0.6, 0, 1200); Tone(1200, 0.35, 0.6, 0, 600); }
                Silence(0.2);
                break;
            case "Rooster Beep":
                Tone(700, 0.15, 0.6); Tone(900, 0.15, 0.6); Tone(1100, 0.15, 0.6); Tone(1400, 0.45, 0.6, 2); Silence(0.5);
                break;
            default: // Digital
                for (int k = 0; k < 4; k++) { Tone(2000, 0.07, 0.55); Silence(0.06); }
                Silence(0.5);
                break;
        }

        float vol = Math.Clamp(volume, 0, 100) / 100f;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataLen = samples.Count * 2;
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataLen); w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataLen);
        foreach (var s in samples) w.Write((short)Math.Clamp(s * vol * 32767, -32767, 32767));
        w.Flush();
        b = ms.ToArray();
        _cache[key] = b;
        return b;
    }
}

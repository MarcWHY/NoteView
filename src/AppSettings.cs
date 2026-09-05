using System;
using System.IO;
using System.Xml.Serialization;

namespace NoteView
{
    public sealed class AppSettings
    {
        public string Background = "#101B26";
        public double BackgroundOpacity = 96;
        public string Accent = "#70E5C0";
        [XmlIgnore] public string HarmonyTint = "#A4BCCB";
        public bool Topmost = true;
        public bool FullRange = true;
        public bool GhostNotes = true;
        public bool Flats = false;
        public int KeySignatureFifths = 0;
        public bool MinorKey = false;
        public double StaffScale = 1;
        public double StaffOffsetX = 0;
        public double StaffOffsetY = 0;
        public bool ObsOutputEnabled = true;
        public double KeyboardScale = 1;
        public double KeyboardOffsetX = 0;
        public double KeyboardOffsetY = 0;
        public double HarmonyScale = 1;
        public double HarmonyOffsetX = 0;
        public double HarmonyOffsetY = 0;
        public bool DarkInk = false;
        public bool Overlay = false;
        public bool IncludeSustainInChord = true;
        public int IntensityMode = 0;
        public int Channel = 0;
        public string DeviceName = "";
        public double Width = 1120;
        public double Height = 780;
        public double Left = -99999;
        public double Top = -99999;
        public static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NoteView");
        public static readonly string FilePath = Path.Combine(DirectoryPath, "settings.xml");

        public AppSettings Snapshot() { return (AppSettings)MemberwiseClone(); }

        public static AppSettings Load()
        {
            try
            {
                using (var stream = File.OpenRead(FilePath))
                {
                    var s = (AppSettings)new XmlSerializer(typeof(AppSettings)).Deserialize(stream);
                    s.BackgroundOpacity = FiniteClamp(s.BackgroundOpacity, 0, 100, 96);
                    s.Width = FiniteClamp(s.Width, 880, 2400, 1120);
                    s.Height = FiniteClamp(s.Height, 640, 1600, 780);
                    s.StaffScale = FiniteClamp(s.StaffScale, .2, 2, 1);
                    s.StaffOffsetX = FiniteClamp(s.StaffOffsetX, -1120, 1120, 0);
                    s.StaffOffsetY = FiniteClamp(s.StaffOffsetY, -640, 640, 0);
                    s.KeyboardScale = FiniteClamp(s.KeyboardScale, .2, 1.5, 1);
                    s.KeyboardOffsetX = FiniteClamp(s.KeyboardOffsetX, -1120, 1120, 0);
                    s.KeyboardOffsetY = FiniteClamp(s.KeyboardOffsetY, -640, 640, 0);
                    s.HarmonyScale = FiniteClamp(s.HarmonyScale, .2, 1.5, 1);
                    s.HarmonyOffsetX = FiniteClamp(s.HarmonyOffsetX, -1120, 1120, 0);
                    s.HarmonyOffsetY = FiniteClamp(s.HarmonyOffsetY, -640, 640, 0);
                    s.Channel = Math.Max(0, Math.Min(16, s.Channel));
                    if (s.KeySignatureFifths < -7 || s.KeySignatureFifths > 7) s.KeySignatureFifths = 0;
                    s.IntensityMode = Math.Max(0, Math.Min(2, s.IntensityMode));
                    return s;
                }
            }
            catch { return new AppSettings(); }
        }
        private static double FiniteClamp(double x, double min, double max, double fallback)
        { return double.IsNaN(x) || double.IsInfinity(x) ? fallback : Math.Max(min, Math.Min(max, x)); }
        public void Save()
        {
            Directory.CreateDirectory(DirectoryPath);
            string temp = FilePath + ".tmp";
            using (var stream = File.Create(temp)) new XmlSerializer(typeof(AppSettings)).Serialize(stream, this);
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null); else File.Move(temp, FilePath);
        }
    }
}

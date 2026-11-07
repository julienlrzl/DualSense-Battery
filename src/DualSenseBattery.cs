// DualSenseBattery — batterie des manettes PS5 DualSense dans la zone de notification (Bluetooth ou USB)
// Compilé avec le csc intégré à Windows (.NET Framework 4.8, C# 5) : voir Recompiler.cmd
//
// - Icône de la zone de notification : batterie Windows 11, pourcentage ou anneau (au choix), suit le thème.
//   Masquée quand aucune manette n'est allumée (option) ; le raccourci « Manette PS5 » du menu Démarrer rouvre le panneau.
// - Clic gauche : panneau façon Windows 11 (acrylique, coins arrondis, animations), une carte par manette
//   (anneau de batterie, surnom, identifier, éteindre, graphique 24 h), manettes récentes déconnectées en gris,
//   page de réglages derrière la roue dentée.
// - Clic droit : petit menu natif.
// - Affichage en jeu : maintenir le bouton PS ~1 s affiche la batterie par-dessus le jeu (sans lui voler le focus).
// - Lecture continue et légère de chaque manette (10 lectures/s) : batterie, inactivité (arrêt automatique en Bluetooth).
// - Alertes à 10 % et 5 % : notification Windows + encart en jeu + vibrations et barre lumineuse rouge sur la manette.
// - Barre lumineuse : bleue, faible, éteinte, ou selon la batterie (bleu → violet → rouge, pulsation orange si faible).
// - Autonomie restante et durée de charge estimées à partir de ce qui est observé.
// - Journal de diagnostic : journal.txt ; historique : historique.csv (à côté de l'exe).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;
using SD = System.Drawing;
using SD2 = System.Drawing.Drawing2D;
using SDI = System.Drawing.Imaging;
using WF = System.Windows.Forms;
using W = System.Windows;
using WC = System.Windows.Controls;
using WCP = System.Windows.Controls.Primitives;
using WS = System.Windows.Shapes;
using WM = System.Windows.Media;
using WMA = System.Windows.Media.Animation;
using WI = System.Windows.Interop;
using WT = System.Windows.Threading;

[assembly: AssemblyTitle("DualSense Battery")]
[assembly: AssemblyProduct("DualSense Battery")]
[assembly: AssemblyDescription("PS5 DualSense controller battery in the Windows taskbar")]
[assembly: AssemblyVersion("3.0.0.0")]
[assembly: AssemblyFileVersion("3.0.0.0")]

namespace DualSenseBattery
{
    static class Native
    {
        public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_RW = 3, OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;
        public const uint DIGCF_PRESENT = 0x2, DIGCF_DEVICEINTERFACE = 0x10;
        public const int WM_DEVICECHANGE = 0x0219, DBT_DEVICEARRIVAL = 0x8000, DBT_DEVICEREMOVECOMPLETE = 0x8004, DBT_DEVTYP_DEVICEINTERFACE = 5;
        public const uint IOCTL_BTH_DISCONNECT_DEVICE = 0x41000C;
        public static readonly IntPtr INVALID = new IntPtr(-1);

        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr RegisterDeviceNotification(IntPtr recipient, IntPtr filter, uint flags);
        [DllImport("user32.dll")] public static extern bool UnregisterDeviceNotification(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool AppendMenu(IntPtr m, uint flags, UIntPtr id, string text);
        [DllImport("user32.dll")] public static extern bool CheckMenuRadioItem(IntPtr m, uint first, uint last, uint check, uint flags);
        [DllImport("user32.dll")] public static extern int TrackPopupMenuEx(IntPtr m, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
        [DllImport("user32.dll")] public static extern bool DestroyMenu(IntPtr m);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
        [DllImport("shell32.dll")] public static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER id, out RECT r);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);
        [DllImport("uxtheme.dll", EntryPoint = "#135")] public static extern int SetPreferredAppMode(int mode);
        [DllImport("uxtheme.dll", EntryPoint = "#136")] public static extern void FlushMenuThemes();

        [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")] public static extern bool HidD_GetFeature(IntPtr h, byte[] buf, int len);
        [DllImport("hid.dll")] public static extern bool HidD_SetNumInputBuffers(IntPtr h, int n);
        [DllImport("hid.dll")] public static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr data);
        [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr data, [Out] short[] caps);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool ReadFile(IntPtr h, IntPtr buf, int len, IntPtr read, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool WriteFile(IntPtr h, byte[] buf, int len, out int written, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetOverlappedResult(IntPtr h, IntPtr ov, out int n, bool wait);
        [DllImport("kernel32.dll")] public static extern bool CancelIoEx(IntPtr h, IntPtr ov);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateEvent(IntPtr sa, bool manual, bool initial, string name);
        [DllImport("kernel32.dll")] public static extern bool ResetEvent(IntPtr h);
        [DllImport("kernel32.dll")] public static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(IntPtr h, uint code, ref ulong inBuf, int inSize, IntPtr outBuf, int outSize, out int ret, IntPtr ov);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] public static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr hwnd, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid g, uint index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, ref SP_DEVINFO_DATA info);
        [DllImport("setupapi.dll")] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("cfgmgr32.dll")] public static extern int CM_Get_Parent(out uint parent, uint inst, int flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] public static extern int CM_Get_Device_ID(uint inst, StringBuilder buf, int len, int flags);

        [DllImport("bthprops.cpl", SetLastError = true)] public static extern IntPtr BluetoothFindFirstRadio(ref BLUETOOTH_FIND_RADIO_PARAMS p, out IntPtr radio);
        [DllImport("bthprops.cpl")] public static extern bool BluetoothFindRadioClose(IntPtr find);

        [StructLayout(LayoutKind.Sequential)] public struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)] public struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)] public struct BLUETOOTH_FIND_RADIO_PARAMS { public uint dwSize; }
        [StructLayout(LayoutKind.Sequential)] public struct DEV_BROADCAST_DEVICEINTERFACE { public int dbcc_size; public int dbcc_devicetype; public int dbcc_reserved; public Guid dbcc_classguid; public short dbcc_name; }
        [StructLayout(LayoutKind.Sequential)] public struct NOTIFYICONIDENTIFIER { public uint cbSize; public IntPtr hWnd; public uint uID; public Guid guidItem; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct MARGINS { public int Left, Right, Top, Bottom; }

        // Fond acrylique Windows 11 + coins arrondis + thème clair/sombre sur une fenêtre WPF sans bordure
        public static bool ApplyBackdrop(IntPtr hwnd, bool light)
        {
            try
            {
                MARGINS m = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref m);
                int dark = light ? 0 : 1, round = 2, backdrop = 3;
                DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
                DwmSetWindowAttribute(hwnd, 33, ref round, 4);
                return DwmSetWindowAttribute(hwnd, 38, ref backdrop, 4) == 0;
            }
            catch { return false; }
        }
    }

    // ------------------------------------------------------------------ journal, réglages, formats, historique

    static class Log
    {
        static readonly object Lock = new object();
        // Données (journal, historique, icône des notifications) : %LOCALAPPDATA%\DualSenseBattery, où que soit l'exe
        public static readonly string Dir = MakeDir();
        static string MakeDir()
        {
            string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DualSenseBattery");
            try { Directory.CreateDirectory(d); }
            catch { d = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location); }
            return d;
        }
        static readonly string File_ = Path.Combine(Dir, "journal.txt");

        public static void Write(string s)
        {
            try
            {
                lock (Lock)
                {
                    FileInfo fi = new FileInfo(File_);
                    if (fi.Exists && fi.Length > 256 * 1024)
                    {
                        string old = File_ + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(File_, old);
                    }
                    File.AppendAllText(File_, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + s + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }
    }

    // Langue de l'interface : celle de Windows (français si Windows est en français, anglais sinon).
    // DSB_LANG=fr|en force une langue (aperçus).
    static class L
    {
        public static readonly bool Fr = Detect();

        static bool Detect()
        {
            string forced = Environment.GetEnvironmentVariable("DSB_LANG");
            if (!string.IsNullOrEmpty(forced)) return forced.StartsWith("fr", StringComparison.OrdinalIgnoreCase);
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr";
        }

        public static string T(string fr, string en) { return Fr ? fr : en; }
        public static string AppName { get { return T("Manette PS5", "DualSense Battery"); } }
        public static string PadName { get { return T("Manette PS5", "DualSense"); } }
        public static string Numbered(int n) { return T("Manette ", "Controller ") + n; }
        public static string Colon { get { return T(" : ", ": "); } }

        // Dans le XAML, [[français|english]]
        static readonly Regex Pair = new Regex(@"\[\[([^|\]]*)\|([^\]]*)\]\]");
        public static string Xaml(string s) { return Pair.Replace(s, delegate (Match m) { return m.Groups[Fr ? 1 : 2].Value; }); }
    }

    static class Settings
    {
        const string Key = @"Software\DualSenseBattery";
        public static readonly int[] AutoOffChoices = { 0, 10, 15, 30, 60 };
        public static readonly string[] LightbarChoices = L.Fr ? new[] { "Bleue", "Faible", "Éteinte", "Selon la batterie" } : new[] { "Blue", "Dim", "Off", "Battery level" };
        public static readonly string[] TrayStyleChoices = L.Fr ? new[] { "Batterie", "Pourcentage", "Anneau" } : new[] { "Battery", "Percentage", "Ring" };
        const double DefaultMinutesPerStep = 30;        // ~5 h pour 100 %, remplacé par la valeur apprise
        const double DefaultChargeMinutesPerStep = 18;  // ~3 h pour une charge complète

        static object Get(string name)
        {
            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key)) return k == null ? null : k.GetValue(name); }
            catch { return null; }
        }

        static void Set(string name, object value, RegistryValueKind kind)
        {
            try { using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key)) k.SetValue(name, value, kind); }
            catch { }
        }

        static int GetInt(string name, int def) { object v = Get(name); return v is int ? (int)v : def; }

        static double GetDouble(string name)
        {
            double d;
            string s = Get(name) as string;
            return s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0;
        }

        public static int AutoOffMinutes { get { return GetInt("AutoOffMinutes", 15); } set { Set("AutoOffMinutes", value, RegistryValueKind.DWord); } }
        public static int Lightbar { get { return Math.Max(0, Math.Min(3, GetInt("Lightbar", 3))); } set { Set("Lightbar", value, RegistryValueKind.DWord); } }
        public static int TrayStyle { get { return Math.Max(0, Math.Min(2, GetInt("TrayStyle", 0))); } set { Set("TrayStyle", value, RegistryValueKind.DWord); } }
        public static bool HideWhenOff { get { return GetInt("HideWhenOff", 1) != 0; } set { Set("HideWhenOff", value ? 1 : 0, RegistryValueKind.DWord); } }
        public static bool GameOverlay { get { return GetInt("GameOverlay", 1) != 0; } set { Set("GameOverlay", value ? 1 : 0, RegistryValueKind.DWord); } }

        // Lancement à l'ouverture de session (clé Run de l'utilisateur)
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RunName = "DualSenseBattery";
        public static bool StartWithWindows
        {
            get
            {
                try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(RunName) != null; }
                catch { return false; }
            }
            set
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                    {
                        if (value) k.SetValue(RunName, "\"" + Assembly.GetEntryAssembly().Location + "\"");
                        else k.DeleteValue(RunName, false);
                    }
                }
                catch { }
            }
        }

        // Premier lancement : démarrage avec Windows activé par défaut ; ensuite, suit l'exe s'il a été déplacé
        public static void InitStartup()
        {
            if (GetInt("FirstRunDone", 0) == 0)
            {
                Set("FirstRunDone", 1, RegistryValueKind.DWord);
                StartWithWindows = true;
            }
            else if (StartWithWindows) StartWithWindows = true;
        }

        public static double MinutesPerStep { get { double d = GetDouble("MinutesPerStep"); return d > 0 ? d : DefaultMinutesPerStep; } }
        public static double ChargeMinutesPerStep { get { double d = GetDouble("ChargeMinutesPerStep"); return d > 0 ? d : DefaultChargeMinutesPerStep; } }

        // Moyenne glissante de la durée d'un palier de 10 % (décharge ou charge)
        public static void LearnStep(bool charging, double minutes)
        {
            if (minutes < 3 || minutes > 240) return;
            string name = charging ? "ChargeMinutesPerStep" : "MinutesPerStep";
            double old = GetDouble(name);
            double v = old > 0 ? old * 0.7 + minutes * 0.3 : minutes;
            Set(name, v.ToString("0.0", CultureInfo.InvariantCulture), RegistryValueKind.String);
            Log.Write((charging ? "Charge" : "Décharge") + " : palier de 10 % en " + minutes.ToString("0") + " min, moyenne " + v.ToString("0") + " min");
        }
    }

    // Manettes déjà vues : surnom, dernier niveau, dernière connexion (pour les afficher en gris une fois éteintes)
    sealed class KnownPad
    {
        public string Key, Nickname;
        public int LastPercent = -1;
        public DateTime LastSeen = DateTime.MinValue;
    }

    static class Known
    {
        const string Root = @"Software\DualSenseBattery\Manettes";

        public static string Nickname(string key)
        {
            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Root + "\\" + key)) { string n = k == null ? null : k.GetValue("Surnom") as string; return string.IsNullOrEmpty(n) ? null : n; } }
            catch { return null; }
        }

        public static void SetNickname(string key, string name)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Root + "\\" + key))
                {
                    if (string.IsNullOrEmpty(name)) k.DeleteValue("Surnom", false);
                    else k.SetValue("Surnom", name);
                }
            }
            catch { }
        }

        public static void Seen(string key, int percent)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Root + "\\" + key))
                {
                    k.SetValue("DernierNiveau", percent, RegistryValueKind.DWord);
                    k.SetValue("DerniereConnexion", DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture));
                }
            }
            catch { }
        }

        public static List<KnownPad> All()
        {
            List<KnownPad> res = new List<KnownPad>();
            try
            {
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(Root))
                {
                    if (root == null) return res;
                    foreach (string name in root.GetSubKeyNames())
                        using (RegistryKey k = root.OpenSubKey(name))
                        {
                            KnownPad p = new KnownPad { Key = name, Nickname = k.GetValue("Surnom") as string };
                            object lv = k.GetValue("DernierNiveau");
                            if (lv is int) p.LastPercent = (int)lv;
                            long t;
                            string s = k.GetValue("DerniereConnexion") as string;
                            if (s != null && long.TryParse(s, out t)) p.LastSeen = new DateTime(t, DateTimeKind.Local);
                            res.Add(p);
                        }
                }
            }
            catch { }
            res.Sort(delegate (KnownPad a, KnownPad b) { return b.LastSeen.CompareTo(a.LastSeen); });
            return res;
        }
    }

    static class Fmt
    {
        static readonly CultureInfo Culture = new CultureInfo(L.Fr ? "fr-FR" : "en-US");
        public static readonly string Pct = L.Fr ? " %" : "%";

        public static string AutoOff(int minutes)
        {
            if (minutes <= 0) return L.T("Jamais", "Never");
            return minutes % 60 == 0 ? (minutes / 60) + L.T(" h", minutes == 60 ? " hour" : " hours") : minutes + " min";
        }

        // « 3 h 30 » / « 3h 30m »
        static string Hours(int h, int m, bool compact)
        {
            if (L.Fr) return m == 0 ? h + " h" : h + " h " + m.ToString("00");
            if (compact) return m == 0 ? h + "h" : h + "h" + m.ToString("00");
            return m == 0 ? h + "h" : h + "h " + m + "m";
        }

        public static string Duration(double minutes)
        {
            if (minutes < 55) return Math.Max(5, (int)Math.Round(minutes / 5) * 5) + " min";
            int total = (int)Math.Round(minutes / 10) * 10;
            return Hours(total / 60, total % 60, false);
        }

        // Version courte pour l'intérieur de l'anneau : « ~3 h », « ~3 h 30 », « ~40 min »
        public static string Short(double minutes)
        {
            if (minutes < 55) return "~" + Math.Max(5, (int)Math.Round(minutes / 5) * 5) + " min";
            int total = (int)Math.Round(minutes / 30) * 30;
            return "~" + Hours(total / 60, total % 60, true);
        }

        public static string Ago(DateTime t)
        {
            TimeSpan d = DateTime.Now - t;
            if (d.TotalMinutes < 1) return L.T("à l'instant", "just now");
            if (d.TotalMinutes < 60) return L.T("il y a " + (int)d.TotalMinutes + " min", (int)d.TotalMinutes + " min ago");
            if (d.TotalHours < 24) return L.T("il y a " + (int)d.TotalHours + " h", (int)d.TotalHours + " h ago");
            if (d.TotalDays < 2) return L.T("hier à ", "yesterday at ") + t.ToString("HH:mm", Culture);
            return L.T("le " + t.ToString("d MMMM", Culture), "on " + t.ToString("MMMM d", Culture));
        }
    }

    sealed class Sample
    {
        public DateTime T;
        public int P;          // pourcentage, -1 = manette déconnectée à partir de T
        public bool Charging;
    }

    // Historique des niveaux (48 h glissantes) par manette, pour le graphique du panneau
    static class History
    {
        static readonly string File_ = Path.Combine(Log.Dir, "historique.csv");
        static readonly Dictionary<string, List<Sample>> Data = new Dictionary<string, List<Sample>>();

        public static void Load()
        {
            try
            {
                if (!File.Exists(File_)) return;
                DateTime limit = DateTime.Now.AddHours(-48);
                foreach (string line in File.ReadAllLines(File_))
                {
                    string[] f = line.Split(';');
                    long t; int p;
                    if (f.Length < 4 || !long.TryParse(f[1], out t) || !int.TryParse(f[2], out p)) continue;
                    DateTime dt = new DateTime(t, DateTimeKind.Local);
                    if (dt < limit) continue;
                    List(f[0]).Add(new Sample { T = dt, P = p, Charging = f[3] == "1" });
                }
                // l'appli était arrêtée : on termine chaque tracé au dernier relevé
                foreach (List<Sample> l in Data.Values)
                    if (l.Count > 0 && l[l.Count - 1].P >= 0) l.Add(new Sample { T = l[l.Count - 1].T.AddSeconds(1), P = -1 });
                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<string, List<Sample>> kv in Data)
                    foreach (Sample s in kv.Value) sb.Append(Line(kv.Key, s));
                File.WriteAllText(File_, sb.ToString());
            }
            catch (Exception e) { Log.Write("Historique illisible : " + e.Message); }
        }

        static List<Sample> List(string key)
        {
            List<Sample> l;
            if (!Data.TryGetValue(key, out l)) { l = new List<Sample>(); Data[key] = l; }
            return l;
        }

        static string Line(string key, Sample s)
        {
            return key + ";" + s.T.Ticks.ToString(CultureInfo.InvariantCulture) + ";" + s.P + ";" + (s.Charging ? 1 : 0) + "\n";
        }

        static void Append(string key, Sample s)
        {
            List(key).Add(s);
            try { File.AppendAllText(File_, Line(key, s)); } catch { }
        }

        // Relevé : n'ajoute un point que si la valeur change, ou toutes les 10 min (pour suivre le temps de jeu)
        public static void Add(string key, int percent, bool charging)
        {
            if (key == null) return;
            List<Sample> l = List(key);
            Sample last = l.Count > 0 ? l[l.Count - 1] : null;
            if (last != null && last.P == percent && last.Charging == charging && (DateTime.Now - last.T).TotalMinutes < 10) return;
            Append(key, new Sample { T = DateTime.Now, P = percent, Charging = charging });
        }

        public static void Gap(string key)
        {
            if (key == null) return;
            List<Sample> l = List(key);
            if (l.Count > 0 && l[l.Count - 1].P >= 0) Append(key, new Sample { T = DateTime.Now, P = -1 });
        }

        public static List<Sample> Get(string key)
        {
            List<Sample> res = new List<Sample>();
            List<Sample> l;
            if (key == null || !Data.TryGetValue(key, out l)) return res;
            DateTime from = DateTime.Now.AddHours(-24);
            for (int i = 0; i < l.Count; i++)
                if (l[i].T >= from || (i + 1 < l.Count && l[i + 1].T > from)) res.Add(l[i]);
            return res;
        }
    }

    // ------------------------------------------------------------------ manettes

    enum Link { None, Bluetooth, Usb }

    sealed class Pad
    {
        public string Path;
        public Link Link;
        public uint DevInst;
        public string Key;     // adresse Bluetooth de la manette (identique en USB et en Bluetooth)
    }

    sealed class Battery
    {
        public int Level;     // 0..10
        public int Status;    // 0 sur batterie, 1 en charge, 2 chargée, autre = erreur
        public int Percent { get { return Level >= 10 ? 100 : (Level <= 0 ? 5 : Level * 10); } }
        public bool Charging { get { return Status == 1 || Status == 2; } }
        public bool Low { get { return Level <= 1 && Status == 0; } }
        public string Describe()
        {
            return Status == 1 ? L.T("en charge", "charging") : (Status == 2 ? L.T("chargée", "charged")
                 : (Status == 0 ? L.T("sur batterie", "on battery") : L.T("erreur de charge", "charging error")));
        }
    }

    static class DualSense
    {
        static readonly Regex BtRx = new Regex(@"\{00001124-[^#]*vid&0002054c_pid&(0ce6|0df2)", RegexOptions.IgnoreCase);
        static readonly Regex UsbRx = new Regex(@"vid_054c&pid_(0ce6|0df2)", RegexOptions.IgnoreCase);
        static readonly Regex AddrRx = new Regex(@"&([0-9A-F]{12})_C", RegexOptions.IgnoreCase);
        static readonly Dictionary<string, string> KeyCache = new Dictionary<string, string>();

        public static Guid HidGuid { get { Guid g; Native.HidD_GetHidGuid(out g); return g; } }

        // Toutes les manettes connectées (une seule entrée par manette, Bluetooth prioritaire)
        public static List<Pad> FindAll()
        {
            List<Pad> bt = new List<Pad>(), usb = new List<Pad>();
            Guid g = HidGuid;
            IntPtr set = Native.SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
            if (set == IntPtr.Zero || set == Native.INVALID) return bt;
            try
            {
                for (uint i = 0; ; i++)
                {
                    Native.SP_DEVICE_INTERFACE_DATA d = new Native.SP_DEVICE_INTERFACE_DATA();
                    d.cbSize = Marshal.SizeOf(d);
                    if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d)) break;
                    Native.SP_DEVINFO_DATA info = new Native.SP_DEVINFO_DATA();
                    info.cbSize = Marshal.SizeOf(info);
                    int required;
                    Native.SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out required, ref info);
                    if (required <= 0) continue;
                    IntPtr buf = Marshal.AllocHGlobal(required);
                    try
                    {
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                        if (!Native.SetupDiGetDeviceInterfaceDetail(set, ref d, buf, required, out required, ref info)) continue;
                        string path = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4));
                        if (path == null) continue;
                        if (BtRx.IsMatch(path)) bt.Add(new Pad { Path = path, Link = Link.Bluetooth, DevInst = info.DevInst });
                        else if (UsbRx.IsMatch(path)) usb.Add(new Pad { Path = path, Link = Link.Usb, DevInst = info.DevInst });
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { Native.SetupDiDestroyDeviceInfoList(set); }

            List<Pad> all = new List<Pad>();
            foreach (Pad p in bt) { p.Key = KeyOf(p); all.Add(p); }
            foreach (Pad p in usb)
            {
                p.Key = KeyOf(p);
                bool dup = false;
                foreach (Pad q in all) if (q.Key == p.Key) dup = true;
                if (!dup) all.Add(p);
            }
            return all;
        }

        static string KeyOf(Pad p)
        {
            lock (KeyCache)
            {
                string k;
                if (KeyCache.TryGetValue(p.Path, out k)) return k;
                ulong a = p.Link == Link.Bluetooth ? BluetoothAddress(p) : UsbAddress(p);
                k = a != 0 ? a.ToString("X12") : "P" + (p.Path.GetHashCode() & 0x7FFFFFFF).ToString("X8");
                KeyCache[p.Path] = k;
                return k;
            }
        }

        // Adresse Bluetooth (via le parent BTHENUM du périphérique HID), 0 si inconnue
        public static ulong BluetoothAddress(Pad pad)
        {
            if (pad == null || pad.Link != Link.Bluetooth) return 0;
            uint parent;
            if (Native.CM_Get_Parent(out parent, pad.DevInst, 0) != 0) return 0;
            StringBuilder sb = new StringBuilder(512);
            if (Native.CM_Get_Device_ID(parent, sb, sb.Capacity, 0) != 0) return 0;
            Match m = AddrRx.Match(sb.ToString());
            return m.Success ? Convert.ToUInt64(m.Groups[1].Value, 16) : 0;
        }

        // En USB, la manette donne son adresse Bluetooth dans le rapport de fonctionnalité 0x09
        static ulong UsbAddress(Pad pad)
        {
            IntPtr h = Native.CreateFile(pad.Path, Native.GENERIC_READ | Native.GENERIC_WRITE, Native.FILE_SHARE_RW, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == Native.INVALID) return 0;
            try
            {
                int inLen, featLen, outLen;
                Caps(h, out inLen, out featLen, out outLen);
                foreach (int len in new[] { 20, featLen, 64 })
                {
                    if (len < 7) continue;
                    byte[] f = new byte[len];
                    f[0] = 0x09;
                    if (!Native.HidD_GetFeature(h, f, len)) continue;
                    ulong a = 0;
                    for (int i = 6; i >= 1; i--) a = (a << 8) | f[i];
                    return a;
                }
                return 0;
            }
            finally { Native.CloseHandle(h); }
        }

        // Coupe la liaison Bluetooth : la DualSense s'éteint d'elle-même
        public static bool PowerOff(ulong addr)
        {
            if (addr == 0) return false;
            Native.BLUETOOTH_FIND_RADIO_PARAMS p = new Native.BLUETOOTH_FIND_RADIO_PARAMS();
            p.dwSize = (uint)Marshal.SizeOf(p);
            IntPtr radio;
            IntPtr find = Native.BluetoothFindFirstRadio(ref p, out radio);
            if (find == IntPtr.Zero) return false;
            try
            {
                int ret;
                return Native.DeviceIoControl(radio, Native.IOCTL_BTH_DISCONNECT_DEVICE, ref addr, 8, IntPtr.Zero, 0, out ret, IntPtr.Zero);
            }
            finally { Native.CloseHandle(radio); Native.BluetoothFindRadioClose(find); }
        }

        // Longueurs des rapports HID (entrée, fonctionnalité, sortie)
        public static void Caps(IntPtr h, out int input, out int feature, out int output)
        {
            input = feature = output = 0;
            IntPtr pp;
            if (!Native.HidD_GetPreparsedData(h, out pp)) return;
            try
            {
                short[] c = new short[64];
                if (Native.HidP_GetCaps(pp, c) == 0x00110000) { input = c[2]; output = c[3]; feature = c[4]; }
            }
            finally { Native.HidD_FreePreparsedData(pp); }
        }

        // En Bluetooth, la manette n'envoie que des rapports simples (sans batterie) tant qu'on ne lui a pas
        // demandé le rapport de fonctionnalité 0x05 : elle passe alors aux rapports complets 0x31.
        // La taille demandée doit être celle annoncée par le pilote (547 octets sous Windows) : c'était la cause
        // de « batterie illisible » dans la version 1, qui demandait 41 octets.
        public static bool RequestFullReports(Pad pad)
        {
            IntPtr h = Native.CreateFile(pad.Path, Native.GENERIC_READ | Native.GENERIC_WRITE, Native.FILE_SHARE_RW, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == Native.INVALID)
            {
                Log.Write("Rapport 0x05 : ouverture impossible (erreur " + Marshal.GetLastWin32Error() + ")");
                return false;
            }
            try
            {
                int inLen, featLen, outLen;
                Caps(h, out inLen, out featLen, out outLen);
                List<int> sizes = new List<int>();
                if (featLen > 0) sizes.Add(featLen);
                foreach (int s in new[] { 41, 64, 78 }) if (!sizes.Contains(s)) sizes.Add(s);
                StringBuilder errs = new StringBuilder();
                foreach (int len in sizes)
                {
                    byte[] f = new byte[len];
                    f[0] = 0x05;
                    if (Native.HidD_GetFeature(h, f, len)) return true;
                    errs.Append(" ").Append(len).Append(":").Append(Marshal.GetLastWin32Error());
                }
                Log.Write("Rapport 0x05 refusé (taille:erreur)" + errs);
                return false;
            }
            finally { Native.CloseHandle(h); }
        }
    }

    // Commandes envoyées à la manette : barre lumineuse et vibrations (rapport de sortie 0x02 en USB, 0x31 en Bluetooth).
    // Important : une commande de lumière seule ne doit pas activer les indicateurs de vibration, sinon la manette l'ignore.
    static class Output
    {
        static readonly object Lock = new object();
        static readonly uint[] CrcTable = MakeCrcTable();
        static int _seq;

        sealed class PulseState { public volatile bool Stop; }
        static readonly Dictionary<string, PulseState> Pulses = new Dictionary<string, PulseState>();

        static uint[] MakeCrcTable()
        {
            uint[] t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[i] = c;
            }
            return t;
        }

        static uint Crc32(byte seed, byte[] d, int n)
        {
            uint c = 0xFFFFFFFF;
            c = CrcTable[(c ^ seed) & 0xFF] ^ (c >> 8);
            for (int i = 0; i < n; i++) c = CrcTable[(c ^ d[i]) & 0xFF] ^ (c >> 8);
            return ~c;
        }

        // Couleur de la barre lumineuse selon le réglage (null = ne rien envoyer, la manette garde sa couleur)
        public static byte[] ColorFor(int setting, Battery b)
        {
            switch (setting)
            {
                case 1: return new byte[] { 0, 0, 40 };
                case 2: return new byte[] { 0, 0, 0 };
                case 3:
                    if (b == null) return null;
                    double f = b.Percent / 100.0;
                    // dégradé : rouge (0 %) → violet (50 %) → bleu (100 %)
                    double[][] stops = { new double[] { 0, 255, 0, 0 }, new double[] { 0.5, 150, 0, 255 }, new double[] { 1, 0, 60, 255 } };
                    for (int i = 1; i < stops.Length; i++)
                        if (f <= stops[i][0])
                        {
                            double k = (f - stops[i - 1][0]) / (stops[i][0] - stops[i - 1][0]);
                            return new byte[] {
                                (byte)(stops[i - 1][1] + (stops[i][1] - stops[i - 1][1]) * k),
                                (byte)(stops[i - 1][2] + (stops[i][2] - stops[i - 1][2]) * k),
                                (byte)(stops[i - 1][3] + (stops[i][3] - stops[i - 1][3]) * k) };
                        }
                    return new byte[] { 0, 60, 255 };
                default: return new byte[] { 0, 0, 255 };
            }
        }

        // Partie commune du rapport (47 octets)
        static byte[] Common(byte[] rgb, bool rumble, byte motor, bool fadeSetup)
        {
            byte[] c = new byte[47];
            c[1] = 0x04;                    // contrôle de la barre lumineuse
            if (rumble)                     // uniquement pour les alertes : une commande de lumière seule ne touche pas aux moteurs
            {
                c[0] = 0x03;                // vibration compatible + sélection haptique
                c[38] = 0x04;               // vibration compatible (micrologiciels récents)
                c[2] = (byte)(motor / 2);   // moteur droit (léger)
                c[3] = motor;               // moteur gauche (fort)
            }
            if (fadeSetup) { c[38] |= 0x02; c[41] = 0x02; }   // éteint l'animation bleue d'origine
            c[44] = rgb[0]; c[45] = rgb[1]; c[46] = rgb[2];
            return c;
        }

        static bool Send(IntPtr h, Pad pad, byte[] common)
        {
            byte[] r;
            if (pad.Link == Link.Bluetooth)
            {
                r = new byte[78];
                r[0] = 0x31;
                r[1] = (byte)((Interlocked.Increment(ref _seq) & 0x0F) << 4);
                r[2] = 0x10;
                Array.Copy(common, 0, r, 3, common.Length);
                uint crc = Crc32(0xA2, r, 74);
                r[74] = (byte)crc; r[75] = (byte)(crc >> 8); r[76] = (byte)(crc >> 16); r[77] = (byte)(crc >> 24);
            }
            else
            {
                r = new byte[48];
                r[0] = 0x02;
                Array.Copy(common, 0, r, 1, common.Length);
            }
            int w;
            return Native.WriteFile(h, r, r.Length, out w, IntPtr.Zero);
        }

        static IntPtr Open(Pad pad)
        {
            IntPtr h = Native.CreateFile(pad.Path, Native.GENERIC_READ | Native.GENERIC_WRITE, Native.FILE_SHARE_RW, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == Native.INVALID) h = Native.CreateFile(pad.Path, Native.GENERIC_WRITE, Native.FILE_SHARE_RW, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            return h;
        }

        // Séquence de commandes sur un thread d'arrière-plan ; « done » est appelé à la fin (même en cas d'échec)
        static void Run(Pad pad, Action<Func<byte[], bool>> script, Action done)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                lock (Lock)
                {
                    IntPtr h = Open(pad);
                    if (h == Native.INVALID) Log.Write("Commande à la manette impossible (erreur " + Marshal.GetLastWin32Error() + ")");
                    else
                    {
                        try { script(delegate (byte[] common) { return Send(h, pad, common); }); }
                        catch (Exception e) { Log.Write("Commande à la manette : " + e.Message); }
                        finally { Native.CloseHandle(h); }
                    }
                }
                if (done != null) done();
            });
        }

        public static void SetLight(Pad pad, byte[] rgb, bool firstTime)
        {
            Run(pad, delegate (Func<byte[], bool> send)
            {
                if (firstTime) { send(Common(rgb, false, 0, true)); Thread.Sleep(60); }
                if (!send(Common(rgb, false, 0, false))) Log.Write("Barre lumineuse : envoi refusé (erreur " + Marshal.GetLastWin32Error() + ")");
            }, null);
        }

        // Alerte batterie : vibrations + barre lumineuse rouge clignotante
        public static void Alert(Pad pad, int flashes, Action done)
        {
            byte[] red = { 255, 0, 0 }, off = { 0, 0, 0 };
            Run(pad, delegate (Func<byte[], bool> send)
            {
                for (int i = 0; i < flashes; i++)
                {
                    send(Common(red, true, 190, false)); Thread.Sleep(180);
                    send(Common(off, true, 0, false)); Thread.Sleep(180);
                }
                send(Common(red, true, 0, false)); Thread.Sleep(900);
            }, done);
        }

        // « Identifier » : la manette clignote en blanc avec une petite vibration, pour savoir laquelle est laquelle
        public static void Identify(Pad pad, Action done)
        {
            byte[] white = { 255, 255, 255 }, off = { 0, 0, 0 };
            Run(pad, delegate (Func<byte[], bool> send)
            {
                for (int i = 0; i < 4; i++)
                {
                    send(Common(white, true, i == 0 ? (byte)120 : (byte)0, false)); Thread.Sleep(170);
                    send(Common(off, true, 0, false)); Thread.Sleep(170);
                }
            }, done);
        }

        // Pulsation orange (batterie faible) : un rapport toutes les 60 ms tant qu'elle est active
        public static void SetPulse(Pad pad, bool on)
        {
            lock (Pulses)
            {
                PulseState st;
                bool has = Pulses.TryGetValue(pad.Path, out st);
                if (on && !has)
                {
                    st = new PulseState();
                    Pulses[pad.Path] = st;
                    PulseState s = st;
                    Thread t = new Thread(delegate () { PulseLoop(pad, s); });
                    t.IsBackground = true;
                    t.Name = "Pulsation";
                    t.Start();
                }
                else if (!on && has)
                {
                    st.Stop = true;
                    Pulses.Remove(pad.Path);
                }
            }
        }

        static void PulseLoop(Pad pad, PulseState st)
        {
            IntPtr h = Open(pad);
            if (h == Native.INVALID) return;
            Stopwatch sw = Stopwatch.StartNew();
            int fails = 0;
            try
            {
                while (!st.Stop && fails < 20)
                {
                    double k = 0.5 - 0.5 * Math.Cos(sw.Elapsed.TotalSeconds * 2 * Math.PI / 1.6);
                    byte[] rgb = { (byte)(35 + 220 * k), (byte)(8 + 82 * k), 0 };
                    lock (Lock)
                    {
                        if (st.Stop) break;
                        if (!Send(h, pad, Common(rgb, false, 0, false))) fails++; else fails = 0;
                    }
                    Thread.Sleep(60);
                }
            }
            finally { Native.CloseHandle(h); }
        }
    }

    // Lit une manette en continu (10 fois par seconde) sur un thread d'arrière-plan :
    // niveau de batterie, dernière activité (boutons, sticks, gâchettes, pavé tactile), appui long sur PS.
    sealed class PadReader
    {
        public readonly Pad Pad;
        public readonly DateTime StartedUtc = DateTime.UtcNow;
        public Action<Battery> BatteryChanged;   // appelé depuis le thread de lecture
        public Action Stopped;                   // idem
        public Action PsHeld;                    // idem : bouton PS maintenu ~1 s

        volatile bool _stop, _dead;
        IntPtr _h = Native.INVALID;
        long _activity = DateTime.UtcNow.Ticks, _lastReport;

        public PadReader(Pad pad) { Pad = pad; }

        public bool Dead { get { return _dead; } }
        public DateTime LastActivityUtc { get { return new DateTime(Interlocked.Read(ref _activity), DateTimeKind.Utc); } }
        public DateTime LastReportUtc { get { return new DateTime(Interlocked.Read(ref _lastReport), DateTimeKind.Utc); } }

        public void Start()
        {
            Thread t = new Thread(Run);
            t.IsBackground = true;
            t.Name = "Lecture DualSense";
            t.Start();
        }

        public void Stop()
        {
            _stop = true;
            IntPtr h = _h;
            if (h != Native.INVALID) Native.CancelIoEx(h, IntPtr.Zero);
        }

        void Run()
        {
            bool bt = Pad.Link == Link.Bluetooth;
            IntPtr h = Native.INVALID, ev = IntPtr.Zero, ov = IntPtr.Zero, buf = IntPtr.Zero;
            try
            {
                h = Native.CreateFile(Pad.Path, Native.GENERIC_READ | Native.GENERIC_WRITE, Native.FILE_SHARE_RW, IntPtr.Zero, Native.OPEN_EXISTING, Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (h == Native.INVALID) h = Native.CreateFile(Pad.Path, Native.GENERIC_READ, Native.FILE_SHARE_RW, IntPtr.Zero, Native.OPEN_EXISTING, Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (h == Native.INVALID) { Log.Write("Ouverture de la manette impossible (erreur " + Marshal.GetLastWin32Error() + ")"); return; }
                _h = h;
                if (_stop) return;

                int inLen, featLen, outLen;
                DualSense.Caps(h, out inLen, out featLen, out outLen);
                int len = inLen > 0 ? inLen : (bt ? 78 : 64);
                Log.Write("Lecture " + (bt ? "Bluetooth" : "USB") + " : rapports de " + inLen + " octets, fonctionnalité " + featLen);
                Native.HidD_SetNumInputBuffers(h, 2);   // on veut l'état récent, pas un historique
                if (bt && !DualSense.RequestFullReports(Pad)) Log.Write("Passage en rapports complets non confirmé");

                ev = Native.CreateEvent(IntPtr.Zero, true, false, null);
                ov = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeOverlapped)));
                buf = Marshal.AllocHGlobal(len);
                byte[] data = new byte[len];
                byte[] cur = new byte[10], reference = null;
                int simple = 0, retries = 0, timeouts = 0;
                Battery reported = null;
                int pendLevel = -1, pendStatus = -1, pendCount = 0;
                bool firstLogged = false, psFired = false;
                long psSince = 0;

                while (!_stop)
                {
                    Native.ResetEvent(ev);
                    NativeOverlapped o = new NativeOverlapped();
                    o.EventHandle = ev;
                    Marshal.StructureToPtr(o, ov, false);
                    int n;
                    if (!Native.ReadFile(h, buf, len, IntPtr.Zero, ov))
                    {
                        int err = Marshal.GetLastWin32Error();
                        if (err != 997) { if (!_stop) Log.Write("Lecture interrompue (erreur " + err + ")"); break; }
                        if (Native.WaitForSingleObject(ev, 3000) != 0)
                        {
                            Native.CancelIoEx(h, ov);
                            Native.GetOverlappedResult(h, ov, out n, true);
                            if (_stop) break;
                            if (++timeouts == 3) Log.Write("Aucun rapport reçu depuis 9 s");
                            continue;
                        }
                    }
                    if (!Native.GetOverlappedResult(h, ov, out n, false))
                    {
                        if (!_stop) Log.Write("Lecture interrompue (erreur " + Marshal.GetLastWin32Error() + ")");
                        break;
                    }
                    timeouts = 0;
                    Marshal.Copy(buf, data, 0, Math.Min(n, len));
                    long now = DateTime.UtcNow.Ticks;
                    Interlocked.Exchange(ref _lastReport, now);

                    int id = data[0];
                    if (!firstLogged) { firstLogged = true; Log.Write("Premier rapport : 0x" + id.ToString("X2") + ", " + n + " octets"); }

                    bool full;
                    if (bt && id == 0x31) full = true;
                    else if (!bt && id == 0x01) full = true;
                    else if (bt && id == 0x01) full = false;
                    else { Thread.Sleep(100); continue; }

                    if (full)
                    {
                        int b = bt ? 2 : 1;
                        for (int i = 0; i < 6; i++) cur[i] = data[b + i];       // sticks, gâchettes
                        cur[6] = data[b + 7]; cur[7] = data[b + 8];               // boutons
                        cur[8] = (byte)(data[b + 9] & 0x07);                      // PS, pavé, micro
                        cur[9] = (byte)((data[b + 32] & 0x80) == 0 ? 1 : 0);      // doigt sur le pavé tactile

                        byte st = data[b + 52];
                        int level = Math.Min(10, st & 0x0F), status = (st >> 4) & 0x0F;
                        if (reported == null || level != reported.Level || status != reported.Status)
                        {
                            if (level == pendLevel && status == pendStatus) pendCount++;
                            else { pendLevel = level; pendStatus = status; pendCount = 1; }
                            if (reported == null || pendCount >= 3)   // anti-scintillement entre deux paliers
                            {
                                reported = new Battery { Level = level, Status = status };
                                if (BatteryChanged != null) BatteryChanged(reported);
                            }
                        }
                        else pendCount = 0;
                    }
                    else
                    {
                        // rapport simple Bluetooth : sticks 1-4, boutons 5-7, gâchettes 8-9, pas de batterie
                        cur[0] = data[1]; cur[1] = data[2]; cur[2] = data[3]; cur[3] = data[4];
                        cur[4] = data[8]; cur[5] = data[9];
                        cur[6] = data[5]; cur[7] = data[6]; cur[8] = (byte)(data[7] & 0x03); cur[9] = 0;
                        if (simple++ == 0) Log.Write("Rapports simples en Bluetooth (pas de batterie) pour l'instant");
                        if (simple % 50 == 0 && retries < 5) { retries++; DualSense.RequestFullReports(Pad); }
                    }

                    // bouton PS maintenu ~1 s
                    if ((cur[8] & 0x01) != 0)
                    {
                        if (psSince == 0) psSince = now;
                        else if (!psFired && now - psSince >= TimeSpan.FromMilliseconds(900).Ticks)
                        {
                            psFired = true;
                            if (PsHeld != null) PsHeld();
                        }
                    }
                    else { psSince = 0; psFired = false; }

                    if (reference == null) reference = (byte[])cur.Clone();
                    else
                    {
                        bool active = cur[9] == 1;
                        for (int i = 0; i < 6 && !active; i++) active = Math.Abs(cur[i] - reference[i]) > 12;
                        for (int i = 6; i < 9 && !active; i++) active = cur[i] != reference[i];
                        if (active)
                        {
                            Array.Copy(cur, reference, cur.Length);
                            Interlocked.Exchange(ref _activity, now);
                        }
                    }

                    Thread.Sleep(100);
                }
            }
            catch (Exception e) { Log.Write("Erreur de lecture : " + e.Message); }
            finally
            {
                _h = Native.INVALID;
                if (h != Native.INVALID) Native.CloseHandle(h);
                if (ev != IntPtr.Zero) Native.CloseHandle(ev);
                if (ov != IntPtr.Zero) Marshal.FreeHGlobal(ov);
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
                _dead = true;
                if (!_stop && Stopped != null) Stopped();
            }
        }
    }

    // Fenêtre invisible qui reçoit les notifications d'arrivée/retrait de périphériques HID
    sealed class DeviceWatcher : WF.NativeWindow, IDisposable
    {
        IntPtr _reg;
        public event EventHandler Changed;

        public DeviceWatcher()
        {
            CreateHandle(new WF.CreateParams());
            Native.DEV_BROADCAST_DEVICEINTERFACE f = new Native.DEV_BROADCAST_DEVICEINTERFACE();
            f.dbcc_size = Marshal.SizeOf(f);
            f.dbcc_devicetype = Native.DBT_DEVTYP_DEVICEINTERFACE;
            f.dbcc_classguid = DualSense.HidGuid;
            IntPtr buf = Marshal.AllocHGlobal(f.dbcc_size);
            try { Marshal.StructureToPtr(f, buf, false); _reg = Native.RegisterDeviceNotification(Handle, buf, 0); }
            finally { Marshal.FreeHGlobal(buf); }
        }

        protected override void WndProc(ref WF.Message m)
        {
            if (m.Msg == Native.WM_DEVICECHANGE)
            {
                int ev = m.WParam.ToInt32();
                if ((ev == Native.DBT_DEVICEARRIVAL || ev == Native.DBT_DEVICEREMOVECOMPLETE) && Changed != null) Changed(this, EventArgs.Empty);
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (_reg != IntPtr.Zero) { Native.UnregisterDeviceNotification(_reg); _reg = IntPtr.Zero; }
            DestroyHandle();
        }
    }

    // ------------------------------------------------------------------ thème, dessins, menus, notifications

    sealed class Theme
    {
        public bool Light;
        public SD.Color Accent;   // couleur d'accentuation telle que Windows l'utilise pour remplir (Light2 en sombre, Dark1 en clair)

        public static Theme Current()
        {
            Theme t = new Theme();
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("SystemUsesLightTheme");
                    t.Light = v is int && (int)v == 1;
                }
            }
            catch { }
            t.Accent = t.Light ? SD.Color.FromArgb(0, 95, 184) : SD.Color.FromArgb(96, 205, 255);
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent"))
                {
                    byte[] p = k == null ? null : k.GetValue("AccentPalette") as byte[];
                    if (p != null && p.Length >= 32)
                    {
                        int i = t.Light ? 4 * 4 : 1 * 4;
                        t.Accent = SD.Color.FromArgb(p[i], p[i + 1], p[i + 2]);
                    }
                }
            }
            catch { }
            return t;
        }

        public SD.Color Foreground { get { return Light ? SD.Color.FromArgb(26, 26, 26) : SD.Color.White; } }
        public SD.Color Critical { get { return Light ? SD.Color.FromArgb(196, 43, 28) : SD.Color.FromArgb(255, 153, 164); } }

        public static string Hex(SD.Color c) { return "#" + c.A.ToString("X2") + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }

        // Jetons de couleur repris de WinUI 3 (Fluent)
        public string Xaml(string xaml)
        {
            string[] dark = { "$TP$", "#FFFFFFFF", "$TS$", "#C5FFFFFF", "$TT$", "#87FFFFFF", "$FILL$", "#0FFFFFFF", "$FILLH$", "#15FFFFFF", "$FILLP$", "#08FFFFFF",
                              "$STROKE$", "#12FFFFFF", "$DIV$", "#15FFFFFF", "$TRACK$", "#8BFFFFFF", "$FOOT$", "#26000000",
                              "$SUBH$", "#0FFFFFFF", "$SUBP$", "#0AFFFFFF", "$ONACC$", "#FF000000", "$TIP$", "#FF2C2C2C",
                              "$CARD$", "#0DFFFFFF", "$CARDS$", "#19000000", "$RING$", "#2EFFFFFF", "$INPUT$", "#B31E1E1E" };
            string[] light = { "$TP$", "#E4000000", "$TS$", "#9E000000", "$TT$", "#72000000", "$FILL$", "#B3FFFFFF", "$FILLH$", "#80F9F9F9", "$FILLP$", "#4DF9F9F9",
                               "$STROKE$", "#0F000000", "$DIV$", "#0F000000", "$TRACK$", "#72000000", "$FOOT$", "#0A000000",
                               "$SUBH$", "#09000000", "$SUBP$", "#06000000", "$ONACC$", "#FFFFFFFF", "$TIP$", "#FFF9F9F9",
                               "$CARD$", "#B3FFFFFF", "$CARDS$", "#0F000000", "$RING$", "#1F000000", "$INPUT$", "#FFFFFFFF" };
            string[] t = Light ? light : dark;
            xaml = L.Xaml(xaml);
            for (int i = 0; i < t.Length; i += 2) xaml = xaml.Replace(t[i], t[i + 1]);
            return xaml.Replace("$ACC$", Hex(Accent)).Replace("$ICONS$", Art.IconFont);
        }

        public WM.Brush Brush(string token)
        {
            string hex = token.StartsWith("#") ? token : Xaml(token);
            WM.Brush b = (WM.Brush)new WM.BrushConverter().ConvertFromString(hex);
            b.Freeze();
            return b;
        }
    }

    static class Art
    {
        public static readonly string IconFont = FontExists("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        public const string Controller = "";

        static bool FontExists(string name)
        {
            try { using (SD.FontFamily f = new SD.FontFamily(name)) return true; }
            catch { return false; }
        }

        public static string BatteryGlyph(int level, bool charging)
        {
            level = Math.Max(0, Math.Min(10, level));
            if (charging) return level == 10 ? "" : ((char)(0xE85A + level)).ToString();
            return level == 10 ? "" : ((char)(0xE850 + level)).ToString();
        }

        // Texte centré dans un carré (centrage calculé sur « reference » pour garder la même position d'un niveau à l'autre)
        static void DrawCentered(SD.Graphics g, string text, string reference, SD.FontFamily ff, SD.FontStyle style, int size, float em, SD.Color color)
        {
            using (SD.Font font = new SD.Font(ff, em, style, SD.GraphicsUnit.Pixel))
            using (SD2.GraphicsPath path = new SD2.GraphicsPath())
            using (SD.StringFormat fmt = (SD.StringFormat)SD.StringFormat.GenericTypographic.Clone())
            using (SD.SolidBrush br = new SD.SolidBrush(color))
            {
                g.TextRenderingHint = SD.Text.TextRenderingHint.AntiAliasGridFit;
                path.AddString(reference ?? text, ff, (int)style, em, SD.PointF.Empty, fmt);
                SD.RectangleF r = path.GetBounds();
                float ox = (float)Math.Round((size - r.Width) / 2f - r.X), oy = (float)Math.Round((size - r.Height) / 2f - r.Y);
                g.DrawString(text, font, br, ox, oy, fmt);
            }
        }

        public static SD.Bitmap Glyph(string glyph, string reference, int size, float em, SD.Color color)
        {
            SD.Bitmap bmp = new SD.Bitmap(size, size, SDI.PixelFormat.Format32bppArgb);
            using (SD.Graphics g = SD.Graphics.FromImage(bmp))
            using (SD.FontFamily ff = new SD.FontFamily(IconFont))
            {
                g.Clear(SD.Color.Transparent);
                DrawCentered(g, glyph, reference, ff, SD.FontStyle.Regular, size, em, color);
            }
            return bmp;
        }

        // Icône de la zone de notification : 0 = batterie Windows, 1 = pourcentage, 2 = anneau
        public static SD.Bitmap TrayBitmap(int size, Link mode, Battery b, Theme th, int style)
        {
            if (mode == Link.None || b == null)
            {
                SD.Color fg = th.Foreground;
                if (mode == Link.None) fg = SD.Color.FromArgb(120, fg);
                return Glyph(Controller, null, size, size, fg);
            }
            SD.Color c = b.Low ? th.Critical : th.Foreground;
            if (style == 1) return NumberIcon(size, b.Percent, c);
            if (style == 2) return RingIcon(size, b, c, th.Foreground);
            return Glyph(BatteryGlyph(b.Level, b.Charging), BatteryGlyph(0, false), size, size, c);
        }

        // Chiffres de l'icône « Pourcentage » : tracés vectoriels de la même hauteur pour 5, 40 ou 100,
        // espacement serré et régulier, hauteur et position calées sur des pixels entiers pour rester nets.
        const string NumberFamily = "Segoe UI";
        const float NumEm = 100f, NumGap = 8f, NumNarrow = 0.82f;   // Segoe UI Bold légèrement resserré : « 4 » fermé, lisible à 16 px

        static SD2.GraphicsPath DigitPath(SD.FontFamily ff, SD.FontStyle style, string s)
        {
            SD2.GraphicsPath p = new SD2.GraphicsPath();
            p.AddString(s, ff, (int)style, NumEm, SD.PointF.Empty, SD.StringFormat.GenericTypographic);
            return p;
        }

        // Largeur d'un nombre quand ses chiffres sont posés encre contre encre, séparés de NumGap
        static float NumberWidth(SD.FontFamily ff, SD.FontStyle style, string s)
        {
            float w = 0;
            foreach (char ch in s)
                using (SD2.GraphicsPath p = DigitPath(ff, style, ch.ToString())) w += p.GetBounds().Width;
            return w + NumGap * (s.Length - 1);
        }

        static SD.Bitmap NumberIcon(int size, int percent, SD.Color c)
        {
            string s = percent.ToString(CultureInfo.InvariantCulture);
            SD.Bitmap bmp = new SD.Bitmap(size, size, SDI.PixelFormat.Format32bppArgb);
            SD.FontStyle style = SD.FontStyle.Bold;
            using (SD.FontFamily ff = new SD.FontFamily(NumberFamily))
            using (SD.Graphics g = SD.Graphics.FromImage(bmp))
            using (SD.SolidBrush br = new SD.SolidBrush(c))
            {
                g.Clear(SD.Color.Transparent);
                g.SmoothingMode = SD2.SmoothingMode.AntiAlias;
                g.PixelOffsetMode = SD2.PixelOffsetMode.HighQuality;

                // même boîte verticale pour tous les chiffres, taille fixée par « 100 » (le plus large)
                SD.RectangleF digits;
                using (SD2.GraphicsPath all = DigitPath(ff, style, "0123456789")) digits = all.GetBounds();
                float scale = Math.Min(size / (NumberWidth(ff, style, "100") * NumNarrow), size * 0.7f / digits.Height);
                float h = (float)Math.Floor(digits.Height * scale);
                scale = h / digits.Height;
                float x = (size - NumberWidth(ff, style, s) * NumNarrow * scale) / 2f;
                float top = (float)Math.Round((size - h) / 2f);

                foreach (char ch in s)
                    using (SD2.GraphicsPath p = DigitPath(ff, style, ch.ToString()))
                    {
                        SD.RectangleF b = p.GetBounds();
                        using (SD2.Matrix m = new SD2.Matrix())
                        {
                            m.Translate((float)Math.Round(x), top);   // chaque chiffre démarre sur un pixel entier
                            m.Scale(scale * NumNarrow, scale);
                            m.Translate(-b.X, -digits.Y);
                            p.Transform(m);
                        }
                        g.FillPath(br, p);
                        x += (b.Width + NumGap) * NumNarrow * scale;
                    }
            }
            return bmp;
        }

        // Éclair vectoriel de hauteur h centré sur (cx, cy) : forme dessinée à la main, centrée géométriquement
        static void FillBolt(SD.Graphics g, SD.Color c, float cx, float cy, float h)
        {
            float[] u = { 0.60f, 0f, 0.13f, 0.58f, 0.46f, 0.58f, 0.40f, 1f, 0.87f, 0.42f, 0.54f, 0.42f };
            SD.PointF[] p = new SD.PointF[u.Length / 2];
            for (int i = 0; i < p.Length; i++) p[i] = new SD.PointF(cx + (u[2 * i] - 0.5f) * h, cy + (u[2 * i + 1] - 0.5f) * h);
            using (SD.SolidBrush br = new SD.SolidBrush(c)) g.FillPolygon(br, p);
        }

        static SD.Bitmap RingIcon(int size, Battery b, SD.Color c, SD.Color fg)
        {
            SD.Bitmap bmp = new SD.Bitmap(size, size, SDI.PixelFormat.Format32bppArgb);
            using (SD.Graphics g = SD.Graphics.FromImage(bmp))
            {
                g.Clear(SD.Color.Transparent);
                g.SmoothingMode = SD2.SmoothingMode.AntiAlias;
                g.PixelOffsetMode = SD2.PixelOffsetMode.HighQuality;
                float t = Math.Max(2f, (float)Math.Round(size / 8f * 2f) / 2f);   // 16 → 2, 20 → 2,5, 24 → 3, 32 → 4
                float inset = t / 2f + 0.5f;
                SD.RectangleF r = new SD.RectangleF(inset, inset, size - 2 * inset, size - 2 * inset);
                using (SD.Pen track = new SD.Pen(SD.Color.FromArgb(70, fg), t))
                using (SD.Pen arc = new SD.Pen(c, t))
                {
                    arc.StartCap = arc.EndCap = SD2.LineCap.Round;
                    g.DrawEllipse(track, r);
                    float sweep = 360f * Math.Max(0.04f, b.Level / 10f);
                    if (sweep >= 359.9f) g.DrawEllipse(arc, r); else g.DrawArc(arc, r, -90, sweep);
                }
                if (b.Charging)
                {
                    float inner = size - 2 * (inset + t / 2f);
                    FillBolt(g, c, size / 2f, size / 2f, inner * 0.56f);
                }
            }
            return bmp;
        }

        // Icône d'application : manette blanche sur carré arrondi bleu
        public static SD.Bitmap AppIcon(int size)
        {
            SD.Bitmap bmp = new SD.Bitmap(size, size, SDI.PixelFormat.Format32bppArgb);
            using (SD.Graphics g = SD.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SD2.SmoothingMode.AntiAlias;
                g.Clear(SD.Color.Transparent);
                float r = size * 0.22f, s = size - 1;
                using (SD2.GraphicsPath p = new SD2.GraphicsPath())
                using (SD2.LinearGradientBrush br = new SD2.LinearGradientBrush(new SD.PointF(0, 0), new SD.PointF(0, size), SD.Color.FromArgb(0, 120, 212), SD.Color.FromArgb(0, 90, 170)))
                {
                    p.AddArc(0, 0, 2 * r, 2 * r, 180, 90);
                    p.AddArc(s - 2 * r, 0, 2 * r, 2 * r, 270, 90);
                    p.AddArc(s - 2 * r, s - 2 * r, 2 * r, 2 * r, 0, 90);
                    p.AddArc(0, s - 2 * r, 2 * r, 2 * r, 90, 90);
                    p.CloseFigure();
                    g.FillPath(br, p);
                }
                using (SD.Bitmap glyph = Glyph(Controller, null, size, size * 0.62f, SD.Color.White))
                    g.DrawImage(glyph, 0, 0);
            }
            return bmp;
        }

        public static void WriteIco(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
            List<byte[]> pngs = new List<byte[]>();
            foreach (int s in sizes)
                using (SD.Bitmap b = AppIcon(s))
                using (MemoryStream ms = new MemoryStream()) { b.Save(ms, SDI.ImageFormat.Png); pngs.Add(ms.ToArray()); }
            using (BinaryWriter w = new BinaryWriter(File.Create(path)))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                    w.Write(pngs[i].Length); w.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (byte[] p in pngs) w.Write(p);
            }
        }
    }

    // Menus contextuels natifs Windows (arrondis, thème clair/sombre comme ceux du système)
    static class NativeMenu
    {
        const uint MF_STRING = 0, MF_CHECKED = 0x8, MF_SEPARATOR = 0x800, MF_BYCOMMAND = 0;
        const uint TPM_RIGHTALIGN = 0x8, TPM_RIGHTBUTTON = 0x2, TPM_BOTTOMALIGN = 0x20, TPM_NONOTIFY = 0x80, TPM_RETURNCMD = 0x100;

        // Renvoie l'index de l'élément choisi, ou -1. radio : élément coché en « puce » ; checks : cases cochées.
        public static int Show(IntPtr owner, int x, int y, string[] items, int radio, bool[] checks, bool light, bool above, bool rightAlign)
        {
            try { Native.SetPreferredAppMode(light ? 3 : 2); Native.FlushMenuThemes(); } catch { }
            IntPtr m = Native.CreatePopupMenu();
            try
            {
                for (int i = 0; i < items.Length; i++)
                {
                    if (items[i] == null) Native.AppendMenu(m, MF_SEPARATOR, UIntPtr.Zero, null);
                    else Native.AppendMenu(m, MF_STRING | (checks != null && checks[i] ? MF_CHECKED : 0), new UIntPtr((uint)(i + 1)), items[i]);
                }
                if (radio >= 0) Native.CheckMenuRadioItem(m, 1, (uint)items.Length, (uint)(radio + 1), MF_BYCOMMAND);
                Native.SetForegroundWindow(owner);
                uint flags = TPM_RETURNCMD | TPM_NONOTIFY | TPM_RIGHTBUTTON | (above ? TPM_BOTTOMALIGN : 0) | (rightAlign ? TPM_RIGHTALIGN : 0);
                int cmd = Native.TrackPopupMenuEx(m, flags, x, y, owner, IntPtr.Zero);
                Native.PostMessage(owner, 0, IntPtr.Zero, IntPtr.Zero);
                return cmd - 1;
            }
            finally { Native.DestroyMenu(m); }
        }
    }

    // Notifications Windows natives au nom de « Manette PS5 » (repli : bulle de la zone de notification)
    static class Toasts
    {
        const string Aumid = "DualSenseBattery.ManettePS5";
        static bool _ready;

        public static void Init()
        {
            try
            {
                string png = Path.Combine(Log.Dir, "icone.png");
                using (SD.Bitmap b = Art.AppIcon(64)) b.Save(png, SDI.ImageFormat.Png);
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + Aumid))
                {
                    k.SetValue("DisplayName", L.AppName);
                    k.SetValue("IconUri", png);
                }
                _ready = true;
            }
            catch (Exception e) { Log.Write("Notifications natives indisponibles : " + e.Message); }
        }

        // Retire nos notifications de l'écran (et du centre de notifications) : le panneau montre déjà la même chose
        public static void ClearAll()
        {
            if (!_ready) return;
            try { global::Windows.UI.Notifications.ToastNotificationManager.History.Clear(Aumid); }
            catch (Exception e) { Log.Write("Effacement des notifications : " + e.Message); }
        }

        // percent >= 0 : ajoute une barre de niveau sous le texte
        public static bool Show(string title, string body, bool urgent, int percent, string progressStatus)
        {
            if (!_ready) return false;
            try { ShowCore(title, body, urgent, percent, progressStatus); return true; }
            catch (Exception e) { Log.Write("Échec de la notification : " + e.Message); return false; }
        }

        static void ShowCore(string title, string body, bool urgent, int percent, string progressStatus)
        {
            StringBuilder x = new StringBuilder();
            x.Append("<toast").Append(urgent ? " scenario='urgent'" : "").Append("><visual><binding template='ToastGeneric'>");
            x.Append("<text>").Append(SecurityElement.Escape(title)).Append("</text>");
            if (!string.IsNullOrEmpty(body)) x.Append("<text>").Append(SecurityElement.Escape(body)).Append("</text>");
            if (percent >= 0)
                x.Append("<progress value='").Append((percent / 100.0).ToString("0.00", CultureInfo.InvariantCulture))
                 .Append("' valueStringOverride='").Append(percent).Append(Fmt.Pct)
                 .Append("' status='").Append(SecurityElement.Escape(progressStatus ?? "")).Append("'/>");
            x.Append("</binding></visual></toast>");
            global::Windows.Data.Xml.Dom.XmlDocument doc = new global::Windows.Data.Xml.Dom.XmlDocument();
            doc.LoadXml(x.ToString());
            global::Windows.UI.Notifications.ToastNotification t = new global::Windows.UI.Notifications.ToastNotification(doc);
            global::Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier(Aumid).Show(t);
        }
    }

    // Raccourci « Manette PS5 » dans le menu Démarrer : rouvre le panneau quand l'icône est masquée
    static class StartMenu
    {
        public static void Ensure()
        {
            try
            {
                string exe = Assembly.GetEntryAssembly().Location;
                string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), L.AppName + ".lnk");
                if (File.Exists(lnk)) return;
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                Type st = sc.GetType();
                st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { exe });
                st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Log.Dir });
                st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { exe + ",0" });
                st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { L.T("Batterie de la manette PS5", "PS5 controller battery") });
                st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
                Marshal.FinalReleaseComObject(sc);
                Marshal.FinalReleaseComObject(shell);
                Log.Write("Raccourci créé dans le menu Démarrer");
            }
            catch (Exception e) { Log.Write("Raccourci du menu Démarrer : " + e.Message); }
        }
    }

    // ------------------------------------------------------------------ éléments visuels

    // Anneau de batterie (piste discrète + arc arrondi), avec contenu centré ; se remplit en s'animant
    sealed class Ring : WC.Grid
    {
        readonly double _size, _thick;
        readonly WS.Path _arc;
        public readonly WC.StackPanel Center;
        double _shown, _target;
        DateTime _animStart;
        bool _animating;

        public Ring(double size, double thick, WM.Brush track)
        {
            _size = size; _thick = thick;
            Width = size; Height = size;
            Children.Add(new WS.Ellipse { Stroke = track, StrokeThickness = thick });
            _arc = new WS.Path { StrokeThickness = thick, StrokeStartLineCap = WM.PenLineCap.Round, StrokeEndLineCap = WM.PenLineCap.Round };
            Children.Add(_arc);
            Center = new WC.StackPanel { HorizontalAlignment = W.HorizontalAlignment.Center, VerticalAlignment = W.VerticalAlignment.Center };
            Children.Add(Center);
        }

        public void Set(double f, WM.Brush brush, bool animate)
        {
            _arc.Stroke = brush;
            _target = Math.Max(0, Math.Min(1, f));
            if (!animate) { _shown = _target; Draw(_shown); return; }
            _animStart = DateTime.UtcNow;
            if (!_animating) { _animating = true; WM.CompositionTarget.Rendering += OnFrame; }
        }

        double _from;
        void OnFrame(object s, EventArgs e)
        {
            double t = Math.Min(1, (DateTime.UtcNow - _animStart).TotalMilliseconds / 650);
            double k = 1 - Math.Pow(1 - t, 3);
            Draw(_from + (_target - _from) * k);
            if (t >= 1) { _shown = _target; _from = _target; _animating = false; WM.CompositionTarget.Rendering -= OnFrame; }
        }

        void Draw(double f)
        {
            if (f <= 0.001) { _arc.Data = null; return; }
            double r = (_size - _thick) / 2, c = _size / 2;
            WM.PathFigure fig = new WM.PathFigure { StartPoint = new W.Point(c, c - r) };
            if (f >= 0.999)
            {
                fig.Segments.Add(new WM.ArcSegment(new W.Point(c, c + r), new W.Size(r, r), 0, false, WM.SweepDirection.Clockwise, true));
                fig.Segments.Add(new WM.ArcSegment(new W.Point(c, c - r), new W.Size(r, r), 0, false, WM.SweepDirection.Clockwise, true));
            }
            else
            {
                double a = 2 * Math.PI * f;
                W.Point end = new W.Point(c + r * Math.Sin(a), c - r * Math.Cos(a));
                fig.Segments.Add(new WM.ArcSegment(end, new W.Size(r, r), 0, f > 0.5, WM.SweepDirection.Clockwise, true));
            }
            WM.PathGeometry g = new WM.PathGeometry();
            g.Figures.Add(fig);
            _arc.Data = g;
        }
    }

    // ------------------------------------------------------------------ panneau (clic gauche)

    sealed class PadView
    {
        public string Key, Name;
        public Link Mode;               // None = manette connue mais éteinte
        public Battery Battery;
        public string Remaining, ChargeRemaining;
        public double RemainingMinutes = -1;
        public List<Sample> History;
        public bool PoweringOff;
        public int LastPercent = -1;
        public DateTime LastSeen;
    }

    sealed class View
    {
        public List<PadView> Pads = new List<PadView>();
        public int Connected;
        public int AutoOff, Lightbar, TrayStyle;
        public bool GameOverlay, HideWhenOff, StartWithWindows;
    }

    // Une manette dans le panneau : carte avec anneau, nom, état, actions en icônes, graphique 24 h
    sealed class PadCard
    {
        const string Markup = @"
<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        CornerRadius='8' Background='$CARD$' BorderBrush='$CARDS$' BorderThickness='1' Padding='14,14,10,14'>
  <StackPanel>
    <Grid>
      <Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
      <Border x:Name='RingHost'/>
      <StackPanel Grid.Column='1' Margin='14,0,6,0' VerticalAlignment='Center'>
        <Grid>
          <TextBlock x:Name='Name' FontWeight='SemiBold' TextTrimming='CharacterEllipsis'/>
          <TextBox x:Name='NameBox' Visibility='Collapsed' FontWeight='SemiBold' FontSize='14' Padding='4,2' MaxLength='24'
                   Background='$INPUT$' Foreground='$TP$' CaretBrush='$TP$' SelectionBrush='$ACC$' BorderBrush='$ACC$' BorderThickness='0,0,0,2'/>
        </Grid>
        <TextBlock x:Name='Sub' FontSize='12' Foreground='$TS$' TextWrapping='Wrap' Margin='0,3,0,0'/>
      </StackPanel>
      <StackPanel Grid.Column='2' Orientation='Horizontal' VerticalAlignment='Center'>
        <Button x:Name='Rename' Style='{DynamicResource Icon}' ToolTip='[[Renommer|Rename]]'><TextBlock Text='&#xE70F;' FontFamily='$ICONS$' FontSize='15'/></Button>
        <Button x:Name='Identify' Style='{DynamicResource Icon}' ToolTip='[[Identifier (la manette clignote)|Identify (the controller flashes)]]'><TextBlock Text='&#xE781;' FontFamily='$ICONS$' FontSize='15'/></Button>
        <Button x:Name='Power' Style='{DynamicResource Icon}' ToolTip='[[Éteindre|Turn off]]'><TextBlock Text='&#xE7E8;' FontFamily='$ICONS$' FontSize='15'/></Button>
      </StackPanel>
    </Grid>
    <StackPanel x:Name='Graph' Margin='0,14,4,0'>
      <Border Height='1' Background='$DIV$' Margin='0,0,0,12'/>
      <Canvas x:Name='Chart' Width='296' Height='40' Background='Transparent' HorizontalAlignment='Left'/>
      <Grid Margin='0,6,30,0'>
        <TextBlock Text='[[il y a 24 h|24 h ago]]' FontSize='11' Foreground='$TT$' HorizontalAlignment='Left'/>
        <TextBlock Text='12 h' FontSize='11' Foreground='$TT$' HorizontalAlignment='Center'/>
        <TextBlock Text='[[maintenant|now]]' FontSize='11' Foreground='$TT$' HorizontalAlignment='Right'/>
      </Grid>
    </StackPanel>
  </StackPanel>
</Border>";

        const double ChartW = 296, PlotW = 266, ChartH = 40;
        public readonly W.FrameworkElement Root;
        public string Key;
        public event Action<string> PowerClicked, IdentifyClicked;
        public event Action<string, string> Renamed;

        readonly Theme _th;
        readonly Ring _ring;
        readonly WC.TextBlock _pct, _sub2, _name, _sub;
        readonly WC.TextBlock _bolt;
        readonly WC.TextBox _nameBox;
        readonly WC.Button _rename, _identify, _power;
        readonly W.FrameworkElement _graph;
        readonly WC.Canvas _chart;
        readonly WM.Brush _accent, _critical, _tp, _ts, _tt, _stroke, _div, _tip;
        List<Sample> _samples = new List<Sample>();
        bool _live, _first = true, _editing;
        WS.Rectangle _cross;
        WC.Border _tipBox;
        WC.TextBlock _tipText;

        public PadCard(Theme th)
        {
            _th = th;
            Root = (W.FrameworkElement)W.Markup.XamlReader.Parse(th.Xaml(Markup));
            _name = (WC.TextBlock)Root.FindName("Name");
            _nameBox = (WC.TextBox)Root.FindName("NameBox");
            _sub = (WC.TextBlock)Root.FindName("Sub");
            _rename = (WC.Button)Root.FindName("Rename");
            _identify = (WC.Button)Root.FindName("Identify");
            _power = (WC.Button)Root.FindName("Power");
            _graph = (W.FrameworkElement)Root.FindName("Graph");
            _chart = (WC.Canvas)Root.FindName("Chart");

            _accent = th.Brush(Theme.Hex(th.Accent));
            _critical = th.Brush(Theme.Hex(th.Critical));
            _tp = th.Brush("$TP$"); _ts = th.Brush("$TS$"); _tt = th.Brush("$TT$");
            _stroke = th.Brush("$STROKE$"); _div = th.Brush("$DIV$"); _tip = th.Brush("$TIP$");

            _ring = new Ring(64, 5, th.Brush("$RING$"));
            _pct = new WC.TextBlock { FontSize = 15, FontWeight = W.FontWeights.SemiBold, HorizontalAlignment = W.HorizontalAlignment.Center };
            _sub2 = new WC.TextBlock { FontSize = 11, Foreground = _ts, HorizontalAlignment = W.HorizontalAlignment.Center, Margin = new W.Thickness(0, -1, 0, 0) };
            _bolt = new WC.TextBlock { Text = "", FontFamily = new WM.FontFamily(Art.IconFont), FontSize = 11, Foreground = _ts, HorizontalAlignment = W.HorizontalAlignment.Center, Margin = new W.Thickness(0, 1, 0, 0) };
            _ring.Center.Children.Add(_pct);
            _ring.Center.Children.Add(_sub2);
            _ring.Center.Children.Add(_bolt);
            ((WC.Border)Root.FindName("RingHost")).Child = _ring;

            _power.Click += delegate { if (PowerClicked != null) PowerClicked(Key); };
            _identify.Click += delegate { if (IdentifyClicked != null) IdentifyClicked(Key); };
            _rename.Click += delegate { BeginRename(); };
            _nameBox.KeyDown += delegate (object s, W.Input.KeyEventArgs e)
            {
                if (e.Key == W.Input.Key.Enter) { EndRename(true); e.Handled = true; }
                else if (e.Key == W.Input.Key.Escape) { EndRename(false); e.Handled = true; }
            };
            _nameBox.LostKeyboardFocus += delegate { if (_editing) EndRename(true); };
            _chart.MouseMove += delegate (object s, W.Input.MouseEventArgs e) { Hover(e.GetPosition(_chart).X); };
            _chart.MouseLeave += delegate { HideHover(); };
        }

        void BeginRename()
        {
            _editing = true;
            _nameBox.Text = _name.Text;
            _name.Visibility = W.Visibility.Hidden;
            _nameBox.Visibility = W.Visibility.Visible;
            _nameBox.Focus();
            _nameBox.SelectAll();
        }

        void EndRename(bool commit)
        {
            if (!_editing) return;
            _editing = false;
            _nameBox.Visibility = W.Visibility.Collapsed;
            _name.Visibility = W.Visibility.Visible;
            string n = _nameBox.Text.Trim();
            if (commit && n != _name.Text && Renamed != null) Renamed(Key, n);
        }

        public void Update(PadView v)
        {
            Key = v.Key;
            bool on = v.Mode != Link.None;
            if (!_editing) _name.Text = v.Name;
            Root.Opacity = on ? 1 : 0.75;
            _bolt.Visibility = W.Visibility.Collapsed;
            _sub2.Visibility = W.Visibility.Collapsed;

            Battery b = on ? v.Battery : null;
            string via = v.Mode == Link.Bluetooth ? "Bluetooth" : "USB";
            if (b != null)
            {
                _pct.Text = b.Percent + Fmt.Pct;
                _pct.Foreground = _tp;
                _ring.Set(Math.Max(0.03, b.Level / 10.0), b.Low ? _critical : _accent, _first);
                if (b.Status == 0)
                {
                    if (v.RemainingMinutes >= 0) { _sub2.Text = Fmt.Short(v.RemainingMinutes); _sub2.Visibility = W.Visibility.Visible; }
                    _sub.Text = via + L.T(" · sur batterie", " · on battery");
                    _ring.ToolTip = v.Remaining != null ? char.ToUpper(v.Remaining[0]) + v.Remaining.Substring(1) : null;
                }
                else if (b.Status == 1)
                {
                    _bolt.Visibility = W.Visibility.Visible;
                    _sub.Text = via + (v.ChargeRemaining != null ? L.T(" · pleine dans ", " · full in ") + v.ChargeRemaining : L.T(" · en charge", " · charging"));
                    _ring.ToolTip = L.T("En charge", "Charging");
                }
                else if (b.Status == 2) { _bolt.Visibility = W.Visibility.Visible; _sub.Text = via + L.T(" · chargée", " · charged"); }
                else _sub.Text = via + L.T(" · erreur de charge", " · charging error");
            }
            else if (on)
            {
                _pct.Text = "…";
                _pct.Foreground = _ts;
                _ring.Set(0, _accent, false);
                _sub.Text = via + L.T(" · lecture de la batterie…", " · reading battery…");
            }
            else
            {
                _pct.Text = v.LastPercent >= 0 ? v.LastPercent + Fmt.Pct : "–";
                _pct.Foreground = _ts;
                _ring.Set(v.LastPercent >= 0 ? v.LastPercent / 100.0 : 0, _tt, _first);
                _sub.Text = L.T("Éteinte", "Off") + (v.LastSeen > DateTime.MinValue ? " · " + Fmt.Ago(v.LastSeen) : "");
            }
            _first = false;

            _identify.Visibility = on ? W.Visibility.Visible : W.Visibility.Collapsed;
            _power.Visibility = v.Mode == Link.Bluetooth ? W.Visibility.Visible : W.Visibility.Collapsed;
            _power.IsEnabled = !v.PoweringOff;
            _power.ToolTip = v.PoweringOff ? L.T("Extinction…", "Turning off…") : L.T("Éteindre", "Turn off");

            _live = on;
            _samples = v.History ?? new List<Sample>();
            bool any = false;
            foreach (Sample s in _samples) if (s.P >= 0) any = true;
            _graph.Visibility = any ? W.Visibility.Visible : W.Visibility.Collapsed;
            if (any) DrawChart();
        }

        double X(DateTime t)
        {
            double span = TimeSpan.FromHours(24).TotalSeconds;
            double s = (t - DateTime.Now.AddHours(-24)).TotalSeconds;
            return Math.Max(0, Math.Min(PlotW, s / span * PlotW));
        }

        static double Y(int p) { return ChartH - 1 - Math.Max(0, Math.Min(100, p)) / 100.0 * (ChartH - 2); }

        // Tracé en escalier (le niveau ne bouge que par paliers de 10 %), aire légère sous la courbe
        void DrawChart()
        {
            _chart.Children.Clear();
            foreach (int p in new[] { 100, 50 })
            {
                WS.Rectangle grid = new WS.Rectangle { Width = PlotW, Height = 1, Fill = _div };
                WC.Canvas.SetTop(grid, Math.Round(Y(p)));
                _chart.Children.Add(grid);
                WC.TextBlock lbl = new WC.TextBlock { Text = p + Fmt.Pct, FontSize = 10, Foreground = _tt };
                lbl.Measure(new W.Size(double.PositiveInfinity, double.PositiveInfinity));
                WC.Canvas.SetLeft(lbl, PlotW + 5);
                WC.Canvas.SetTop(lbl, Math.Round(Y(p)) - lbl.DesiredSize.Height / 2);
                _chart.Children.Add(lbl);
            }
            WS.Rectangle baseLine = new WS.Rectangle { Width = PlotW, Height = 1, Fill = _stroke };
            WC.Canvas.SetTop(baseLine, ChartH - 1);
            _chart.Children.Add(baseLine);

            WM.Brush line = _live ? _accent : _tt;
            WM.Brush area = line.Clone();
            area.Opacity = 0.18;
            List<W.Point> run = new List<W.Point>();
            for (int i = 0; i < _samples.Count; i++)
            {
                Sample s = _samples[i];
                if (s.P < 0) { Flush(run, area, line); continue; }
                DateTime end = i + 1 < _samples.Count ? _samples[i + 1].T : (_live ? DateTime.Now : s.T);
                double x0 = X(s.T), x1 = X(end), y = Y(s.P);
                if (run.Count > 0) run.Add(new W.Point(x0, run[run.Count - 1].Y));
                run.Add(new W.Point(x0, y));
                run.Add(new W.Point(x1, y));
            }
            Flush(run, area, line);

            _cross = new WS.Rectangle { Width = 1, Height = ChartH, Fill = _ts, Visibility = W.Visibility.Collapsed, IsHitTestVisible = false };
            _chart.Children.Add(_cross);
            _tipText = new WC.TextBlock { FontSize = 12, Foreground = _tp };
            _tipBox = new WC.Border
            {
                Background = _tip, BorderBrush = _stroke, BorderThickness = new W.Thickness(1), CornerRadius = new W.CornerRadius(4),
                Padding = new W.Thickness(8, 4, 8, 5), Child = _tipText, Visibility = W.Visibility.Collapsed, IsHitTestVisible = false
            };
            _chart.Children.Add(_tipBox);
        }

        void Flush(List<W.Point> run, WM.Brush area, WM.Brush line)
        {
            if (run.Count < 2) { run.Clear(); return; }
            WM.PathFigure fill = new WM.PathFigure { StartPoint = new W.Point(run[0].X, ChartH - 1), IsClosed = true };
            fill.Segments.Add(new WM.PolyLineSegment(run, true));
            fill.Segments.Add(new WM.LineSegment(new W.Point(run[run.Count - 1].X, ChartH - 1), true));
            WM.PathGeometry fg = new WM.PathGeometry();
            fg.Figures.Add(fill);
            _chart.Children.Add(new WS.Path { Data = fg, Fill = area, IsHitTestVisible = false });

            WM.PathFigure lf = new WM.PathFigure { StartPoint = run[0] };
            lf.Segments.Add(new WM.PolyLineSegment(run.GetRange(1, run.Count - 1), true));
            WM.PathGeometry lg = new WM.PathGeometry();
            lg.Figures.Add(lf);
            _chart.Children.Add(new WS.Path { Data = lg, Stroke = line, StrokeThickness = 2, StrokeLineJoin = WM.PenLineJoin.Round, IsHitTestVisible = false });
            run.Clear();
        }

        void Hover(double x)
        {
            if (_cross == null) return;
            x = Math.Max(0, Math.Min(PlotW, x));
            DateTime t = DateTime.Now.AddHours(-24).AddSeconds(x / PlotW * TimeSpan.FromHours(24).TotalSeconds);
            Sample at = null;
            foreach (Sample s in _samples) if (s.T <= t) at = s;
            bool known = at != null && at.P >= 0 && (_live || _samples.IndexOf(at) < _samples.Count - 1);
            _tipText.Text = t.ToString("HH:mm") + " · " + (known ? at.P + Fmt.Pct + (at.Charging ? L.T(" · en charge", " · charging") : "") : L.T("éteinte", "off"));
            _cross.Visibility = W.Visibility.Visible;
            WC.Canvas.SetLeft(_cross, Math.Round(x));
            _tipBox.Visibility = W.Visibility.Visible;
            _tipBox.Measure(new W.Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = _tipBox.DesiredSize.Width;
            WC.Canvas.SetLeft(_tipBox, Math.Max(0, Math.Min(ChartW - w, x - w / 2)));
            WC.Canvas.SetTop(_tipBox, -_tipBox.DesiredSize.Height - 4);
        }

        void HideHover()
        {
            if (_cross == null) return;
            _cross.Visibility = W.Visibility.Collapsed;
            _tipBox.Visibility = W.Visibility.Collapsed;
        }
    }

    sealed class Flyout : W.Window
    {
        const string Markup = @"
<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Width='360' TextElement.Foreground='$TP$' TextElement.FontFamily='Segoe UI Variable Text, Segoe UI' TextElement.FontSize='14'
        UseLayoutRounding='True' TextOptions.TextFormattingMode='Display'>
  <Border.Resources>
    <Style x:Key='Btn' TargetType='Button'>
      <Setter Property='Foreground' Value='$TP$'/>
      <Setter Property='Background' Value='$FILL$'/>
      <Setter Property='BorderBrush' Value='$STROKE$'/>
      <Setter Property='Padding' Value='12,5,12,6'/>
      <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='B' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='4' Padding='{TemplateBinding Padding}'>
              <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='B' Property='Background' Value='$FILLH$'/></Trigger>
              <Trigger Property='IsPressed' Value='True'><Setter TargetName='B' Property='Background' Value='$FILLP$'/><Setter Property='Foreground' Value='$TS$'/></Trigger>
              <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='Subtle' TargetType='Button' BasedOn='{StaticResource Btn}'>
      <Setter Property='Padding' Value='8,5,10,6'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='B' Background='Transparent' CornerRadius='4' Padding='{TemplateBinding Padding}'>
              <ContentPresenter VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='B' Property='Background' Value='$SUBH$'/></Trigger>
              <Trigger Property='IsPressed' Value='True'><Setter TargetName='B' Property='Background' Value='$SUBP$'/><Setter Property='Foreground' Value='$TS$'/></Trigger>
              <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='Icon' TargetType='Button' BasedOn='{StaticResource Subtle}'>
      <Setter Property='Width' Value='32'/>
      <Setter Property='Height' Value='32'/>
      <Setter Property='Padding' Value='0'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='B' Background='Transparent' CornerRadius='4'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='B' Property='Background' Value='$SUBH$'/></Trigger>
              <Trigger Property='IsPressed' Value='True'><Setter TargetName='B' Property='Background' Value='$SUBP$'/><Setter Property='Foreground' Value='$TS$'/></Trigger>
              <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='Choice' TargetType='Button' BasedOn='{StaticResource Btn}'>
      <Setter Property='HorizontalAlignment' Value='Right'/>
      <Setter Property='VerticalAlignment' Value='Center'/>
      <Setter Property='MinWidth' Value='160'/>
      <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
    </Style>
    <Style x:Key='Switch' TargetType='ToggleButton'>
      <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
      <Setter Property='HorizontalAlignment' Value='Right'/>
      <Setter Property='VerticalAlignment' Value='Center'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='ToggleButton'>
            <Grid Width='40' Height='20' Background='Transparent'>
              <Border x:Name='Track' CornerRadius='10' BorderThickness='1' BorderBrush='$TS$' Background='Transparent'/>
              <Ellipse x:Name='Knob' Width='12' Height='12' Fill='$TS$' HorizontalAlignment='Left' Margin='4,0,0,0'/>
            </Grid>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Knob' Property='Width' Value='14'/><Setter TargetName='Knob' Property='Height' Value='14'/><Setter TargetName='Knob' Property='Margin' Value='3,0,0,0'/></Trigger>
              <Trigger Property='IsChecked' Value='True'>
                <Setter TargetName='Track' Property='Background' Value='$ACC$'/>
                <Setter TargetName='Track' Property='BorderBrush' Value='$ACC$'/>
                <Setter TargetName='Knob' Property='Fill' Value='$ONACC$'/>
                <Setter TargetName='Knob' Property='HorizontalAlignment' Value='Right'/>
                <Setter TargetName='Knob' Property='Margin' Value='0,0,4,0'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style TargetType='ToolTip'>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='ToolTip'>
            <Border Background='$TIP$' BorderBrush='$STROKE$' BorderThickness='1' CornerRadius='4' Padding='8,4,8,5'>
              <ContentPresenter TextElement.Foreground='$TP$' TextElement.FontSize='12'/>
            </Border>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
  </Border.Resources>
  <Grid>
    <StackPanel x:Name='MainPage'>
      <Grid Margin='20,12,10,10'>
        <StackPanel Orientation='Horizontal' VerticalAlignment='Center'>
          <TextBlock Text='[[Manettes|Controllers]]' FontWeight='SemiBold'/>
          <TextBlock x:Name='Count' Foreground='$TS$' FontSize='12' Margin='8,1,0,0' VerticalAlignment='Center'/>
        </StackPanel>
        <Button x:Name='Gear' Style='{StaticResource Icon}' HorizontalAlignment='Right' ToolTip='[[Réglages|Settings]]'>
          <TextBlock Text='&#xE713;' FontFamily='$ICONS$' FontSize='16'/>
        </Button>
      </Grid>
      <StackPanel x:Name='Cards' Margin='16,0,16,16'/>
    </StackPanel>
    <StackPanel x:Name='SettingsPage' Visibility='Collapsed'>
      <Grid Margin='10,12,20,6'>
        <Button x:Name='Back' Style='{StaticResource Icon}' HorizontalAlignment='Left' ToolTip='[[Retour|Back]]'>
          <TextBlock Text='&#xE72B;' FontFamily='$ICONS$' FontSize='14'/>
        </Button>
        <TextBlock Text='[[Réglages|Settings]]' FontWeight='SemiBold' Margin='42,0,0,0' VerticalAlignment='Center'/>
      </Grid>
      <Grid Margin='20,10,20,10'>
        <StackPanel VerticalAlignment='Center' Margin='0,0,172,0'>
          <TextBlock Text='[[Arrêt automatique|Auto power-off]]'/>
          <TextBlock Text='[[Si la manette est inactive (Bluetooth)|When the controller is idle (Bluetooth)]]' FontSize='12' Foreground='$TS$' TextWrapping='Wrap'/>
        </StackPanel>
        <Button x:Name='AutoOff' Style='{StaticResource Choice}'>
          <Grid><TextBlock x:Name='AutoOffText' HorizontalAlignment='Left' Margin='0,0,22,0'/><TextBlock Text='&#xE70D;' FontFamily='$ICONS$' FontSize='12' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,1,0,0'/></Grid>
        </Button>
      </Grid>
      <Grid Margin='20,10,20,10'>
        <StackPanel VerticalAlignment='Center' Margin='0,0,172,0'>
          <TextBlock Text='[[Barre lumineuse|Lightbar]]'/>
          <TextBlock Text='[[Couleur ou intensité de la lumière|Color or brightness]]' FontSize='12' Foreground='$TS$' TextWrapping='Wrap'/>
        </StackPanel>
        <Button x:Name='Lightbar' Style='{StaticResource Choice}'>
          <Grid><TextBlock x:Name='LightbarText' HorizontalAlignment='Left' Margin='0,0,22,0'/><TextBlock Text='&#xE70D;' FontFamily='$ICONS$' FontSize='12' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,1,0,0'/></Grid>
        </Button>
      </Grid>
      <Grid Margin='20,10,20,10'>
        <StackPanel VerticalAlignment='Center' Margin='0,0,172,0'>
          <TextBlock Text='[[Icône|Tray icon]]'/>
          <TextBlock Text='[[Dans la barre des tâches|Shown in the taskbar]]' FontSize='12' Foreground='$TS$' TextWrapping='Wrap'/>
        </StackPanel>
        <Button x:Name='TrayStyle' Style='{StaticResource Choice}'>
          <Grid><TextBlock x:Name='TrayStyleText' HorizontalAlignment='Left' Margin='0,0,22,0'/><TextBlock Text='&#xE70D;' FontFamily='$ICONS$' FontSize='12' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,1,0,0'/></Grid>
        </Button>
      </Grid>
      <Grid Margin='20,10,20,10'>
        <StackPanel VerticalAlignment='Center' Margin='0,0,60,0'>
          <TextBlock Text='[[Affichage en jeu|In-game overlay]]'/>
          <TextBlock Text='[[Maintiens le bouton PS 1 seconde pour voir la batterie par-dessus le jeu|Hold the PS button for 1 second to see the battery over your game]]' FontSize='12' Foreground='$TS$' TextWrapping='Wrap'/>
        </StackPanel>
        <ToggleButton x:Name='Overlay' Style='{StaticResource Switch}'/>
      </Grid>
      <Grid Margin='20,10,20,10'>
        <StackPanel VerticalAlignment='Center' Margin='0,0,60,0'>
          <TextBlock Text='[[Lancer au démarrage|Start with Windows]]'/>
          <TextBlock Text='[[À l&apos;ouverture de ta session Windows|When you sign in to Windows]]' FontSize='12' Foreground='$TS$' TextWrapping='Wrap'/>
        </StackPanel>
        <ToggleButton x:Name='Startup' Style='{StaticResource Switch}'/>
      </Grid>
      <Grid Margin='20,10,20,14'>
        <StackPanel VerticalAlignment='Center' Margin='0,0,60,0'>
          <TextBlock Text='[[Masquer l&apos;icône|Hide the icon]]'/>
          <TextBlock Text='[[Quand aucune manette n&apos;est allumée (le raccourci « Manette PS5 » du menu Démarrer rouvre ce panneau)|When no controller is on (the “DualSense Battery” Start menu shortcut reopens this panel)]]' FontSize='12' Foreground='$TS$' TextWrapping='Wrap'/>
        </StackPanel>
        <ToggleButton x:Name='Hide' Style='{StaticResource Switch}'/>
      </Grid>
      <Border Background='$FOOT$' BorderBrush='$DIV$' BorderThickness='0,1,0,0' Padding='12,10,20,10'>
        <Button x:Name='BtSettings' Style='{StaticResource Subtle}' HorizontalAlignment='Left'>
          <StackPanel Orientation='Horizontal'>
            <TextBlock Text='&#xE702;' FontFamily='$ICONS$' FontSize='16' VerticalAlignment='Center'/>
            <TextBlock Text='[[Paramètres Bluetooth|Bluetooth settings]]' Margin='10,0,0,0' VerticalAlignment='Center'/>
          </StackPanel>
        </Button>
      </Border>
    </StackPanel>
  </Grid>
</Border>";

        public event Action<string> PowerOffRequested, IdentifyRequested;
        public event Action<string, string> RenameRequested;
        public event Action BluetoothSettingsRequested;
        public event Action<int> AutoOffChosen, LightbarChosen, TrayStyleChosen;
        public event Action<bool> OverlayToggled, HideToggled, StartupToggled;

        readonly Theme _th;
        readonly bool _demo;
        readonly W.FrameworkElement _root, _mainPage, _settingsPage;
        readonly WC.StackPanel _cards;
        readonly WC.TextBlock _count, _autoOffText, _lightbarText, _trayStyleText;
        readonly WC.Button _autoOff, _lightbar, _trayStyle, _btSettings, _gear, _back;
        readonly WCP.ToggleButton _overlay, _hide, _startup;
        readonly List<PadCard> _cardList = new List<PadCard>();
        IntPtr _hwnd;
        bool _menuOpen, _closing, _fromBelow = true, _activated, _updating;
        double _anchorBottom = double.NaN;
        View _view;
        WT.DispatcherTimer _outsideWatch;
        WC.Button _dismissedBy;
        DateTime _dismissedAt;

        public IntPtr Hwnd { get { return _hwnd; } }

        public Flyout(Theme th, bool demo)
        {
            _th = th; _demo = demo;
            Title = L.AppName;
            WindowStyle = W.WindowStyle.None;
            ResizeMode = W.ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            SizeToContent = W.SizeToContent.WidthAndHeight;
            Background = WM.Brushes.Transparent;

            _root = (W.FrameworkElement)W.Markup.XamlReader.Parse(th.Xaml(Markup));
            Content = _root;
            _mainPage = (W.FrameworkElement)_root.FindName("MainPage");
            _settingsPage = (W.FrameworkElement)_root.FindName("SettingsPage");
            _cards = (WC.StackPanel)_root.FindName("Cards");
            _count = (WC.TextBlock)_root.FindName("Count");
            _gear = (WC.Button)_root.FindName("Gear");
            _back = (WC.Button)_root.FindName("Back");
            _autoOff = (WC.Button)_root.FindName("AutoOff");
            _autoOffText = (WC.TextBlock)_root.FindName("AutoOffText");
            _lightbar = (WC.Button)_root.FindName("Lightbar");
            _lightbarText = (WC.TextBlock)_root.FindName("LightbarText");
            _trayStyle = (WC.Button)_root.FindName("TrayStyle");
            _trayStyleText = (WC.TextBlock)_root.FindName("TrayStyleText");
            _overlay = (WCP.ToggleButton)_root.FindName("Overlay");
            _hide = (WCP.ToggleButton)_root.FindName("Hide");
            _startup = (WCP.ToggleButton)_root.FindName("Startup");
            _btSettings = (WC.Button)_root.FindName("BtSettings");

            _gear.Click += delegate { ShowPage(true); };
            _back.Click += delegate { ShowPage(false); };
            _btSettings.Click += delegate { CloseAnimated(); if (BluetoothSettingsRequested != null) BluetoothSettingsRequested(); };
            _autoOff.Click += delegate { OpenAutoOffMenu(); };
            _lightbar.Click += delegate { OpenLightbarMenu(); };
            _trayStyle.Click += delegate
            {
                int idx = Choose(_trayStyle, Settings.TrayStyleChoices, _view != null ? _view.TrayStyle : 0);
                if (idx >= 0 && TrayStyleChosen != null) TrayStyleChosen(idx);
            };
            _overlay.Click += delegate { if (!_updating && OverlayToggled != null) OverlayToggled(_overlay.IsChecked == true); };
            _hide.Click += delegate { if (!_updating && HideToggled != null) HideToggled(_hide.IsChecked == true); };
            _startup.Click += delegate { if (!_updating && StartupToggled != null) StartupToggled(_startup.IsChecked == true); };

            SourceInitialized += OnSourceInitialized;
            Activated += delegate { _activated = true; };
            Deactivated += delegate { if (!_menuOpen && !_demo) CloseAnimated(); };
            KeyDown += delegate (object s, W.Input.KeyEventArgs e) { if (e.Key == W.Input.Key.Escape) CloseAnimated(); };
            SizeChanged += delegate { KeepAnchored(); };
            Closing += delegate { _closing = true; if (_outsideWatch != null) _outsideWatch.Stop(); };
        }

        // Passage entre la page des manettes et la page des réglages (glissement latéral)
        public void ShowPage(bool settings)
        {
            W.FrameworkElement show = settings ? _settingsPage : _mainPage, hide = settings ? _mainPage : _settingsPage;
            hide.Visibility = W.Visibility.Collapsed;
            show.Visibility = W.Visibility.Visible;
            WM.TranslateTransform tt = new WM.TranslateTransform(settings ? 28 : -28, 0);
            show.RenderTransform = tt;
            WMA.DoubleAnimation slide = new WMA.DoubleAnimation(0, TimeSpan.FromMilliseconds(260));
            slide.EasingFunction = new WMA.ExponentialEase { EasingMode = WMA.EasingMode.EaseOut, Exponent = 5 };
            tt.BeginAnimation(WM.TranslateTransform.XProperty, slide);
            show.BeginAnimation(W.UIElement.OpacityProperty, new WMA.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }

        // Fermeture façon Windows 11 : le contenu glisse vers la barre des tâches en s'effaçant
        public void CloseAnimated()
        {
            if (_closing) return;
            _closing = true;
            WM.TranslateTransform tt = _root.RenderTransform as WM.TranslateTransform;
            if (tt == null || tt.IsFrozen) { tt = new WM.TranslateTransform(); _root.RenderTransform = tt; }
            WMA.DoubleAnimation slide = new WMA.DoubleAnimation(_fromBelow ? 20 : -20, TimeSpan.FromMilliseconds(140));
            slide.EasingFunction = new WMA.CubicEase { EasingMode = WMA.EasingMode.EaseIn };
            WMA.DoubleAnimation fade = new WMA.DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
            fade.Completed += delegate { Close(); };
            tt.BeginAnimation(WM.TranslateTransform.YProperty, slide);
            _root.BeginAnimation(W.UIElement.OpacityProperty, fade);
        }

        void OnSourceInitialized(object sender, EventArgs e)
        {
            _hwnd = new WI.WindowInteropHelper(this).Handle;
            WI.HwndSource.FromHwnd(_hwnd).CompositionTarget.BackgroundColor = WM.Colors.Transparent;
            long ex = Native.GetWindowLongPtr(_hwnd, -20).ToInt64();
            Native.SetWindowLongPtr(_hwnd, -20, new IntPtr((ex | 0x80) & ~0x40000L));   // fenêtre outil (absente d'Alt+Tab)
            if (!Native.ApplyBackdrop(_hwnd, _th.Light))
                _root.SetValue(WC.Border.BackgroundProperty, new WM.SolidColorBrush(_th.Light ? WM.Color.FromRgb(249, 249, 249) : WM.Color.FromRgb(44, 44, 44)));
        }

        public void SetView(View v)
        {
            _view = v;
            _count.Text = v.Connected == 0 ? L.T("aucune connectée", "none connected") : v.Connected + (v.Connected > 1 ? L.T(" connectées", " connected") : L.T(" connectée", " connected"));

            List<PadView> pads = v.Pads;
            if (pads.Count == 0) pads = new List<PadView> { new PadView { Key = "", Name = L.PadName, Mode = Link.None } };
            if (_cardList.Count != pads.Count)
            {
                _cards.Children.Clear();
                _cardList.Clear();
                for (int i = 0; i < pads.Count; i++)
                {
                    PadCard c = new PadCard(_th);
                    if (i > 0) c.Root.Margin = new W.Thickness(0, 8, 0, 0);
                    c.PowerClicked += delegate (string k) { if (PowerOffRequested != null) PowerOffRequested(k); };
                    c.IdentifyClicked += delegate (string k) { if (IdentifyRequested != null) IdentifyRequested(k); };
                    c.Renamed += delegate (string k, string n) { if (RenameRequested != null && k != "") RenameRequested(k, n); };
                    _cards.Children.Add(c.Root);
                    _cardList.Add(c);
                }
            }
            for (int i = 0; i < pads.Count; i++) _cardList[i].Update(pads[i]);
            if (v.Pads.Count == 0 && _cardList.Count == 1) _cardList[0].Root.ToolTip = L.T("Appuie sur le bouton PS de la manette pour la connecter", "Press the PS button on the controller to connect it");

            _updating = true;
            _autoOffText.Text = Fmt.AutoOff(v.AutoOff);
            _lightbarText.Text = Settings.LightbarChoices[v.Lightbar];
            _trayStyleText.Text = Settings.TrayStyleChoices[v.TrayStyle];
            _overlay.IsChecked = v.GameOverlay;
            _hide.IsChecked = v.HideWhenOff;
            _startup.IsChecked = v.StartWithWindows;
            _updating = false;
        }

        double Scale
        {
            get
            {
                WI.HwndSource src = WI.HwndSource.FromHwnd(_hwnd);
                return src != null && src.CompositionTarget != null ? src.CompositionTarget.TransformToDevice.M11 : 1;
            }
        }

        // Affiche le panneau au-dessus de l'icône (coordonnées en pixels), comme les panneaux de Windows 11.
        // Sans icône (raccourci du menu Démarrer), en bas à droite de l'écran.
        public void ShowAt(SD.Rectangle anchor)
        {
            new WI.WindowInteropHelper(this).EnsureHandle();
            double scale = Scale;
            _root.Measure(new W.Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = _root.DesiredSize.Width * scale, h = _root.DesiredSize.Height * scale, m = 12 * scale;

            SD.Point c = anchor.IsEmpty ? WF.Cursor.Position : new SD.Point(anchor.Left + anchor.Width / 2, anchor.Top + anchor.Height / 2);
            WF.Screen scr = anchor.IsEmpty ? WF.Screen.PrimaryScreen : WF.Screen.FromPoint(c);
            SD.Rectangle wa = scr.WorkingArea, sb = scr.Bounds;
            double x, y;
            if (anchor.IsEmpty) { x = wa.Right - m - w; y = wa.Bottom - m - h; }
            else if (wa.Top > sb.Top) { x = c.X - w / 2; y = wa.Top + m; _fromBelow = false; }
            else if (wa.Left > sb.Left) { x = wa.Left + m; y = c.Y - h / 2; _fromBelow = false; }
            else if (wa.Right < sb.Right) { x = wa.Right - m - w; y = c.Y - h / 2; _fromBelow = false; }
            else { x = c.X - w / 2; y = wa.Bottom - m - h; }
            x = Math.Max(wa.Left + m, Math.Min(x, wa.Right - m - w));
            y = Math.Max(wa.Top + m, Math.Min(y, wa.Bottom - m - h));
            Left = x / scale;
            Top = y / scale;
            if (_fromBelow) _anchorBottom = (y + h) / scale;

            Show();
            Activate();
            Native.SetForegroundWindow(_hwnd);
            Animate();
            if (!_demo) WatchOutsideClicks();
        }

        void KeepAnchored()
        {
            if (double.IsNaN(_anchorBottom)) return;
            double top = _anchorBottom - ActualHeight;
            if (Math.Abs(Top - top) > 0.5) Top = top;
        }

        void Animate()
        {
            WM.TranslateTransform tt = new WM.TranslateTransform(0, _fromBelow ? 24 : -24);
            _root.RenderTransform = tt;
            WMA.DoubleAnimation slide = new WMA.DoubleAnimation(0, TimeSpan.FromMilliseconds(330));
            slide.EasingFunction = new WMA.ExponentialEase { EasingMode = WMA.EasingMode.EaseOut, Exponent = 6 };
            tt.BeginAnimation(WM.TranslateTransform.YProperty, slide);
            _root.BeginAnimation(W.UIElement.OpacityProperty, new WMA.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        }

        // Filet de sécurité : si Windows n'a pas donné le premier plan au panneau, on le ferme quand même
        // dès qu'un autre élément passe au premier plan.
        void WatchOutsideClicks()
        {
            _outsideWatch = new WT.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _outsideWatch.Tick += delegate
            {
                if (_menuOpen || _closing) return;
                IntPtr fg = Native.GetForegroundWindow();
                if (_activated && fg != _hwnd) CloseAnimated();
                else if (!_activated && fg != _hwnd && (WF.Control.MouseButtons != WF.MouseButtons.None) && !IsCursorInside()) CloseAnimated();
            };
            _outsideWatch.Start();
        }

        bool IsCursorInside()
        {
            Native.RECT r;
            Native.GetWindowRect(_hwnd, out r);
            SD.Point p = WF.Cursor.Position;
            return p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;
        }

        static bool CursorOver(W.FrameworkElement e)
        {
            W.Point a = e.PointToScreen(new W.Point(0, 0)), b = e.PointToScreen(new W.Point(e.ActualWidth, e.ActualHeight));
            SD.Point c = WF.Cursor.Position;
            return c.X >= a.X && c.X <= b.X && c.Y >= a.Y && c.Y <= b.Y;
        }

        // Menu natif ouvert au-dessus du bouton, aligné à droite. Un clic sur le bouton pendant que son menu est
        // ouvert le ferme (Windows referme le menu au clic, puis le même clic arrive au bouton : on l'ignore).
        int Choose(WC.Button button, string[] labels, int current)
        {
            if (button == _dismissedBy && (DateTime.UtcNow - _dismissedAt).TotalMilliseconds < 400) { _dismissedBy = null; return -1; }
            W.Point p = button.PointToScreen(new W.Point(button.ActualWidth, -4));
            _menuOpen = true;
            int idx = -1;
            try { idx = NativeMenu.Show(_hwnd, (int)p.X, (int)p.Y, labels, current, null, _th.Light, true, true); return idx; }
            finally
            {
                _menuOpen = false;
                _dismissedBy = idx < 0 && CursorOver(button) ? button : null;
                _dismissedAt = DateTime.UtcNow;
            }
        }

        public void OpenAutoOffMenu()
        {
            int[] choices = Settings.AutoOffChoices;
            string[] labels = new string[choices.Length];
            int current = -1;
            for (int i = 0; i < choices.Length; i++)
            {
                labels[i] = choices[i] == 0 ? Fmt.AutoOff(0) : L.T("Après ", "After ") + Fmt.AutoOff(choices[i]);
                if (_view != null && choices[i] == _view.AutoOff) current = i;
            }
            int idx = Choose(_autoOff, labels, current);
            if (idx >= 0 && AutoOffChosen != null) AutoOffChosen(choices[idx]);
        }

        public void OpenLightbarMenu()
        {
            int idx = Choose(_lightbar, Settings.LightbarChoices, _view != null ? _view.Lightbar : 0);
            if (idx >= 0 && LightbarChosen != null) LightbarChosen(idx);
        }
    }

    // Encart « en jeu » (bouton PS maintenu) : pastille sombre translucide en haut à droite, un anneau par manette
    // (façon sdsc-utils). Fenêtre transparente qui ne prend jamais le focus et laisse passer les clics.
    sealed class Overlay : W.Window
    {
        static readonly WM.Brush White = Frozen(WM.Color.FromRgb(255, 255, 255));
        static readonly WM.Brush Dim = Frozen(WM.Color.FromArgb(200, 255, 255, 255));
        static readonly WM.Brush Track = Frozen(WM.Color.FromArgb(64, 255, 255, 255));
        static readonly WM.Brush Glass = Frozen(WM.Color.FromArgb(232, 38, 38, 44));
        static readonly WM.Brush Edge = Frozen(WM.Color.FromArgb(72, 255, 255, 255));
        static readonly WM.Color LowColor = WM.Color.FromRgb(255, 107, 107);
        const double Shadow = 20, Gap = 16;   // marge laissée pour l'ombre ; distance au bord de l'écran

        readonly WC.StackPanel _rings;
        readonly WC.Border _alert;
        readonly WC.TextBlock _alertText;
        readonly W.FrameworkElement _root;
        readonly WT.DispatcherTimer _hide = new WT.DispatcherTimer();
        bool _closing;

        static WM.Brush Frozen(WM.Color c) { WM.SolidColorBrush b = new WM.SolidColorBrush(c); b.Freeze(); return b; }

        static WM.Effects.DropShadowEffect MakeShadow()
        {
            return new WM.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Direction = 270, Opacity = 0.45, Color = WM.Colors.Black };
        }

        public Overlay(Theme th)
        {
            WindowStyle = W.WindowStyle.None;
            AllowsTransparency = true;
            Background = WM.Brushes.Transparent;
            ResizeMode = W.ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            SizeToContent = W.SizeToContent.WidthAndHeight;

            _rings = new WC.StackPanel();
            WC.Border pill = new WC.Border
            {
                Background = Glass, BorderBrush = Edge, BorderThickness = new W.Thickness(1), CornerRadius = new W.CornerRadius(34),
                Padding = new W.Thickness(6), Child = _rings, Effect = MakeShadow(), VerticalAlignment = W.VerticalAlignment.Top
            };

            WM.Brush low = Frozen(LowColor);
            _alertText = new WC.TextBlock { Foreground = low, FontWeight = W.FontWeights.SemiBold, FontSize = 13, VerticalAlignment = W.VerticalAlignment.Center };
            WC.StackPanel alertRow = new WC.StackPanel { Orientation = WC.Orientation.Horizontal };
            alertRow.Children.Add(new WC.TextBlock { Text = "", FontFamily = new WM.FontFamily(Art.IconFont), FontSize = 14, Foreground = low, VerticalAlignment = W.VerticalAlignment.Center, Margin = new W.Thickness(0, 1, 8, 0) });
            alertRow.Children.Add(_alertText);
            _alert = new WC.Border
            {
                Background = Glass, BorderBrush = Edge, BorderThickness = new W.Thickness(1), CornerRadius = new W.CornerRadius(18),
                Padding = new W.Thickness(14, 8, 16, 9), Child = alertRow, Effect = MakeShadow(),
                VerticalAlignment = W.VerticalAlignment.Top, Margin = new W.Thickness(0, 15, 10, 0), Visibility = W.Visibility.Collapsed
            };

            WC.StackPanel row = new WC.StackPanel { Orientation = WC.Orientation.Horizontal, Margin = new W.Thickness(Shadow) };
            row.Children.Add(_alert);
            row.Children.Add(pill);
            row.SetValue(WC.TextBlock.FontFamilyProperty, new WM.FontFamily("Segoe UI Variable Display, Segoe UI"));
            row.SetValue(WC.TextBlock.ForegroundProperty, White);
            WM.TextOptions.SetTextRenderingMode(row, WM.TextRenderingMode.Grayscale);
            _root = row;
            Content = row;

            _hide.Tick += delegate { _hide.Stop(); FadeOut(); };
            SourceInitialized += delegate
            {
                IntPtr hwnd = new WI.WindowInteropHelper(this).Handle;
                long ex = Native.GetWindowLongPtr(hwnd, -20).ToInt64();
                // fenêtre outil, ne prend jamais le focus, laisse passer les clics
                Native.SetWindowLongPtr(hwnd, -20, new IntPtr((ex | 0x80 | 0x08000000 | 0x20) & ~0x40000L));
            };
        }

        public bool Closing_ { get { return _closing; } }

        // Même dégradé que la barre lumineuse « Selon la batterie », éclairci pour ressortir sur fond sombre
        static WM.Color RingColor(Battery b)
        {
            if (b.Low) return LowColor;
            byte[] c = Output.ColorFor(3, b);
            return WM.Color.FromRgb((byte)(c[0] + (255 - c[0]) * 0.3), (byte)(c[1] + (255 - c[1]) * 0.3), (byte)(c[2] + (255 - c[2]) * 0.3));
        }

        public void ShowPads(List<PadView> pads, string headline)
        {
            _rings.Children.Clear();
            foreach (PadView p in pads)
            {
                if (p.Mode == Link.None) continue;
                Battery b = p.Battery;
                Ring ring = new Ring(56, 4.5, Track);
                if (_rings.Children.Count > 0) ring.Margin = new W.Thickness(0, 6, 0, 0);
                ring.Center.Children.Add(new WC.TextBlock
                {
                    Text = b == null ? "…" : b.Percent + "%", FontSize = 14, FontWeight = W.FontWeights.SemiBold,
                    HorizontalAlignment = W.HorizontalAlignment.Center, Margin = new W.Thickness(0, -1, 0, 0)
                });
                WC.TextBlock sub = null;
                if (b != null && b.Status == 1)
                    sub = new WC.TextBlock { Text = "", FontFamily = new WM.FontFamily(Art.IconFont), FontSize = 10 };
                else if (b != null && b.Status == 2) sub = new WC.TextBlock { Text = L.T("pleine", "full") };
                else if (b != null && p.RemainingMinutes >= 0) sub = new WC.TextBlock { Text = Fmt.Short(p.RemainingMinutes) };
                if (sub != null)
                {
                    if (sub.FontFamily.Source != Art.IconFont) sub.FontSize = 10.5;
                    sub.Foreground = Dim;
                    sub.HorizontalAlignment = W.HorizontalAlignment.Center;
                    sub.Margin = new W.Thickness(0, -1, 0, 0);
                    ring.Center.Children.Add(sub);
                }
                if (b != null) ring.Set(Math.Max(0.03, b.Level / 10.0), Frozen(RingColor(b)), true);
                _rings.Children.Add(ring);
            }
            _alertText.Text = headline ?? "";
            _alert.Visibility = headline != null ? W.Visibility.Visible : W.Visibility.Collapsed;

            // coin haut droit de l'écran du jeu (fenêtre au premier plan)
            new WI.WindowInteropHelper(this).EnsureHandle();
            IntPtr fgw = Native.GetForegroundWindow();
            WF.Screen scr = fgw != IntPtr.Zero ? WF.Screen.FromHandle(fgw) : WF.Screen.PrimaryScreen;
            WI.HwndSource src = WI.HwndSource.FromHwnd(new WI.WindowInteropHelper(this).Handle);
            double scale = src != null && src.CompositionTarget != null ? src.CompositionTarget.TransformToDevice.M11 : 1;
            _root.Measure(new W.Size(double.PositiveInfinity, double.PositiveInfinity));
            Left = scr.Bounds.Right / scale - _root.DesiredSize.Width - Shadow - Gap + 2 * Shadow;
            Top = scr.Bounds.Top / scale + Gap - Shadow;

            _closing = false;
            _root.BeginAnimation(W.UIElement.OpacityProperty, null);
            if (!IsVisible) Show();
            WM.TranslateTransform tt = new WM.TranslateTransform(14, 0);
            _root.RenderTransform = tt;
            WMA.DoubleAnimation slide = new WMA.DoubleAnimation(0, TimeSpan.FromMilliseconds(320));
            slide.EasingFunction = new WMA.ExponentialEase { EasingMode = WMA.EasingMode.EaseOut, Exponent = 6 };
            tt.BeginAnimation(WM.TranslateTransform.XProperty, slide);
            _root.BeginAnimation(W.UIElement.OpacityProperty, new WMA.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            _hide.Stop();
            _hide.Interval = TimeSpan.FromSeconds(headline != null ? 7 : 4);
            _hide.Start();
        }

        void FadeOut()
        {
            if (_closing) return;
            _closing = true;
            WMA.DoubleAnimation fade = new WMA.DoubleAnimation(0, TimeSpan.FromMilliseconds(260));
            fade.Completed += delegate { if (_closing) Close(); };
            _root.BeginAnimation(W.UIElement.OpacityProperty, fade);
        }
    }

    // ------------------------------------------------------------------ application

    // État d'une manette connectée
    sealed class Ctl
    {
        public string Key;
        public int Number;
        public Pad Pad;
        public PadReader Reader;
        public Battery Battery;
        public bool Greet = true, Fired50, Fired10, Fired5, PoweringOff, LightSet, Busy;
        public byte[] LastColor;
        public DateTime? LastStep;
        public Link Mode { get { return Pad.Link; } }
    }

    sealed class TrayApp
    {
        readonly WT.Dispatcher _ui = WT.Dispatcher.CurrentDispatcher;
        readonly WF.NotifyIcon _tray = new WF.NotifyIcon();
        readonly DeviceWatcher _watcher;
        readonly WT.DispatcherTimer _poll, _debounce, _idle, _trim;
        readonly List<Ctl> _ctls = new List<Ctl>();

        bool _busy, _pending;
        IntPtr _hIcon = IntPtr.Zero;
        Flyout _flyout;
        Overlay _overlay;
        DateTime _flyoutClosedAt = DateTime.MinValue;

        public TrayApp()
        {
            Toasts.Init();
            History.Load();
            StartMenu.Ensure();
            Settings.InitStartup();
            _tray.MouseUp += delegate (object s, WF.MouseEventArgs e)
            {
                if (e.Button == WF.MouseButtons.Left) ToggleFlyout(TrayIconRect());
                else if (e.Button == WF.MouseButtons.Right) ShowTrayMenu();
            };
            UpdateTray();

            _debounce = new WT.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _debounce.Tick += delegate { _debounce.Stop(); Refresh(); };
            _watcher = new DeviceWatcher();
            _watcher.Changed += delegate { Kick(); };

            _poll = new WT.DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };   // filet de sécurité
            _poll.Tick += delegate { Refresh(); };
            _poll.Start();

            _idle = new WT.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _idle.Tick += delegate { CheckIdle(); };
            _idle.Start();

            _trim = new WT.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _trim.Tick += delegate { _trim.Stop(); TrimMemory(); };

            SystemEvents.UserPreferenceChanged += OnPrefChanged;
            Log.Write("Démarrage");
            Refresh();
            PromoteTrayIconOnce();
            ScheduleTrim(15);
        }

        void Kick() { _debounce.Stop(); _debounce.Start(); }

        string NameOf(Ctl c)
        {
            string n = Known.Nickname(c.Key);
            if (n != null) return n;
            return _ctls.Count > 1 || c.Number > 1 ? L.Numbered(c.Number) : L.PadName;
        }

        static string Via(Ctl c) { return c.Mode == Link.Bluetooth ? "Bluetooth" : L.T("câble USB", "USB cable"); }
        Ctl Find(string key) { return _ctls.Find(delegate (Ctl x) { return x.Key == key; }); }

        // ---- détection des manettes ----
        void Refresh()
        {
            if (_busy) { _pending = true; return; }
            _busy = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<Pad> pads = null;
                try { pads = DualSense.FindAll(); }
                catch (Exception e) { Log.Write("Recherche des manettes : " + e.Message); }
                _ui.BeginInvoke(new Action(delegate
                {
                    _busy = false;
                    try { if (pads != null) OnPads(pads); }
                    catch (Exception e) { Log.Write("Erreur : " + e); }
                    if (_pending) { _pending = false; Refresh(); }
                }));
            });
        }

        void OnPads(List<Pad> pads)
        {
            bool changed = false;
            for (int i = _ctls.Count - 1; i >= 0; i--)
            {
                Ctl c = _ctls[i];
                bool still = false;
                foreach (Pad p in pads) if (p.Key == c.Key) still = true;
                if (still) continue;
                c.Reader.Stop();
                Output.SetPulse(c.Pad, false);
                History.Gap(c.Key);
                Log.Write(NameOf(c) + " déconnectée");
                _ctls.RemoveAt(i);
                changed = true;
            }

            foreach (Pad p in pads)
            {
                Ctl c = Find(p.Key);
                if (c == null)
                {
                    c = new Ctl { Key = p.Key, Pad = p, Number = FreeNumber() };
                    _ctls.Add(c);
                    _ctls.Sort(delegate (Ctl a, Ctl b) { return a.Number.CompareTo(b.Number); });
                    Log.Write(NameOf(c) + " connectée (" + Via(c) + ")");
                    StartReader(c);
                    changed = true;
                }
                else if (c.Pad.Path != p.Path || (c.Reader.Dead && (DateTime.UtcNow - c.Reader.StartedUtc).TotalSeconds > 20))
                {
                    if (c.Pad.Link != p.Link) { c.Battery = null; c.LastStep = null; c.LightSet = false; c.LastColor = null; }
                    c.Reader.Stop();
                    Output.SetPulse(c.Pad, false);
                    c.Pad = p;
                    StartReader(c);
                    changed = true;
                }
            }
            if (changed) UpdateUi();
        }

        int FreeNumber()
        {
            for (int n = 1; ; n++)
                if (_ctls.Find(delegate (Ctl x) { return x.Number == n; }) == null) return n;
        }

        void StartReader(Ctl c)
        {
            PadReader r = new PadReader(c.Pad);
            c.Reader = r;
            r.BatteryChanged = delegate (Battery b) { _ui.BeginInvoke(new Action(delegate { if (c.Reader == r && _ctls.Contains(c)) OnBattery(c, b); })); };
            r.Stopped = delegate { _ui.BeginInvoke(new Action(delegate { if (c.Reader == r) Kick(); })); };
            r.PsHeld = delegate { _ui.BeginInvoke(new Action(delegate { if (Settings.GameOverlay) ShowOverlay(null); })); };
            r.Start();
        }

        void OnBattery(Ctl c, Battery b)
        {
            Battery prev = c.Battery;
            c.Battery = b;
            Known.Seen(c.Key, b.Percent);
            History.Add(c.Key, b.Percent, b.Charging);
            string name = NameOf(c);

            if (c.Greet)
            {
                c.Greet = false;
                Log.Write(name + " : " + b.Percent + " % (" + b.Describe() + ")");
                Notify(name + L.T(" connectée", " connected"), null, false, b.Percent, Via(c) + " · " + b.Describe());
                c.Fired50 = b.Level <= 5; c.Fired10 = b.Level <= 1; c.Fired5 = b.Level <= 0;   // pas d'alerte pour un seuil déjà dépassé
            }
            else if (b.Charging) { c.Fired50 = c.Fired10 = c.Fired5 = false; }   // en charge : on réarme
            else if (b.Level <= 0 && !c.Fired5)
            {
                Notify(name + L.T(" : batterie très faible", ": battery very low"), L.T("Elle va bientôt s'éteindre. Branche-la pour la recharger.", "It will turn off soon. Plug it in to recharge."), true, b.Percent, L.T("Sur batterie", "On battery"));
                AlertOnPad(c, 5);
                if (Settings.GameOverlay) ShowOverlay(L.T("Batterie très faible", "Battery very low"));
                c.Fired5 = c.Fired10 = c.Fired50 = true;
            }
            else if (b.Level <= 1 && !c.Fired10)
            {
                Notify(name + L.T(" : batterie faible", ": battery low"), L.T("Pense à la recharger.", "Time to charge it."), true, b.Percent, StatusLine(b));
                AlertOnPad(c, 3);
                if (Settings.GameOverlay) ShowOverlay(L.T("Batterie faible", "Battery low"));
                c.Fired10 = c.Fired50 = true;
            }
            else if (b.Level <= 5 && !c.Fired50)
            {
                Notify(name + L.T(" : batterie à moitié", ": battery at half"), null, false, b.Percent, StatusLine(b));
                c.Fired50 = true;
            }

            // apprentissage : durée entre deux changements de palier consécutifs dans le même sens
            bool down = prev != null && prev.Status == 0 && b.Status == 0 && b.Level == prev.Level - 1;
            bool up = prev != null && prev.Status == 1 && b.Status == 1 && b.Level == prev.Level + 1;
            if (down || up)
            {
                if (c.LastStep.HasValue) Settings.LearnStep(up, (DateTime.UtcNow - c.LastStep.Value).TotalMinutes);
                c.LastStep = DateTime.UtcNow;
            }
            else if (prev == null || prev.Status != b.Status || b.Level != prev.Level) c.LastStep = null;

            ApplyLight(c, false);
            UpdateUi();
        }

        // ---- barre lumineuse ----
        // force : renvoyer la couleur même si elle n'a pas changé (après une alerte ou « Identifier »)
        void ApplyLight(Ctl c, bool force)
        {
            if (c.Busy || c.Battery == null) return;   // rapports complets pas encore reçus, ou séquence en cours
            int mode = Settings.Lightbar;
            bool pulse = mode == 3 && c.Battery.Low;
            Output.SetPulse(c.Pad, pulse);
            if (pulse) { c.LastColor = null; c.LightSet = true; return; }
            byte[] rgb = Output.ColorFor(mode, c.Battery);
            if (mode == 0 && !c.LightSet && !force) { c.LightSet = true; return; }   // bleu d'origine : rien à envoyer
            bool same = c.LastColor != null && rgb[0] == c.LastColor[0] && rgb[1] == c.LastColor[1] && rgb[2] == c.LastColor[2];
            if (same && !force) return;
            Output.SetLight(c.Pad, rgb, !c.LightSet);
            c.LightSet = true;
            c.LastColor = rgb;
        }

        void AlertOnPad(Ctl c, int flashes)
        {
            c.Busy = true;
            Output.SetPulse(c.Pad, false);
            Output.Alert(c.Pad, flashes, delegate { _ui.BeginInvoke(new Action(delegate { c.Busy = false; ApplyLight(c, true); })); });
        }

        void Identify(Ctl c)
        {
            if (c.Busy) return;
            c.Busy = true;
            Output.SetPulse(c.Pad, false);
            Output.Identify(c.Pad, delegate { _ui.BeginInvoke(new Action(delegate { c.Busy = false; ApplyLight(c, true); })); });
        }

        static string Remaining(Battery b, out double minutes)
        {
            minutes = -1;
            if (b == null || b.Status != 0) return null;
            double steps = b.Level >= 10 ? 10 : b.Level + 0.5;
            minutes = steps * Settings.MinutesPerStep;
            return L.T("environ " + Fmt.Duration(minutes) + " restantes", "about " + Fmt.Duration(minutes) + " left");
        }

        static string ChargeRemaining(Battery b)
        {
            if (b == null || b.Status != 1) return null;
            double steps = Math.Max(0.5, 10 - b.Level - 0.5);
            return "~" + Fmt.Duration(steps * Settings.ChargeMinutesPerStep);
        }

        static string StatusLine(Battery b)
        {
            double m;
            string r = Remaining(b, out m);
            return r == null ? L.T("Sur batterie", "On battery") : L.T("Sur batterie · ", "On battery · ") + r;
        }

        // ---- arrêt ----
        void CheckIdle()
        {
            foreach (Ctl c in _ctls.ToArray())
            {
                if (c.Battery != null) History.Add(c.Key, c.Battery.Percent, c.Battery.Charging);   // un point toutes les 10 min
                int minutes = Settings.AutoOffMinutes;
                PadReader r = c.Reader;
                if (minutes <= 0 || c.Mode != Link.Bluetooth || c.PoweringOff || r.Dead) continue;
                DateTime now = DateTime.UtcNow;
                if ((now - r.LastReportUtc).TotalSeconds > 10) continue;   // on ne lit rien : impossible de juger de l'inactivité
                if ((now - r.LastActivityUtc).TotalMinutes < minutes) continue;
                Log.Write(NameOf(c) + " inactive depuis " + minutes + " min : extinction");
                PowerOff(c, true);
            }
            if (_flyout != null) _flyout.SetView(BuildView());
        }

        void PowerOff(Ctl c, bool auto)
        {
            ulong addr = DualSense.BluetoothAddress(c.Pad);
            if (addr == 0) { if (!auto) Notify(NameOf(c), L.T("La manette n'est pas connectée en Bluetooth.", "The controller isn't connected over Bluetooth."), false, -1, null); return; }
            Output.SetPulse(c.Pad, false);
            if (DualSense.PowerOff(addr))
            {
                c.PoweringOff = true;
                Log.Write(auto ? "Extinction automatique" : "Extinction demandée");
                if (auto) Notify(NameOf(c) + L.T(" éteinte", " turned off"), L.T("Aucune activité depuis ", "No activity for ") + Fmt.AutoOff(Settings.AutoOffMinutes) + ".", false, -1, null);
            }
            else
            {
                Log.Write("Échec de l'extinction (erreur " + Marshal.GetLastWin32Error() + ")");
                Notify(L.T("Impossible d'éteindre « " + NameOf(c) + " »", "Couldn't turn off “" + NameOf(c) + "”"), L.T("Maintiens le bouton PS enfoncé 10 secondes.", "Hold the PS button for 10 seconds."), true, -1, null);
            }
            Kick();
            UpdateUi();
        }

        void Quit()
        {
            _poll.Stop(); _debounce.Stop(); _idle.Stop(); _trim.Stop();
            if (_flyout != null) _flyout.Close();
            if (_overlay != null) _overlay.Close();
            foreach (Ctl c in _ctls) { c.Reader.Stop(); Output.SetPulse(c.Pad, false); History.Gap(c.Key); }
            SystemEvents.UserPreferenceChanged -= OnPrefChanged;
            _watcher.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            if (_hIcon != IntPtr.Zero) Native.DestroyIcon(_hIcon);
            Log.Write("Arrêt");
            _ui.InvokeShutdown();
        }

        void OnPrefChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.Color && e.Category != UserPreferenceCategory.VisualStyle) return;
            _ui.BeginInvoke(new Action(UpdateTray));
        }

        void Notify(string title, string text, bool warning, int percent, string progressStatus)
        {
            if (_flyout != null) { Log.Write("Notification non affichée (panneau ouvert) : " + title); return; }
            if (!Toasts.Show(title, text, warning, percent, progressStatus))
            {
                string body = text ?? "";
                if (percent >= 0) body = (percent + Fmt.Pct + " · " + progressStatus + (body.Length > 0 ? "\n" + body : ""));
                _tray.ShowBalloonTip(warning ? 10000 : 5000, title, body.Length > 0 ? body : title, warning ? WF.ToolTipIcon.Warning : WF.ToolTipIcon.Info);
            }
        }

        void ShowOverlay(string headline)
        {
            if (_ctls.Count == 0) return;
            if (_overlay == null || _overlay.Closing_)
            {
                if (_overlay != null) _overlay.Close();
                _overlay = new Overlay(Theme.Current());
                Overlay o = _overlay;
                o.Closed += delegate { if (_overlay == o) _overlay = null; ScheduleTrim(3); };
            }
            _overlay.ShowPads(BuildView().Pads, headline);
        }

        // Libère la mémoire inutilisée (WPF en réserve beaucoup après fermeture des fenêtres)
        void ScheduleTrim(int seconds) { _trim.Stop(); _trim.Interval = TimeSpan.FromSeconds(seconds); _trim.Start(); }

        static void TrimMemory()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Native.SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, new IntPtr(-1), new IntPtr(-1));
            }
            catch { }
        }

        // ---- interface ----
        View BuildView()
        {
            View v = new View
            {
                Connected = _ctls.Count,
                AutoOff = Settings.AutoOffMinutes,
                Lightbar = Settings.Lightbar,
                TrayStyle = Settings.TrayStyle,
                GameOverlay = Settings.GameOverlay,
                HideWhenOff = Settings.HideWhenOff,
                StartWithWindows = Settings.StartWithWindows
            };
            foreach (Ctl c in _ctls)
            {
                double mins;
                string rem = Remaining(c.Battery, out mins);
                v.Pads.Add(new PadView
                {
                    Key = c.Key, Name = NameOf(c), Mode = c.Mode, Battery = c.Battery,
                    Remaining = rem, RemainingMinutes = mins, ChargeRemaining = ChargeRemaining(c.Battery),
                    History = History.Get(c.Key), PoweringOff = c.PoweringOff
                });
            }
            // manettes connues, éteintes depuis moins de 7 jours
            foreach (KnownPad k in Known.All())
            {
                if (v.Pads.Count >= 4) break;
                if (Find(k.Key) != null || (DateTime.Now - k.LastSeen).TotalDays > 7) continue;
                v.Pads.Add(new PadView
                {
                    Key = k.Key, Name = k.Nickname ?? L.PadName, Mode = Link.None,
                    LastPercent = k.LastPercent, LastSeen = k.LastSeen, History = History.Get(k.Key)
                });
            }
            return v;
        }

        void UpdateUi()
        {
            UpdateTray();
            if (_flyout != null) _flyout.SetView(BuildView());
        }

        void UpdateTray()
        {
            // l'icône suit la manette la plus déchargée
            Ctl shown = null;
            foreach (Ctl c in _ctls)
                if (shown == null || (c.Battery != null && (shown.Battery == null || c.Battery.Percent < shown.Battery.Percent))) shown = c;

            string tip;
            if (_ctls.Count == 0) tip = L.T("Manette PS5 · non connectée", "DualSense · not connected");
            else
            {
                List<string> parts = new List<string>();
                foreach (Ctl c in _ctls)
                {
                    string via = c.Mode == Link.Bluetooth ? "Bluetooth" : "USB";
                    parts.Add(NameOf(c) + L.Colon + (c.Battery == null ? "…" : c.Battery.Percent + Fmt.Pct + (_ctls.Count == 1 ? " · " + via + (c.Battery.Status == 0 ? "" : " · " + c.Battery.Describe()) : "")));
                }
                tip = string.Join("\n", parts.ToArray());
            }
            if (tip.Length > 63) tip = tip.Substring(0, 63);

            int size = Math.Max(16, WF.SystemInformation.SmallIconSize.Width);
            IntPtr old = _hIcon;
            using (SD.Bitmap bmp = Art.TrayBitmap(size, shown == null ? Link.None : shown.Mode, shown == null ? null : shown.Battery, Theme.Current(), Settings.TrayStyle))
            {
                _hIcon = bmp.GetHicon();
                _tray.Icon = SD.Icon.FromHandle(_hIcon);
            }
            if (old != IntPtr.Zero) Native.DestroyIcon(old);
            _tray.Text = tip;

            bool visible = _ctls.Count > 0 || !Settings.HideWhenOff || _flyout != null;
            if (_tray.Visible != visible) _tray.Visible = visible;
        }

        void ToggleFlyout(SD.Rectangle anchor)
        {
            if (_flyout != null) { _flyout.CloseAnimated(); return; }
            if ((DateTime.UtcNow - _flyoutClosedAt).TotalMilliseconds < 400) return;   // le clic qui vient de fermer le panneau
            Refresh();
            Toasts.ClearAll();
            Flyout f = new Flyout(Theme.Current(), false);
            _flyout = f;
            f.PowerOffRequested += delegate (string key) { Ctl c = Find(key); if (c != null && c.Mode == Link.Bluetooth && !c.PoweringOff) PowerOff(c, false); };
            f.IdentifyRequested += delegate (string key) { Ctl c = Find(key); if (c != null) Identify(c); };
            f.RenameRequested += delegate (string key, string name)
            {
                Known.SetNickname(key, name);
                Log.Write("Surnom : " + (string.IsNullOrEmpty(name) ? "(par défaut)" : name));
                UpdateUi();
            };
            f.BluetoothSettingsRequested += OpenBluetoothSettings;
            f.AutoOffChosen += delegate (int m) { Settings.AutoOffMinutes = m; Log.Write("Arrêt automatique : " + Fmt.AutoOff(m)); UpdateUi(); };
            f.LightbarChosen += delegate (int l)
            {
                Settings.Lightbar = l;
                Log.Write("Barre lumineuse : " + Settings.LightbarChoices[l]);
                foreach (Ctl c in _ctls) ApplyLight(c, true);
                UpdateUi();
            };
            f.TrayStyleChosen += delegate (int s) { Settings.TrayStyle = s; UpdateUi(); };
            f.OverlayToggled += delegate (bool on)
            {
                Settings.GameOverlay = on;
                if (on) ShowOverlay(null);   // aperçu immédiat
                UpdateUi();
            };
            f.HideToggled += delegate (bool on) { Settings.HideWhenOff = on; UpdateUi(); };
            f.StartupToggled += delegate (bool on) { Settings.StartWithWindows = on; Log.Write("Lancement au démarrage : " + (on ? "oui" : "non")); UpdateUi(); };
            f.Closed += delegate
            {
                if (_flyout == f) _flyout = null;
                _flyoutClosedAt = DateTime.UtcNow;
                UpdateTray();
                ScheduleTrim(3);
            };
            f.SetView(BuildView());
            f.ShowAt(anchor);
        }

        // Lancé depuis le raccourci du menu Démarrer alors que l'appli tourne déjà
        public void ShowFromShortcut()
        {
            if (_flyout != null) { _flyout.Activate(); return; }
            _tray.Visible = true;
            WT.DispatcherTimer t = new WT.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            t.Tick += delegate { t.Stop(); _flyoutClosedAt = DateTime.MinValue; ToggleFlyout(TrayIconRect()); };
            t.Start();
        }

        void ShowTrayMenu()
        {
            if (_flyout != null) _flyout.Close();
            SD.Point p = WF.Cursor.Position;
            bool hide = Settings.HideWhenOff;
            string[] items = { L.T("Masquer l'icône quand la manette est éteinte", "Hide the icon when the controller is off"), L.T("Paramètres Bluetooth", "Bluetooth settings"), null, L.T("Quitter", "Quit") };
            int i = NativeMenu.Show(_watcher.Handle, p.X, p.Y, items, -1, new[] { hide, false, false, false }, Theme.Current().Light, true, false);
            if (i == 0) { Settings.HideWhenOff = !hide; UpdateTray(); }
            else if (i == 1) OpenBluetoothSettings();
            else if (i == 3) Quit();
        }

        void OpenBluetoothSettings()
        {
            try { Process.Start("ms-settings:bluetooth"); }
            catch (Exception e) { Log.Write("Paramètres Bluetooth : " + e.Message); }
        }

        SD.Rectangle TrayIconRect()
        {
            try
            {
                if (!_tray.Visible) return SD.Rectangle.Empty;
                FieldInfo fid = typeof(WF.NotifyIcon).GetField("id", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo fwin = typeof(WF.NotifyIcon).GetField("window", BindingFlags.NonPublic | BindingFlags.Instance);
                Native.NOTIFYICONIDENTIFIER nid = new Native.NOTIFYICONIDENTIFIER();
                nid.cbSize = (uint)Marshal.SizeOf(nid);
                nid.hWnd = ((WF.NativeWindow)fwin.GetValue(_tray)).Handle;
                nid.uID = (uint)(int)fid.GetValue(_tray);
                Native.RECT r;
                if (Native.Shell_NotifyIconGetRect(ref nid, out r) == 0 && r.Right > r.Left)
                    return SD.Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            }
            catch { }
            return SD.Rectangle.Empty;
        }

        // Épingle l'icône dans la barre des tâches au tout premier lancement (sans écraser un choix de l'utilisateur)
        void PromoteTrayIconOnce()
        {
            WT.DispatcherTimer t = new WT.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            t.Tick += delegate
            {
                t.Stop();
                try
                {
                    string exe = Assembly.GetEntryAssembly().Location;
                    using (RegistryKey root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings"))
                    {
                        if (root == null) return;
                        foreach (string name in root.GetSubKeyNames())
                        {
                            using (RegistryKey k = root.OpenSubKey(name, true))
                            {
                                if (k == null) continue;
                                string p = k.GetValue("ExecutablePath") as string;
                                if (p == null || !string.Equals(Path.GetFullPath(p), exe, StringComparison.OrdinalIgnoreCase)) continue;
                                if (k.GetValue("IsPromoted") == null) k.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                            }
                        }
                    }
                }
                catch { }
            };
            t.Start();
        }
    }

    // ------------------------------------------------------------------ démarrage

    static class Program
    {
        const string ShowEventName = @"Local\DualSenseBatteryShow";

        [STAThread]
        static void Main(string[] args)
        {
            Native.SetProcessDPIAware();
            if (args.Length >= 1 && args[0] == "--demo") { Demo.Run(args); return; }

            bool created;
            using (Mutex m = new Mutex(true, @"Local\DualSenseBatteryNotifier", out created))
            {
                if (!created)
                {
                    // déjà lancée : on lui demande d'ouvrir son panneau (raccourci du menu Démarrer)
                    EventWaitHandle other;
                    if (EventWaitHandle.TryOpenExisting(ShowEventName, out other)) using (other) other.Set();
                    return;
                }
                WT.Dispatcher.CurrentDispatcher.UnhandledException += delegate (object s, WT.DispatcherUnhandledExceptionEventArgs e)
                {
                    Log.Write("Erreur inattendue : " + e.Exception);
                    e.Handled = true;
                };
                TrayApp app = new TrayApp();
                WT.Dispatcher ui = WT.Dispatcher.CurrentDispatcher;
                using (EventWaitHandle show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                {
                    ThreadPool.RegisterWaitForSingleObject(show, delegate { ui.BeginInvoke(new Action(app.ShowFromShortcut)); }, null, -1, false);
                    WT.Dispatcher.Run();
                }
                GC.KeepAlive(m);
            }
        }
    }

    // Aperçus pour la mise au point : DualSenseBattery.exe --demo <scénario> <fichier.png> [clair|sombre]
    static class Demo
    {
        public static void Run(string[] a)
        {
            string scen = a.Length > 1 ? a[1] : "bt", outp = a.Length > 2 ? a[2] : "demo.png";
            Theme th = Theme.Current();
            if (a.Length > 3) th.Light = a[3] == "clair";
            // DSB_DEMO=1 : captures pour le README (bleu Windows par défaut, fond neutre plein écran derrière)
            if (Environment.GetEnvironmentVariable("DSB_DEMO") == "1")
            {
                th.Accent = th.Light ? SD.Color.FromArgb(0, 95, 184) : SD.Color.FromArgb(76, 194, 255);
                W.Window wall = new W.Window
                {
                    WindowStyle = W.WindowStyle.None, ResizeMode = W.ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, ShowActivated = false,
                    Left = 0, Top = 0, Width = W.SystemParameters.PrimaryScreenWidth, Height = W.SystemParameters.PrimaryScreenHeight,
                    Background = th.Light ? new WM.LinearGradientBrush(WM.Color.FromRgb(196, 214, 238), WM.Color.FromRgb(238, 226, 236), 35)
                                          : new WM.LinearGradientBrush(WM.Color.FromRgb(16, 28, 64), WM.Color.FromRgb(44, 16, 52), 35)
                };
                wall.Show();
            }

            if (scen == "ico") { Art.WriteIco(outp); return; }
            if (scen == "icones") { DumpIcons(outp, th, a.Length > 3 && a[3] == "complet"); return; }
            if (scen == "toast")
            {
                Toasts.Init();
                Toasts.Show("Manette PS5 connectée", null, false, 70, "Bluetooth · sur batterie");
                if (a.Length > 2 && a[2] == "effacer") { Thread.Sleep(1500); Toasts.ClearAll(); }
                return;
            }

            View v = new View { Connected = 1, AutoOff = 15, Lightbar = 3, TrayStyle = 0, GameOverlay = true, HideWhenOff = true, StartWithWindows = true };
            PadView p1 = new PadView { Key = "A", Name = L.PadName, Mode = Link.Bluetooth, Battery = new Battery { Level = 7, Status = 0 }, Remaining = null, RemainingMinutes = 210, History = FakeHistory(true) };
            PadView off = new PadView { Key = "C", Name = L.T("Manette de Tom", "Tom's controller"), Mode = Link.None, LastPercent = 40, LastSeen = DateTime.Now.AddHours(-2), History = FakeHistory(false) };
            if (scen == "charge") { p1.Mode = Link.Usb; p1.Battery = new Battery { Level = 4, Status = 1 }; p1.Remaining = null; p1.RemainingMinutes = -1; p1.ChargeRemaining = "~1 h 50"; }
            else if (scen == "faible") { p1.Battery = new Battery { Level = 1, Status = 0 }; p1.Remaining = "environ 45 min restantes"; p1.RemainingMinutes = 45; }
            if (scen == "aucune") { v.Connected = 0; off.Name = L.PadName; v.Pads.Add(off); }
            else if (scen == "deux") { p1.Name = L.T("Ma manette", "My controller"); v.Connected = 2; v.Pads.Add(p1); v.Pads.Add(new PadView { Key = "B", Name = L.Numbered(2), Mode = Link.Bluetooth, Battery = new Battery { Level = 3, Status = 0 }, Remaining = "environ 1 h 45 restantes", RemainingMinutes = 105, History = FakeHistory(true) }); }
            else if (scen == "eteinte") { v.Pads.Add(p1); v.Pads.Add(off); }
            else v.Pads.Add(p1);

            if (scen == "overlay")
            {
                // fond « jeu » derrière l'encart (sombre, ou clair avec « clair »)
                W.Window bg = new W.Window
                {
                    WindowStyle = W.WindowStyle.None, ResizeMode = W.ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, ShowActivated = false,
                    Width = 420, Height = 300, Left = W.SystemParameters.PrimaryScreenWidth - 420, Top = 0,
                    Background = th.Light ? (WM.Brush)new WM.LinearGradientBrush(WM.Color.FromRgb(214, 226, 240), WM.Color.FromRgb(246, 240, 228), 45)
                                          : new WM.LinearGradientBrush(WM.Color.FromRgb(58, 20, 26), WM.Color.FromRgb(8, 8, 14), 45)
                };
                bg.Show();
                Overlay o = new Overlay(th);
                v.Pads.Add(new PadView { Key = "B", Name = L.Numbered(2), Mode = Link.Bluetooth, Battery = new Battery { Level = 1, Status = 0 }, RemainingMinutes = 45 });
                o.ShowPads(v.Pads, a.Length > 4 ? L.T("Batterie faible", "Battery low") : null);
                WT.DispatcherTimer t1 = new WT.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
                t1.Tick += delegate { t1.Stop(); Capture(new WI.WindowInteropHelper(o).Handle, outp, 40); WT.Dispatcher.CurrentDispatcher.InvokeShutdown(); };
                t1.Start();
                WT.Dispatcher.Run();
                return;
            }

            Flyout f = new Flyout(th, true);
            f.SetView(v);
            f.ShowAt(SD.Rectangle.Empty);
            if (scen == "reglages") f.ShowPage(true);

            if (scen == "lumiere")
            {
                Thread shot = new Thread(delegate () { Thread.Sleep(1200); Capture(f.Hwnd, outp, 260); Native.PostMessage(f.Hwnd, 0x1F, IntPtr.Zero, IntPtr.Zero); });
                shot.IsBackground = true;
                WT.DispatcherTimer t0 = new WT.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                t0.Tick += delegate { t0.Stop(); f.ShowPage(true); shot.Start(); f.OpenLightbarMenu(); WT.Dispatcher.CurrentDispatcher.InvokeShutdown(); };
                t0.Start();
            }
            else
            {
                WT.DispatcherTimer t = new WT.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1100) };
                t.Tick += delegate { t.Stop(); Capture(f.Hwnd, outp, 40); WT.Dispatcher.CurrentDispatcher.InvokeShutdown(); };
                t.Start();
            }
            WT.Dispatcher.Run();
        }

        // Une journée type : décharge en jeu, une recharge, une déconnexion
        static List<Sample> FakeHistory(bool live)
        {
            List<Sample> l = new List<Sample>();
            DateTime t0 = DateTime.Now.AddHours(-23);
            int[] lv = { 100, 90, 80, 70, 60, 50, 40 };
            for (int i = 0; i < lv.Length; i++) l.Add(new Sample { T = t0.AddMinutes(i * 35), P = lv[i] });
            l.Add(new Sample { T = t0.AddMinutes(245), P = -1 });
            DateTime t1 = DateTime.Now.AddHours(-12);
            for (int i = 0; i < 6; i++) l.Add(new Sample { T = t1.AddMinutes(i * 20), P = 40 + i * 10, Charging = true });
            l.Add(new Sample { T = t1.AddMinutes(130), P = 100 });
            l.Add(new Sample { T = t1.AddMinutes(150), P = -1 });
            DateTime t2 = DateTime.Now.AddHours(-3);
            for (int i = 0; i < 4; i++) l.Add(new Sample { T = t2.AddMinutes(i * 40), P = 100 - i * 10 });
            if (!live) l.Add(new Sample { T = DateTime.Now.AddHours(-2), P = -1 });
            return l;
        }

        static void Capture(IntPtr hwnd, string path, int margin)
        {
            Native.RECT r;
            Native.GetWindowRect(hwnd, out r);
            SD.Rectangle rc = SD.Rectangle.FromLTRB(r.Left - 40, r.Top - margin, r.Right + 40, r.Bottom + 40);
            using (SD.Bitmap b = new SD.Bitmap(rc.Width, rc.Height))
            {
                using (SD.Graphics g = SD.Graphics.FromImage(b)) g.CopyFromScreen(rc.Location, SD.Point.Empty, rc.Size);
                b.Save(path, SDI.ImageFormat.Png);
            }
        }

        // Planche de contrôle des icônes : chaque état agrandi (pixels visibles), puis en vraie taille 16/20/24/32 px,
        // sur barre des tâches sombre puis claire. « complet » : tous les pourcentages du style « Pourcentage ».
        static void DumpIcons(string path, Theme th, bool full)
        {
            List<KeyValuePair<int, Battery>> states = new List<KeyValuePair<int, Battery>>();
            int[][] spec = full
                ? new[] { new[] { 1, 0, 0 }, new[] { 1, 1, 0 }, new[] { 1, 2, 0 }, new[] { 1, 3, 0 }, new[] { 1, 4, 0 }, new[] { 1, 5, 0 }, new[] { 1, 6, 0 }, new[] { 1, 7, 0 }, new[] { 1, 8, 0 }, new[] { 1, 9, 0 }, new[] { 1, 10, 0 }, new[] { 1, 4, 1 } }
                : new[] { new[] { 0, 7, 0 }, new[] { 0, 10, 2 }, new[] { 0, 4, 1 }, new[] { 0, 1, 0 },
                          new[] { 1, 7, 0 }, new[] { 1, 10, 2 }, new[] { 1, 4, 1 }, new[] { 1, 1, 0 },
                          new[] { 2, 7, 0 }, new[] { 2, 10, 2 }, new[] { 2, 4, 1 }, new[] { 2, 1, 0 } };
            foreach (int[] x in spec) states.Add(new KeyValuePair<int, Battery>(x[0], new Battery { Level = x[1], Status = x[2] }));

            int zoom = 5, z = 24, cell = z * zoom + 10, pad = 10;
            int[] sizes = { 16, 20, 24, 32 };
            int blockH = pad + z * zoom + pad + 32 * sizes.Length + 6 * sizes.Length + pad;
            using (SD.Bitmap sheet = new SD.Bitmap(pad + cell * states.Count, blockH * 2))
            using (SD.Graphics g = SD.Graphics.FromImage(sheet))
            {
                g.InterpolationMode = SD2.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = SD2.PixelOffsetMode.Half;
                for (int pass = 0; pass < 2; pass++)
                {
                    Theme t = new Theme { Light = pass == 1, Accent = th.Accent };
                    int y0 = pass * blockH;
                    using (SD.SolidBrush bg = new SD.SolidBrush(t.Light ? SD.Color.FromArgb(243, 243, 243) : SD.Color.FromArgb(28, 28, 28)))
                        g.FillRectangle(bg, 0, y0, sheet.Width, blockH);
                    for (int n = 0; n < states.Count; n++)
                    {
                        int x = pad + n * cell;
                        using (SD.Bitmap b = Art.TrayBitmap(z, Link.Bluetooth, states[n].Value, t, states[n].Key))
                            g.DrawImage(b, new SD.Rectangle(x, y0 + pad, z * zoom, z * zoom));
                        int y = y0 + pad + z * zoom + pad;
                        foreach (int s in sizes)
                        {
                            using (SD.Bitmap b = Art.TrayBitmap(s, Link.Bluetooth, states[n].Value, t, states[n].Key))
                                g.DrawImageUnscaled(b, x + (z * zoom - s) / 2, y + (32 - s) / 2);
                            y += 32 + 6;
                        }
                    }
                }
                sheet.Save(path, SDI.ImageFormat.Png);
            }
            Log.Write("Taille des icônes de la zone de notification ici : " + WF.SystemInformation.SmallIconSize.Width + " px");
        }
    }
}

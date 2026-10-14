using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;

namespace Diagnostico
{
    public class DiskHealth
    {
        public string Model = "", Kind = "Disco", Brand = "", Size = "", Status = "", Interface = "";
        public int Percent = -1;

        public string Title { get { return "Saúde do " + Kind + (Brand != "" ? " (" + Brand + ")" : ""); } }

        public string Value
        {
            get
            {
                string s = Status;
                if (Percent >= 0) s += (s != "" ? " - " : "") + Percent + "%";
                if (Size != "") s += (s != "" ? " | " : "") + Size;
                return s == "" ? "Desconhecido" : s;
            }
        }
    }

    public class MachineInfo
    {
        public string Fabricante = "", Modelo = "", Serial = "", Tipo = "", Processador = "", Memoria = "", Sistema = "";
        // Tela principal: resolução nativa (maior modo), taxa máxima nessa resolução e o modo em uso agora
        public int TelaLargura, TelaAltura, TelaHz, TelaLarguraAtual, TelaAlturaAtual, TelaHzAtual;
        public int BateriaPercent = -1;
        public string BateriaTexto = "";
        public List<DiskHealth> Discos = new List<DiskHealth>();
        public string FonteDiscos = "";
        public string DiskInfoTxt;   // relatório completo do CrystalDiskInfo, se disponível

        public string TelaValor
        {
            get
            {
                if (TelaLargura <= 0) return "";
                string s = TelaLargura + " x " + TelaAltura + (TelaHz > 0 ? " @ " + TelaHz + " Hz" : "");
                bool sameRes = TelaLarguraAtual == TelaLargura && TelaAlturaAtual == TelaAltura;
                if (TelaLarguraAtual > 0 && (!sameRes || TelaHzAtual != TelaHz))
                    s += "  (em uso: " + (sameRes ? "" : TelaLarguraAtual + " x " + TelaAlturaAtual + " ") + TelaHzAtual + " Hz)";
                return s;
            }
        }

        public string BateriaValor
        {
            get
            {
                if (BateriaPercent >= 0) return BateriaPercent + "% " + BateriaTexto;
                return BateriaTexto == "" ? "Sem bateria" : BateriaTexto;
            }
        }
    }

    static class SystemInfo
    {
        // WinPE (Strelec): não é o Windows do cliente que está rodando.
        public static readonly bool IsWinPE = DetectWinPE();

        static bool DetectWinPE()
        {
            try { using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\MiniNT")) return k != null; }
            catch { return false; }
        }

        static IEnumerable<ManagementObject> Query(string scope, string wql)
        {
            var list = new List<ManagementObject>();
            try
            {
                using (var s = new ManagementObjectSearcher(scope, wql))
                using (var res = s.Get())
                    foreach (ManagementObject o in res) list.Add(o);
            }
            catch { }
            return list;
        }

        static string Str(ManagementObject o, string prop)
        {
            try { var v = o[prop]; return v == null ? "" : v.ToString().Trim(); } catch { return ""; }
        }

        static string First(string cls, string prop, string scope = @"root\cimv2")
        {
            foreach (var o in Query(scope, "SELECT " + prop + " FROM " + cls))
            {
                string v = Str(o, prop);
                if (v != "") return v;
            }
            return "";
        }

        static bool Junk(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return true;
            string l = s.ToLowerInvariant();
            return l.Contains("to be filled") || l.Contains("default string") || l == "system product name" || l == "none" || l == "0";
        }

        public static MachineInfo Collect()
        {
            var m = new MachineInfo();
            m.Fabricante = First("Win32_ComputerSystem", "Manufacturer");
            m.Modelo = First("Win32_ComputerSystemProduct", "Name");
            if (Junk(m.Modelo)) m.Modelo = First("Win32_ComputerSystem", "Model");
            string version = First("Win32_ComputerSystemProduct", "Version");
            if (m.Fabricante.ToLowerInvariant().Contains("lenovo") && !Junk(version)) m.Modelo = version + " (" + m.Modelo + ")";
            m.Serial = First("Win32_BIOS", "SerialNumber");
            if (Junk(m.Serial)) m.Serial = First("Win32_BaseBoard", "SerialNumber");
            if (Junk(m.Serial)) m.Serial = "Not Applicable";
            m.Processador = Regex.Replace(First("Win32_Processor", "Name"), @"\s+", " ");
            m.Sistema = First("Win32_OperatingSystem", "Caption");

            long ram = 0;
            foreach (var o in Query(@"root\cimv2", "SELECT Capacity FROM Win32_PhysicalMemory"))
            { long c; if (long.TryParse(Str(o, "Capacity"), out c)) ram += c; }
            if (ram == 0) long.TryParse(First("Win32_ComputerSystem", "TotalPhysicalMemory"), out ram);
            if (ram > 0) m.Memoria = Math.Round(ram / 1073741824.0) + " GB";

            ReadDisplay(m);
            ReadBattery(m);
            m.Tipo = ChassisType(m.BateriaPercent >= 0 || m.BateriaTexto != "");
            ReadDisks(m);
            return m;
        }

        // 59 Hz é como o Windows informa 59,94 Hz; 143 -> 144 etc.
        static int RoundHz(int hz)
        {
            foreach (int std in new[] { 60, 75, 90, 120, 144, 165, 240, 360 }) if (hz == std - 1) return std;
            return hz;
        }

        // Lê do driver de vídeo todos os modos da tela principal.
        static void ReadDisplay(MachineInfo m)
        {
            try
            {
                var dm = new Native.DEVMODE();
                dm.dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.DEVMODE));
                if (Native.EnumDisplaySettings(null, Native.ENUM_CURRENT_SETTINGS, ref dm))
                {
                    bool rotated = dm.dmDisplayOrientation == 1 || dm.dmDisplayOrientation == 3;
                    m.TelaLarguraAtual = rotated ? dm.dmPelsHeight : dm.dmPelsWidth;
                    m.TelaAlturaAtual = rotated ? dm.dmPelsWidth : dm.dmPelsHeight;
                    m.TelaHzAtual = RoundHz(dm.dmDisplayFrequency);
                }
                long best = 0;
                for (int i = 0; i < 5000; i++)
                {
                    dm = new Native.DEVMODE();
                    dm.dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.DEVMODE));
                    if (!Native.EnumDisplaySettings(null, i, ref dm)) break;
                    int w = Math.Max(dm.dmPelsWidth, dm.dmPelsHeight), h = Math.Min(dm.dmPelsWidth, dm.dmPelsHeight);
                    long px = (long)w * h;
                    int hz = RoundHz(dm.dmDisplayFrequency);
                    if (px > best) { best = px; m.TelaLargura = w; m.TelaAltura = h; m.TelaHz = hz; }
                    else if (px == best && w == m.TelaLargura && hz > m.TelaHz) m.TelaHz = hz;
                }
                if (m.TelaLargura == 0) { m.TelaLargura = m.TelaLarguraAtual; m.TelaAltura = m.TelaAlturaAtual; m.TelaHz = m.TelaHzAtual; }
            }
            catch { }
        }

        static string ChassisType(bool hasBattery)
        {
            foreach (var o in Query(@"root\cimv2", "SELECT ChassisTypes FROM Win32_SystemEnclosure"))
            {
                var arr = o["ChassisTypes"] as ushort[];
                if (arr == null) continue;
                foreach (var t in arr)
                {
                    if (new ushort[] { 8, 9, 10, 11, 12, 14, 18, 21, 31, 32 }.Contains(t)) return t == 30 || t == 31 || t == 32 ? "notebook (2 em 1)" : "notebook";
                    if (t == 30) return "tablet";
                    if (t == 13) return "all-in-one";
                    if (new ushort[] { 3, 4, 5, 6, 7, 15, 16, 35, 36 }.Contains(t)) return "desktop";
                }
            }
            return hasBattery ? "notebook" : "desktop";
        }

        static void ReadBattery(MachineInfo m)
        {
            long design = 0, full = 0;
            foreach (var o in Query(@"root\wmi", "SELECT DesignedCapacity FROM BatteryStaticData"))
                long.TryParse(Str(o, "DesignedCapacity"), out design);
            foreach (var o in Query(@"root\wmi", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity"))
                long.TryParse(Str(o, "FullChargedCapacity"), out full);
            if (design > 0 && full > 0)
            {
                m.BateriaPercent = (int)Math.Min(100, Math.Round(full * 100.0 / design));
                m.BateriaTexto = m.BateriaPercent >= 80 ? "boa" : m.BateriaPercent >= 60 ? "ok" : m.BateriaPercent >= 40 ? "desgastada" : "ruim";
            }
            else if (Query(@"root\cimv2", "SELECT Name FROM Win32_Battery").Any())
                m.BateriaTexto = "presente (sem dados de desgaste)";
        }

        // ---------- discos ----------

        static void ReadDisks(MachineInfo m)
        {
            var crystal = Session.Cfg.Programas.FirstOrDefault(t => (t.Nome ?? "").ToLowerInvariant().Contains("crystal") && t.Exists);
            if (crystal != null)
            {
                try
                {
                    string txt = RunCrystalDiskInfo(crystal.FullPath);
                    if (txt != null)
                    {
                        m.DiskInfoTxt = txt;
                        m.Discos = ParseCrystal(txt);
                        if (m.Discos.Count > 0) { m.FonteDiscos = "CrystalDiskInfo"; return; }
                    }
                }
                catch { }
            }
            m.Discos = DisksFromWmi();
            m.FonteDiscos = "Windows (WMI)";
        }

        static string RunCrystalDiskInfo(string exe)
        {
            string dir = Path.GetDirectoryName(exe);
            string txt = Path.Combine(dir, "DiskInfo.txt");
            DateTime before = File.Exists(txt) ? File.GetLastWriteTime(txt) : DateTime.MinValue;
            using (var p = Process.Start(new ProcessStartInfo(exe, "/CopyExit") { WorkingDirectory = dir, UseShellExecute = true, WindowStyle = ProcessWindowStyle.Minimized }))
            {
                if (p != null && !p.WaitForExit(90000)) { try { p.Kill(); } catch { } }
            }
            if (!File.Exists(txt) || File.GetLastWriteTime(txt) <= before) return null;
            return File.ReadAllText(txt);
        }

        public static List<DiskHealth> ParseCrystal(string txt)
        {
            var list = new List<DiskHealth>();
            DiskHealth cur = null;
            foreach (var raw in txt.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                var kv = Regex.Match(line, @"^\s*([A-Za-z .]+?)\s*:\s*(.*)$");
                if (!kv.Success) continue;
                string key = kv.Groups[1].Value.Trim(), val = kv.Groups[2].Value.Trim();
                if (key == "Model") { cur = new DiskHealth { Model = val }; list.Add(cur); continue; }
                if (cur == null) continue;
                switch (key)
                {
                    case "Disk Size":
                        var sz = Regex.Match(val, @"([\d.,]+)\s*GB");
                        if (sz.Success) cur.Size = FormatSize(double.Parse(sz.Groups[1].Value.Replace(",", "."), System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    case "Health Status":
                        var h = Regex.Match(val, @"^(\w+)(?:\s*\((\d+)\s*%\))?");
                        if (h.Success)
                        {
                            cur.Status = Translate(h.Groups[1].Value);
                            if (h.Groups[2].Success) cur.Percent = int.Parse(h.Groups[2].Value);
                        }
                        break;
                    case "Interface": cur.Interface = val; break;
                    case "Rotation Rate":
                        if (val.Contains("SSD")) cur.Kind = "SSD";
                        else if (val.Contains("RPM")) cur.Kind = "HD";
                        break;
                }
            }
            foreach (var d in list)
            {
                if (d.Interface.Contains("NVM")) d.Kind = "SSD";
                d.Brand = BrandOf(d.Model);
            }
            return list;
        }

        static string Translate(string s)
        {
            // O DiskInfo.txt sai no idioma do CrystalDiskInfo: "Good" em inglês, "Saudável" em português...
            string t = s.ToLowerInvariant();
            if (t == "good" || t.StartsWith("saud") || t == "bom" || t == "boa") return "Bom";
            if (t == "caution" || t.StartsWith("aten") || t == "cuidado") return "Atenção";
            if (t == "bad" || t == "ruim") return "Ruim";
            return "Desconhecido";
        }

        static List<DiskHealth> DisksFromWmi()
        {
            var list = new List<DiskHealth>();
            foreach (var o in Query(@"root\Microsoft\Windows\Storage", "SELECT FriendlyName, MediaType, BusType, Size, HealthStatus FROM MSFT_PhysicalDisk"))
            {
                int bus; int.TryParse(Str(o, "BusType"), out bus);
                if (bus == 7 /* USB */) continue;   // ignora o próprio pendrive
                var d = new DiskHealth { Model = Str(o, "FriendlyName") };
                string media = Str(o, "MediaType");
                d.Kind = media == "4" || bus == 17 ? "SSD" : media == "3" ? "HD" : "Disco";
                if (bus == 17) d.Interface = "NVM Express";
                long size; if (long.TryParse(Str(o, "Size"), out size)) d.Size = FormatSize(size / 1e9);
                string hs = Str(o, "HealthStatus");
                d.Status = hs == "0" ? "Bom" : hs == "1" ? "Atenção" : hs == "2" ? "Ruim" : "Desconhecido";
                d.Brand = BrandOf(d.Model);
                list.Add(d);
            }
            if (list.Count == 0)
                foreach (var o in Query(@"root\cimv2", "SELECT Model, Size, Status, InterfaceType FROM Win32_DiskDrive"))
                {
                    if (Str(o, "InterfaceType") == "USB") continue;
                    var d = new DiskHealth { Model = Str(o, "Model") };
                    long size; if (long.TryParse(Str(o, "Size"), out size)) d.Size = FormatSize(size / 1e9);
                    d.Status = Str(o, "Status") == "OK" ? "Bom" : Str(o, "Status");
                    d.Brand = BrandOf(d.Model);
                    list.Add(d);
                }
            return list;
        }

        static string FormatSize(double gb)
        {
            if (gb >= 900) return Math.Round(gb / 1000.0, gb >= 1900 ? 0 : 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "TB";
            return Math.Round(gb) + "GB";
        }

        static readonly string[] Brands = { "Kingston", "SanDisk", "Samsung", "Western Digital", "WDC", "Seagate", "Toshiba", "Crucial",
            "Micron", "Intel", "Hitachi", "HGST", "ADATA", "Lexar", "SK hynix", "Hynix", "Netac", "Patriot", "Kioxia", "Transcend", "PNY",
            "Corsair", "Gigabyte", "Team", "Hikvision", "Apacer", "Phison", "Fujitsu", "LiteOn", "Lite-On", "Plextor", "Goodram", "Mushkin" };

        static string BrandOf(string model)
        {
            string m = (model ?? "").ToLowerInvariant();
            foreach (var b in Brands) if (m.Contains(b.ToLowerInvariant())) return b == "WDC" ? "Western Digital" : b;
            if (m.StartsWith("st")) return "Seagate";
            if (m.StartsWith("hgst") || m.StartsWith("hts")) return "HGST";
            if (m.StartsWith("mz") || m.StartsWith("pm")) return "Samsung";
            if (m.StartsWith("sa400") || m.StartsWith("kbg")) return "Kingston";
            var w = (model ?? "").Split(new[] { ' ', '_' }, StringSplitOptions.RemoveEmptyEntries);
            return w.Length > 0 ? w[0] : "";
        }
    }
}

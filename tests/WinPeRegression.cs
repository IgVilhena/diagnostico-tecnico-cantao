using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Diagnostico;

public static class WinPeRegression
{
    static int checks;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }

    static string FakePe(string root, string folder, string name, ushort machine)
    {
        string dir = Path.Combine(root, folder);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write((ushort)0x5a4d);
            writer.BaseStream.Position = 0x3c; writer.Write(128);
            writer.BaseStream.Position = 128; writer.Write(0x00004550); writer.Write(machine);
        }
        return path;
    }

    public static int Run()
    {
        AppConfig previous = Session.Cfg;
        bool wasPe = SystemInfo.IsWinPE;
        string root = Path.Combine(AppPaths.AppDir, "winpe-regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var cfg = Json.Serializer.Deserialize<AppConfig>("{\"Versao\":3,\"Tecnico\":\"João\",\"Programas\":[{\"Nome\":\"Teste\",\"Exe\":\"interno:teclado\"}]}");
            Check(cfg.Tema == "Claro" && cfg.MostrarDicas && cfg.PastaOS == @".\OS", "Legacy config keeps constructor defaults");
            Check(cfg.Programas[0].Espera == 8 && cfg.Programas[0].MaximizarAntesPrint, "Legacy tools keep constructor defaults");
            string configJson = Json.Serializer.Serialize(new AppConfig());
            Check(!configJson.Contains("FullPath") && !configJson.Contains("OsRoot") && !configJson.Contains("Exists"), "Computed properties are excluded");
            var restored = Json.Serializer.Deserialize<AppConfig>(configJson);
            Check(restored.Programas.Count == AppConfig.DefaultTools().Count && restored.Fotos[0].Codigo == "01_TampaSuperior", "Complete config round trip");
            string data = "{\"cliente\":\"João \\" + "\"Silva\\\"\",\"observacoes\":\"linha 1\\nlinha 2 <&> \\uD83D\\uDD27\",\"chave com / : ç\":true,\"nulo\":null,\"valor\":12.5,\"saude\":{\"discos\":[{\"modelo\":\"SSD\",\"percentual\":95}]},\"futuro\":[1,false,null]}";
            var order = Json.Serializer.Deserialize<Dictionary<string, object>>(data);
            var reloaded = Json.Serializer.Deserialize<Dictionary<string, object>>(Json.Pretty(Json.Serializer.Serialize(order)));
            Check(OsJson.Str(reloaded, "cliente") == "João \"Silva\"", "Quotes and Unicode survive");
            Check(OsJson.Str(reloaded, "observacoes").Contains("\nlinha 2 <&> 🔧"), "Escapes, markup and surrogate pair survive");
            Check((bool)reloaded["chave com / : ç"] && reloaded["nulo"] == null && Convert.ToDecimal(reloaded["valor"]) == 12.5m, "Mapped keys and scalar values survive");
            var disks = (ArrayList)OsJson.Section(reloaded, "saude")["discos"];
            Check(disks.Count == 1 && OsJson.Str((Dictionary<string, object>)disks[0], "modelo") == "SSD", "Disk arrays remain compatible with PDF generator");
            Check(((ArrayList)reloaded["futuro"]).Count == 3, "Unknown OS fields survive save");
            bool badJson = false;
            try { Json.Serializer.Deserialize<AppConfig>("{broken}"); } catch { badJson = true; }
            Check(badJson, "Malformed JSON is rejected");

            string x86 = FakePe(root, "x86", "HWiNFO32.exe", 0x014c);
            string x64 = FakePe(root, "x64", "HWiNFO64.exe", 0x8664);
            string arm = FakePe(root, "arm64", "DiskInfo64.exe", 0xaa64);
            Check(ToolDiscovery.Compatible(x86, false) && !ToolDiscovery.Compatible(x64, false) && ToolDiscovery.Compatible(x64, true), "PE architecture selection");
            Check(!ToolDiscovery.Compatible(arm, true), "ARM64 is not selected for Strelec x64");
            string script = Path.Combine(root, "tool.cmd");
            File.WriteAllText(script, "@echo off\r\nexit /b 0\r\n");
            Check(ToolDiscovery.Compatible(script, false), "Configured batch launchers remain supported");
            var files = ToolDiscovery.Executables(new[] { Path.Combine(root, "missing"), root, root });
            Check(files.Count == 3, "Missing folders do not abort search; duplicate roots are skipped");
            cfg.Programas = new List<ToolConfig> { ToolConfig.Make("HWiNFO - Hardware", @"Z:\not-present\HWiNFO64.exe", "HW", ""), ToolConfig.Make("Teclado", "interno:teclado", "KB", "") };
            Check(ToolDiscovery.FindMissing(cfg, files, false) == 2 && cfg.Programas[0].FullPath == x86, "x86 WinPE falls back from HWiNFO64 to HWiNFO32");
            cfg.Programas[0].Exe = @"Z:\not-present\HWiNFO64.exe";
            ToolDiscovery.FindMissing(cfg, files, true);
            Check(cfg.Programas[0].FullPath == x64, "x64 WinPE prefers native tool");
            cfg.Programas[0].Exe = x86;
            ToolDiscovery.FindMissing(cfg, files, true);
            Check(cfg.Programas[0].FullPath == x86, "Valid configured paths are preserved");

            typeof(SystemInfo).GetField("IsWinPE").SetValue(null, true);
            Session.Cfg = new AppConfig { Programas = new List<ToolConfig>() };
            var machine = new MachineInfo();
            SystemInfo.ApplyFallbacks(machine);
            Check(machine.Memoria != "" && machine.Processador != "", "Hardware fallback without WMI supplies RAM and CPU");
            Check(machine.Sistema.Contains("WinPE") && machine.Sistema.Contains("não avaliado"), "Live environment is not reported as installed Windows");
            Check(WinPeSupport.IsTemporaryPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows)), "WinPE system volume is recognized as temporary");
            var unavailable = typeof(SystemInfo).GetMethod("Query", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { @"root\missing_diagnostico_test", "SELECT * FROM MissingClass" });
            Check(((ICollection)unavailable).Count == 0, "Unavailable WMI provider does not abort diagnosis");

            string os = Path.Combine(root, "123");
            Directory.CreateDirectory(os);
            OsJson.Save(os, order);
            Check(OsJson.Section(OsJson.Load(os), "saude") != null, "Existing nested OS data persists");
            LaudoExport.Write(os, "Cliente", "Técnico", machine, Session.Cfg.Testes, new Dictionary<string, int>(), new Dictionary<string, int>(), true, "Teste WinPE");
            foreach (string path in Directory.GetFiles(Path.Combine(os, PhotoStore.DocDir), "*.json").Where(p => !Path.GetFileName(p).StartsWith("OS_")))
            {
                var report = Json.Serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                Check(!report.ContainsKey("campo_128") && !report.ContainsKey("campo_129") && !report.ContainsKey("check_129"), "WinPE does not mark the installed OS as working");
            }
            string pdf = PdfReport.Generate(os, new List<string>());
            Check(File.Exists(pdf) && File.ReadAllText(pdf).StartsWith("%PDF"), "PDF generation works with portable JSON");
            Check(!typeof(AppConfig).Assembly.GetReferencedAssemblies().Any(a => a.Name.StartsWith("System.Web")), "No ASP.NET assembly dependency");
            File.WriteAllText(Path.Combine(AppPaths.AppDir, "winpe-results.txt"), "PASS: " + checks + " checks (WinPE simulated on Windows; boot validation still required).");
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(AppPaths.AppDir, "winpe-results.txt"), "FAIL: " + ex);
            return 1;
        }
        finally
        {
            Session.Cfg = previous;
            typeof(SystemInfo).GetField("IsWinPE").SetValue(null, wasPe);
        }
    }
}

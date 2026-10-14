using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Diagnostico
{
    static class WinPeSupport
    {
        public static bool IsTemporaryPath(string path)
        {
            return string.Equals(Path.GetPathRoot(Path.GetFullPath(path)),
                Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)), StringComparison.OrdinalIgnoreCase);
        }

        public static bool PrepareStorage(AppConfig cfg)
        {
            while (true)
            {
                try
                {
                    string root = cfg.OsRoot;
                    if (IsTemporaryPath(root)) throw new IOException("A unidade do WinPE é temporária e será apagada ao reiniciar.");
                    Directory.CreateDirectory(root);
                    string probe = Path.Combine(root, ".diagnostico-" + Guid.NewGuid().ToString("N") + ".tmp");
                    using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
                    return true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Escolha uma pasta gravável no pendrive ou disco para guardar as OS.\n\n" + ex.Message,
                        "Pasta das OS no WinPE", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    using (var dialog = new FolderBrowserDialog { Description = "Salvar OS no pendrive ou disco (fora da unidade temporária do WinPE)", ShowNewFolderButton = true })
                    {
                        if (dialog.ShowDialog() != DialogResult.OK) return false;
                        cfg.PastaOS = AppPaths.ToRelative(dialog.SelectedPath);
                        cfg.Save();
                    }
                }
            }
        }
    }

    static class ToolDiscovery
    {
        public static IEnumerable<string> SearchRoots(AppConfig cfg)
        {
            var roots = new List<string> { cfg.ToolsRoot, Path.Combine(AppPaths.AppDir, "Ferramentas") };
            if (SystemInfo.IsWinPE)
            {
                // Somente pastas de ferramentas; não varre os documentos do Windows instalado.
                foreach (var drive in DriveInfo.GetDrives())
                    try { if (drive.IsReady && drive.DriveType != DriveType.Network) roots.Add(Path.Combine(drive.RootDirectory.FullName, "SSTR")); } catch { }
                string systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
                roots.Add(Path.Combine(systemRoot, "Program Files"));
                roots.Add(Path.Combine(systemRoot, "Program Files (x86)"));
                roots.Add(Path.Combine(systemRoot, "Programs"));
            }
            return roots.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists);
        }

        public static List<string> Executables(IEnumerable<string> roots)
        {
            var files = new List<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Stack<string>(roots.Reverse());
            while (pending.Count > 0)
            {
                string dir = pending.Pop();
                if (!visited.Add(dir)) continue;
                try
                {
                    if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) continue;
                    files.AddRange(Directory.GetFiles(dir, "*.exe").OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
                    foreach (string child in Directory.GetDirectories(dir).OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase)) pending.Push(child);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return files;
        }

        static readonly string[][] Families = {
            new[] { "HWiNFO64.exe", "HWiNFO32.exe", "HWiNFO.exe" },
            new[] { "DiskInfo64.exe", "DiskInfo32.exe", "DiskInfo.exe" },
            new[] { "HDDScan.exe" }, new[] { "h2testw.exe" },
            new[] { "BatteryInfoView.exe", "BatteryInfoView64.exe" },
            new[] { "FurMark.exe", "FurMark_GUI.exe" },
            new[] { "Superposition.exe" }, new[] { "Cinebench.exe" }
        };

        public static bool Compatible(string path, bool is64Bit)
        {
            try
            {
                string extension = Path.GetExtension(path);
                if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)) return File.Exists(path);
                using (var reader = new BinaryReader(File.OpenRead(path)))
                {
                    if (reader.ReadUInt16() != 0x5a4d) return false;
                    reader.BaseStream.Position = 0x3c;
                    int offset = reader.ReadInt32();
                    if (offset < 64 || offset > reader.BaseStream.Length - 6) return false;
                    reader.BaseStream.Position = offset;
                    if (reader.ReadUInt32() != 0x00004550) return false;
                    ushort machine = reader.ReadUInt16();
                    return machine == 0x014c || (is64Bit && machine == 0x8664);
                }
            }
            catch { return false; }
        }

        public static int FindMissing(AppConfig cfg, IEnumerable<string> files, bool is64Bit)
        {
            var available = files.ToList();
            int found = 0;
            foreach (var tool in cfg.Programas)
            {
                if (tool.IsUrl || tool.IsInternal) { found++; continue; }
                if (tool.Exists && Compatible(tool.FullPath, is64Bit)) { found++; continue; }
                string name = Path.GetFileName(tool.Exe ?? "");
                if (string.IsNullOrWhiteSpace(name)) continue;
                var names = Families.FirstOrDefault(f => f.Contains(name, StringComparer.OrdinalIgnoreCase)) ?? new[] { name };
                string hit = available.Where(f => names.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase) && Compatible(f, is64Bit))
                    .OrderBy(f => is64Bit && Path.GetFileNameWithoutExtension(f).EndsWith("64", StringComparison.OrdinalIgnoreCase) ? 0 : 1).FirstOrDefault();
                if (hit == null) continue;
                tool.Exe = AppPaths.ToRelative(hit);
                found++;
            }
            return found;
        }
    }
}

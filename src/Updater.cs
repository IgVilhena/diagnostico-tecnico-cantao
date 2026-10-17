using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Diagnostico
{
    public class UpdateReleaseInfo
    {
        public string TagName { get; set; }
        public string Version { get; set; }
        public string DownloadUrl { get; set; }
        public string HtmlUrl { get; set; }
    }

    public static class AppUpdater
    {
        public const string CurrentVersion = "0.5.0";
        public const string DefaultRepo = "IgVilhena/diagnostico-tecnico-cantao";

        static void EnableTls12()
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072 | SecurityProtocolType.Tls; }
            catch { }
        }

        // Remove o executável antigo (.old) deixado pela atualização anterior.
        public static void CleanupOldBinary()
        {
            try
            {
                string exePath = Application.ExecutablePath;
                if (string.IsNullOrEmpty(exePath)) return;
                string oldPath = exePath + ".old";
                if (!File.Exists(oldPath)) return;
                for (int i = 0; i < 5; i++)
                {
                    try { File.Delete(oldPath); return; }
                    catch { System.Threading.Thread.Sleep(150); }
                }
            }
            catch { }
        }

        public static string NormalizeVersion(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return "";
            var m = Regex.Match(tag.Trim(), @"v?(\d+(?:\.\d+){1,3})", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : "";
        }

        public static bool IsNewerVersion(string currentVersion, string remoteTag)
        {
            string cur = NormalizeVersion(currentVersion);
            string rem = NormalizeVersion(remoteTag);
            if (cur == "" || rem == "") return false;
            int[] a = ParseParts(cur), b = ParseParts(rem);
            int len = Math.Max(a.Length, b.Length);
            for (int i = 0; i < len; i++)
            {
                int va = i < a.Length ? a[i] : 0;
                int vb = i < b.Length ? b[i] : 0;
                if (vb != va) return vb > va;
            }
            return false;
        }

        static int[] ParseParts(string v)
        {
            string[] split = v.Split('.');
            int[] res = new int[split.Length];
            for (int i = 0; i < split.Length; i++) int.TryParse(split[i], out res[i]);
            return res;
        }

        public static UpdateReleaseInfo ParseReleaseJson(string json, string currentVersion = CurrentVersion)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            var root = Json.Serializer.Deserialize<Dictionary<string, object>>(json);
            if (root == null) return null;
            object draftObj;
            if (root.TryGetValue("draft", out draftObj) && draftObj is bool && (bool)draftObj) return null;

            string tag = OsJson.Str(root, "tag_name");
            if (!IsNewerVersion(currentVersion, tag)) return null;

            string downloadUrl = "";
            object assetsObj;
            if (root.TryGetValue("assets", out assetsObj) && assetsObj is ArrayList)
            {
                var assets = (ArrayList)assetsObj;
                foreach (var item in assets)
                {
                    var asset = item as Dictionary<string, object>;
                    if (asset == null) continue;
                    string name = OsJson.Str(asset, "name");
                    string url = OsJson.Str(asset, "browser_download_url");
                    if (string.IsNullOrEmpty(url)) continue;
                    if (string.Equals(name, "DiagnosticoTecnico.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = url;
                        break;
                    }
                    if (downloadUrl == "" && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        downloadUrl = url;
                }
            }

            if (string.IsNullOrEmpty(downloadUrl)) return null;
            return new UpdateReleaseInfo
            {
                TagName = tag,
                Version = NormalizeVersion(tag),
                DownloadUrl = downloadUrl,
                HtmlUrl = OsJson.Str(root, "html_url"),
            };
        }

        public static Task<UpdateReleaseInfo> CheckForUpdateAsync(string repo = DefaultRepo)
        {
            return Task.Run(() =>
            {
                try
                {
                    EnableTls12();
                    string url = "https://api.github.com/repos/" + (string.IsNullOrWhiteSpace(repo) ? DefaultRepo : repo.Trim()) + "/releases/latest";
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.Method = "GET";
                    req.UserAgent = "DiagnosticoTecnico/" + CurrentVersion;
                    req.Accept = "application/vnd.github+json";
                    req.Timeout = 6000;
                    req.ReadWriteTimeout = 6000;
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var stream = resp.GetResponseStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string json = reader.ReadToEnd();
                        return ParseReleaseJson(json, CurrentVersion);
                    }
                }
                catch
                {
                    return null;
                }
            });
        }

        public static bool IsValidPeFile(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length < 4096) return false;
                using (var fs = File.OpenRead(path))
                {
                    return fs.ReadByte() == 'M' && fs.ReadByte() == 'Z';
                }
            }
            catch { return false; }
        }

        // Baixa o novo .exe da Release, renomeia o executável atual para .old, coloca o novo no lugar e reinicia.
        public static async Task<bool> DownloadAndApplyAsync(UpdateReleaseInfo release, MainForm mainForm)
        {
            if (release == null || string.IsNullOrEmpty(release.DownloadUrl)) return false;
            string exePath = Application.ExecutablePath;
            string newPath = exePath + ".new";
            string oldPath = exePath + ".old";

            try
            {
                await Task.Run(() =>
                {
                    EnableTls12();
                    var req = (HttpWebRequest)WebRequest.Create(release.DownloadUrl);
                    req.Method = "GET";
                    req.UserAgent = "DiagnosticoTecnico/" + CurrentVersion;
                    req.Timeout = 30000;
                    req.ReadWriteTimeout = 30000;
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var input = resp.GetResponseStream())
                    using (var output = new FileStream(newPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        input.CopyTo(output);
                    }
                });

                if (!IsValidPeFile(newPath))
                {
                    try { if (File.Exists(newPath)) File.Delete(newPath); } catch { }
                    Toast.Show("O arquivo baixado da atualização é inválido ou está incompleto.", true);
                    return false;
                }

                if (File.Exists(oldPath)) File.Delete(oldPath);
                File.Move(exePath, oldPath);
                File.Move(newPath, exePath);

                Process.Start(new ProcessStartInfo(exePath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath),
                });

                if (mainForm != null && !mainForm.IsDisposed)
                    mainForm.CloseFromButton();
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    if (!File.Exists(exePath) && File.Exists(oldPath))
                        File.Move(oldPath, exePath);
                    if (File.Exists(newPath))
                        File.Delete(newPath);
                }
                catch { }
                Toast.Show("Erro ao atualizar: " + ex.Message, true);
                return false;
            }
        }
    }
}

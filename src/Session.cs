using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Diagnostico
{
    public enum ToolStatus { NaoEncontrado, Pendente, Aberto, SegundoPlano, FotoTirada }

    // Estado compartilhado entre as telas: configuração, OS ativa, etapa atual, programas em execução e prints.
    static class Session
    {
        public static AppConfig Cfg;
        public static Form MainWindow;

        // Pasta onde os prints (F8/F9) são salvos. Definida pela tela ativa.
        public static string ActiveFolder;
        public static string ActiveOs { get { return string.IsNullOrEmpty(ActiveFolder) ? "" : Path.GetFileName(ActiveFolder.TrimEnd('\\')); } }

        // Fase da OS: false = antes da manutenção, true = depois (arquivos ganham o sufixo _Depois).
        public static bool Depois;

        // Etapa selecionada: é ela que dá nome ao próximo print quando a janela ativa não identifica o programa
        // (sites no navegador, HDMI, as duas etapas do HWiNFO...).
        public static ToolConfig CurrentStep;

        public static event Action<string> PhotoSaved;

        // Um teste interno (teclado, áudio) informou o resultado: 0 = Sim, 1 = Não, 2 = Não tem.
        public static event Action<string, int> TestAnswered;
        public static void AnswerTest(string test, int answer) { if (TestAnswered != null) TestAnswered(test, answer); }

        public static string FolderForOs(string number)
        {
            number = PhotoStore.Sanitize((number ?? "").Trim());
            if (string.IsNullOrWhiteSpace(number) || number == "Print") return null;
            return Path.Combine(Cfg.OsRoot, number);
        }

        public static IEnumerable<ToolConfig> ToolsForMode(string mode)
        {
            var all = Cfg.Programas.Where(Allowed);
            switch (mode)
            {
                case "Rápido": return all.Where(t => t.ModoRapido && !IsConditional(t));
                case "Estresse": return all.Where(t => t.SoEstresse);
                case "Periféricos": return all.Where(t => t.SoPerifericos);
                case "Todos": return all;
                default: return all.Where(t => !IsConditional(t));   // Completo: tudo que a diretriz exige
            }
        }

        // Escolha do técnico (Cfg.TipoTestes): testes pelo próprio programa (offline), por sites (online) ou os dois.
        // Offline esconde as etapas que são sites; Online esconde o teste interno que tem site equivalente.
        public static bool Allowed(ToolConfig t)
        {
            if (t == null || t.Desativado) return false;
            switch (Cfg.TipoTestes)
            {
                case "Offline": return !t.IsUrl;
                case "Online": return !t.HasOnlineVersion;
                default: return true;
            }
        }

        // No modo Online (ou se o teste interno equivalente estiver desativado) o site deixa de ser só uma alternativa e passa a ser exigido.
        public static bool IsConditional(ToolConfig t)
        {
            if (t.IsUrl && (Cfg.TipoTestes == "Online" || (t.Arquivo == "PRINT_02_Teclado_Online" && !Cfg.Programas.Any(p => p.IsInternal && p.HasOnlineVersion && !p.Desativado))))
                return false;
            return t.Condicional;
        }

        // ---------- processos ----------

        static HashSet<string> runningCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static DateTime runningCacheAt = DateTime.MinValue;
        static readonly Dictionary<ToolConfig, DateTime> launchedAt = new Dictionary<ToolConfig, DateTime>();

        public static HashSet<string> RunningProcesses()
        {
            if ((DateTime.Now - runningCacheAt).TotalMilliseconds < 700) return runningCache;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses()) { try { set.Add(p.ProcessName); } catch { } finally { p.Dispose(); } }
            runningCache = set; runningCacheAt = DateTime.Now;
            return set;
        }

        public static string ProcessNameOf(IntPtr hwnd)
        {
            int pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            try { using (var p = Process.GetProcessById(pid)) return p.ProcessName; } catch { return ""; }
        }

        static bool SameProcess(ToolConfig t, string processName)
        {
            return t.ProcessName != "" && string.Equals(t.ProcessName, processName, StringComparison.OrdinalIgnoreCase);
        }

        // Descobre a qual etapa pertence o print da janela ativa.
        public static ToolConfig StepForWindow(IntPtr hwnd)
        {
            string name = ProcessNameOf(hwnd);
            var matches = string.IsNullOrEmpty(name) ? new List<ToolConfig>() : Cfg.Programas.Where(t => SameProcess(t, name)).ToList();
            if (matches.Count == 0) return CurrentStep;                       // navegador, área de trabalho...
            if (CurrentStep != null && matches.Contains(CurrentStep)) return CurrentStep;
            return matches.FirstOrDefault(t => !Evidence.Has(ActiveFolder, t.Codigo, Depois)) ?? matches[0];
        }

        public static IntPtr MainWindowOf(ToolConfig t)
        {
            if (t.ProcessName == "") return IntPtr.Zero;
            IntPtr found = IntPtr.Zero;
            foreach (var p in Process.GetProcessesByName(t.ProcessName))
            {
                // percorre todos mesmo depois de achar, para liberar cada Process
                try { if (found == IntPtr.Zero && p.MainWindowHandle != IntPtr.Zero) found = p.MainWindowHandle; }
                catch { }
                finally { p.Dispose(); }
            }
            return found;
        }

        public static ToolStatus StatusOf(ToolConfig t, string folder)
        {
            if (!t.Exists) return ToolStatus.NaoEncontrado;
            if (!string.IsNullOrEmpty(folder) && Evidence.Has(folder, t.Codigo, Depois)) return ToolStatus.FotoTirada;
            if (t.ProcessName == "" || !RunningProcesses().Contains(t.ProcessName))
                return launchedAt.ContainsKey(t) && t == CurrentStep ? ToolStatus.Aberto : ToolStatus.Pendente;
            var fg = Native.GetForegroundWindow();
            return SameProcess(t, ProcessNameOf(fg)) ? ToolStatus.Aberto : ToolStatus.SegundoPlano;
        }

        public static void Launch(ToolConfig t)
        {
            CurrentStep = t;
            if (!t.Exists) { Toast.Show(t.Nome + ": executável não encontrado. Veja em Configurações.", true); return; }
            try
            {
                if (t.IsInternal)
                {
                    launchedAt[t] = DateTime.Now;
                    InternalTests.Open(t);
                    return;
                }
                if (t.IsUrl)
                {
                    foreach (var url in t.AllUrls())
                        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    launchedAt[t] = DateTime.Now;
                    return;
                }

                if (!ToolDiscovery.Compatible(t.FullPath, Environment.Is64BitOperatingSystem))
                {
                    Toast.Show(t.Nome + ": executável incompatível com este ambiente. Use uma versão " +
                        (Environment.Is64BitOperatingSystem ? "x64 ou x86" : "x86 (32 bits)") + ".", true);
                    return;
                }

                var existing = MainWindowOf(t);
                if (existing != IntPtr.Zero)
                {
                    if (t.Janela == "maximizada") Native.ShowWindow(existing, Native.SW_MAXIMIZE);
                    Native.ForceForeground(existing);
                    if (!launchedAt.ContainsKey(t)) launchedAt[t] = DateTime.Now;
                    return;
                }

                var psi = new ProcessStartInfo(t.FullPath, t.Argumentos ?? "")
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(t.FullPath),
                    WindowStyle = t.Janela == "minimizada" ? ProcessWindowStyle.Minimized
                                : t.Janela == "normal" ? ProcessWindowStyle.Normal : ProcessWindowStyle.Maximized,
                };
                Process.Start(psi);
                launchedAt[t] = DateTime.Now;
                runningCacheAt = DateTime.MinValue;
                if (t.Modo == "automatico") QueueAutoCapture(t);
            }
            catch (Exception ex)
            {
                Toast.Show("Erro ao abrir " + t.Nome + ": " + ex.Message, true);
            }
        }

        // ---------- prints ----------

        static readonly Queue<ToolConfig> autoQueue = new Queue<ToolConfig>();
        static bool autoRunning;

        static void QueueAutoCapture(ToolConfig t)
        {
            autoQueue.Enqueue(t);
            if (!autoRunning) RunAutoQueue();
        }

        static async void RunAutoQueue()
        {
            autoRunning = true;
            try
            {
                while (autoQueue.Count > 0)
                {
                    var t = autoQueue.Dequeue();
                    await Task.Delay(Math.Max(1, t.Espera) * 1000);
                    IntPtr h = IntPtr.Zero;
                    for (int i = 0; i < 20 && h == IntPtr.Zero; i++) { h = MainWindowOf(t); if (h == IntPtr.Zero) await Task.Delay(500); }
                    if (h == IntPtr.Zero) { Toast.Show(t.Nome + ": janela não apareceu, tire o print manualmente (F9).", true); continue; }
                    Native.ForceForeground(h);
                    await Task.Delay(400);
                    await Capture(h, t, t.Codigo, t.MaximizarAntesPrint, true);
                }
            }
            finally { autoRunning = false; }
        }

        public static string EnsureActiveFolder()
        {
            if (string.IsNullOrEmpty(ActiveFolder))
            {
                Toast.Show("Informe o número da OS antes de tirar o print.", true);
                return null;
            }
            try { Directory.CreateDirectory(ActiveFolder); return ActiveFolder; }
            catch (Exception ex) { Toast.Show("Não foi possível criar a pasta da OS: " + ex.Message, true); return null; }
        }

        // F9: maximiza a janela ativa e tira o print.  F8: tira o print da tela como está.
        // Com PrintTelaInteira (Regra de Ouro da diretriz) os dois salvam a tela inteira, com barra de tarefas e relógio.
        public static async Task HotkeyPrint(bool wholeScreen)
        {
            IntPtr fg = Native.GetForegroundWindow();
            bool isMain = MainWindow != null && fg == MainWindow.Handle;
            ToolConfig step = isMain ? null : StepForWindow(fg);
            string code;
            if (isMain) code = "RESUMO_Diagnostico";
            else if (step != null) code = step.Codigo;
            else
            {
                string label = Native.WindowTitle(fg);
                if (string.IsNullOrWhiteSpace(label) || label.Length > 40) label = ProcessNameOf(fg);
                if (string.IsNullOrWhiteSpace(label)) label = "Tela";
                code = "EXTRA_" + PhotoStore.Sanitize(label).Replace(' ', '_');
            }
            // janelas do próprio programa (testes internos, diálogos) nunca são maximizadas à força
            int pid;
            Native.GetWindowThreadProcessId(fg, out pid);
            bool own = isMain || pid == Process.GetCurrentProcess().Id;
            bool maximize = !wholeScreen && !own && (step == null || step.MaximizarAntesPrint);
            await Capture(fg, step, code, maximize, wholeScreen || Cfg.PrintTelaInteira);
        }

        public static async Task<string> Capture(IntPtr hwnd, ToolConfig step, string code, bool maximize, bool fullScreen)
        {
            string folder = EnsureActiveFolder();
            if (folder == null) return null;
            Toast.HideCurrent();
            if (maximize && hwnd != IntPtr.Zero && !Native.IsZoomed(hwnd))
            {
                Native.ShowWindow(hwnd, Native.SW_MAXIMIZE);
                await Task.Delay(700);
            }
            else await Task.Delay(120);
            try
            {
                string path = Evidence.NewPath(folder, code, Depois, ".png");
                using (var bmp = fullScreen ? ScreenCapture.ScreenOf(hwnd) : ScreenCapture.Window(hwnd))
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                System.Media.SystemSounds.Asterisk.Play();

                string warn = MinimumTimeWarning(step);
                if (warn != null) Toast.Show(Path.GetFileName(path) + " salvo.\n" + warn, true);
                else Toast.Show("Print salvo:  " + Path.GetFileName(path));
                if (PhotoSaved != null) PhotoSaved(path);
                return path;
            }
            catch (Exception ex)
            {
                Log.Write("Falha no print: " + ex);
                Toast.Show("Falha no print: " + ex.Message, true);
                return null;
            }
        }

        // Ex.: FurMark deve rodar no mínimo 10 minutos antes da evidência.
        static string MinimumTimeWarning(ToolConfig step)
        {
            DateTime started;
            if (step == null || step.MinutosMinimos <= 0 || !launchedAt.TryGetValue(step, out started)) return null;
            double min = (DateTime.Now - started).TotalMinutes;
            if (min >= step.MinutosMinimos) return null;
            return "Atenção: " + step.Nome + " rodou só " + Math.Floor(min) + " min (a diretriz pede no mínimo " + step.MinutosMinimos + " min).";
        }

        // Esconde o próprio app, tira o print do que está atrás e volta.
        public static async Task CaptureBehind(bool wholeScreen)
        {
            if (EnsureActiveFolder() == null) return;
            var main = MainWindow;
            var prev = main.WindowState;
            main.WindowState = FormWindowState.Minimized;
            await Task.Delay(700);
            try { await HotkeyPrint(wholeScreen); }
            finally
            {
                // volta a janela mesmo se o print falhar
                main.WindowState = prev == FormWindowState.Minimized ? FormWindowState.Normal : prev;
                main.Activate();
            }
        }

        // Copia uma foto física (câmera/celular) para a OS já com o nome padronizado.
        public static int ImportPhotos(IWin32Window owner, string folder, string codigo, string title)
        {
            if (string.IsNullOrEmpty(folder)) { Toast.Show("Informe o número da OS primeiro.", true); return 0; }
            using (var dlg = new OpenFileDialog { Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp", Multiselect = true, Title = title })
            {
                if (dlg.ShowDialog(owner) != DialogResult.OK) return 0;
                int n = 0;
                try
                {
                    Directory.CreateDirectory(folder);
                    foreach (var f in dlg.FileNames)
                    {
                        string code = codigo ?? "EXTRA_" + PhotoStore.Sanitize(Path.GetFileNameWithoutExtension(f)).Replace(' ', '_');
                        File.Copy(f, Evidence.NewPath(folder, code, Depois, Path.GetExtension(f).ToLowerInvariant()));
                        n++;
                    }
                }
                catch (Exception ex) { Toast.Show("Erro ao copiar a foto: " + ex.Message, true); }
                if (n > 0 && PhotoSaved != null) PhotoSaved(folder);
                return n;
            }
        }
    }
}

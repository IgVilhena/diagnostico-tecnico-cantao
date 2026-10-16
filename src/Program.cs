using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Diagnóstico Técnico")]
[assembly: System.Reflection.AssemblyProduct("Diagnóstico Técnico")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace Diagnostico
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            try { Run(); }
            catch (Exception ex)
            {
                Log.Write("Falha na inicialização: " + ex);
                MessageBox.Show("Não foi possível iniciar o Diagnóstico Técnico.\n\n" + ex.Message +
                    "\n\nNo Strelec, verifique se o suporte .NET 4.5 ou superior está carregado. Consulte LEIA-ME-WINPE.txt e erro.log.",
                    "Diagnóstico Técnico", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.ExitCode = 1;
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void Run()
        {
            bool created;
            using (var mutex = new Mutex(true, "DiagnosticoTecnico_SingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show("O Diagnóstico Técnico já está aberto.", "Diagnóstico Técnico", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                try { Native.SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) =>
                {
                    Log.Write("Erro na tela: " + e.Exception);
                    try { Toast.Show("Erro: " + e.Exception.Message + " (detalhes em erro.log)", true); } catch { }
                };
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("ERRO FATAL: " + e.ExceptionObject);
                System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) => { Log.Write("Erro em tarefa: " + e.Exception); e.SetObserved(); };
                Log.Write("Programa aberto");

                using (var g = Graphics.FromHwnd(IntPtr.Zero)) Theme.Scale = g.DpiX / 96f;
                // A tela padrão foi desenhada para ~1370x920; em telas pequenas (ex.: 1366x768) reduz um pouco.
                var wa = Screen.PrimaryScreen.WorkingArea;
                float fit = Math.Min(wa.Width / (1370f * Theme.Scale), wa.Height / (900f * Theme.Scale));
                if (fit < 1f) Theme.Scale *= Math.Max(0.72f, fit);

                Session.Cfg = AppConfig.Load();
                if (SystemInfo.IsWinPE)
                {
                    Log.Write("Ambiente WinPE " + (Environment.Is64BitOperatingSystem ? "x64" : "x86") + "; CLR " + Environment.Version);
                    if (!WinPeSupport.PrepareStorage(Session.Cfg)) return;
                    var exes = ToolDiscovery.Executables(ToolDiscovery.SearchRoots(Session.Cfg));
                    ToolDiscovery.DiscoverPresets(Session.Cfg, exes, Environment.Is64BitOperatingSystem);
                    ToolDiscovery.FindMissing(Session.Cfg, exes, Environment.Is64BitOperatingSystem);
                    Session.Cfg.Save();
                }
                Theme.Configure(Session.Cfg);
#if SNAP
                // Build de teste: renderiza cada tela em PNG e sai.
                AppDomain.CurrentDomain.UnhandledException += (s, e) => System.IO.File.WriteAllText(System.IO.Path.Combine(AppPaths.AppDir, "snap_error.txt"), e.ExceptionObject.ToString());
                Application.ThreadException += (s, e) => System.IO.File.AppendAllText(System.IO.Path.Combine(AppPaths.AppDir, "snap_error.txt"), e.Exception.ToString());
                MainForm f = null;
                try { f = new MainForm(); } catch (Exception ex) { System.IO.File.WriteAllText(System.IO.Path.Combine(AppPaths.AppDir, "snap_error.txt"), ex.ToString()); return; }
                f.Shown += async (s, e) =>
                {
                    f.WindowState = FormWindowState.Normal;
                    await System.Threading.Tasks.Task.Delay(300);
                    string[] names = { "HomePage", "AnalysisPage", "EditPage", "PdfPage", "ConfigPage" };
                    for (int pi = 0; pi < 5; pi++)
                    {
                        Session.ActiveFolder = System.IO.Path.Combine(Session.Cfg.OsRoot, "12312");
                        f.Go(pi);
                        await System.Threading.Tasks.Task.Delay(pi == 1 ? 5000 : 2500);
                        using (var bmp = new Bitmap(f.Width, f.Height))
                        {
                            f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                            bmp.Save(System.IO.Path.Combine(AppPaths.AppDir, "snap_" + names[pi] + ".png"));
                        }
                    }
                    Session.ActiveFolder = System.IO.Path.Combine(Session.Cfg.OsRoot, "12312");
                    foreach (var dlg in new Form[] { new ChecklistDialog(Session.ActiveFolder), new CompareDialog(Session.ActiveFolder, null), new KeyboardTestForm(Session.Cfg.Programas[1]), new AudioTestDialog(Session.Cfg.Programas[1]) })
                    {
                        dlg.StartPosition = FormStartPosition.CenterScreen;
                        dlg.Show(f);
                        await System.Threading.Tasks.Task.Delay(1200);
                        using (var bmp = new Bitmap(dlg.ClientSize.Width, dlg.ClientSize.Height))
                        {
                            dlg.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                            bmp.Save(System.IO.Path.Combine(AppPaths.AppDir, "snap_" + dlg.GetType().Name + ".png"));
                        }
                        dlg.Close();
                    }
                    Session.CurrentStep = Session.Cfg.Programas[1];
                    Session.Depois = true;
                    string shot = await Session.Capture(Native.GetForegroundWindow(), Session.CurrentStep, Session.CurrentStep.Codigo, false, true);
                    System.IO.File.WriteAllText(System.IO.Path.Combine(AppPaths.AppDir, "snap_capture.txt"), shot ?? "FALHOU");
                    Session.Depois = false;
                    try
                    {
                        var mi = await System.Threading.Tasks.Task.Run(() => SystemInfo.Collect());
                        var a1 = new System.Collections.Generic.Dictionary<string, int> { { "Teclado", 0 }, { "Webcam", 1 }, { "HDMI", 2 }, { "Bateria", 0 }, { "Armazenamento", 0 }, { "Wi-Fi", 0 } };
                        var a2 = new System.Collections.Generic.Dictionary<string, int> { { "Teclado", 0 }, { "Webcam", 0 }, { "USB", 0 }, { "Armazenamento", 0 } };
                        System.IO.File.WriteAllText(System.IO.Path.Combine(AppPaths.AppDir, "snap_tela.txt"), mi.TelaValor + " | nativa " + mi.TelaLargura + "x" + mi.TelaAltura + "@" + mi.TelaHz + " | atual " + mi.TelaLarguraAtual + "x" + mi.TelaAlturaAtual + "@" + mi.TelaHzAtual);
                        LaudoExport.Write(Session.ActiveFolder, "Fulano de Tal", "joao", mi, Session.Cfg.Testes, a1, a2, true, "Trocada a pasta termica. Cliente ciente.");
                    }
                    catch (Exception ex) { System.IO.File.WriteAllText(System.IO.Path.Combine(AppPaths.AppDir, "snap_error.txt"), ex.ToString()); }
                    try { PdfReport.Generate(Session.ActiveFolder, PhotoStore.Photos(Session.ActiveFolder)); }
                    catch (Exception ex) { System.IO.File.WriteAllText(System.IO.Path.Combine(AppPaths.AppDir, "snap_error.txt"), ex.ToString()); }
                    f.CloseFromButton();
                };
                Application.Run(f);
#else
                Application.Run(new MainForm());
#endif
            }
        }
    }
}

namespace Diagnostico
{
    // Registro simples em erro.log (ao lado do .exe) para descobrir por que algo falhou.
    static class Log
    {
        static readonly object sync = new object();

        public static void Write(string msg)
        {
            try
            {
                lock (sync)
                {
                    string path = System.IO.Path.Combine(AppPaths.AppDir, "erro.log");
                    var fi = new System.IO.FileInfo(path);
                    if (fi.Exists && fi.Length > 1024 * 1024) fi.Delete();   // não deixa crescer sem limite
                    System.IO.File.AppendAllText(path, DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss") + "  " + msg + "\r\n");
                }
            }
            catch { }
        }
    }
}

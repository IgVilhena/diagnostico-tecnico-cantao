using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Diagnostico
{
    // Organização das pastas de OS:  OS\<numero>\OS_<numero>_<evidência>.png  +  subpastas Textos\ e Documentos\
    static class PhotoStore
    {
        public static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg", ".bmp" };
        public const string TextDir = "Textos";
        public const string DocDir = "Documentos";

        public static bool IsImage(string p) { return ImageExt.Contains(Path.GetExtension(p).ToLowerInvariant()); }

        public static List<string> Photos(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return new List<string>();
            return Directory.GetFiles(folder).Where(IsImage)
                .OrderBy(f => Evidence.SortGroup(f))
                .ThenBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static int PhotoCount(string folder)
        {
            try { return Directory.GetFiles(folder).Count(IsImage); } catch { return 0; }
        }

        // Nome amigável da evidência ("OS_123_PRINT_03_CrystalDiskInfo.png" -> "CrystalDiskInfo").
        public static string Label(string path) { return Evidence.DisplayName(path); }

        public static string Sanitize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "Print";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            s = Regex.Replace(s.Trim(), @"\s+", " ");
            return s.Length > 60 ? s.Substring(0, 60) : s;
        }

        public static Image LoadImage(string path)
        {
            // Lê via memória para não travar o arquivo (permite renomear/excluir depois).
            var bytes = File.ReadAllBytes(path);
            using (var ms = new MemoryStream(bytes))
            using (var img = Image.FromStream(ms))
                return new Bitmap(img);
        }

        public static Image LoadThumbnail(string path, int maxW)
        {
            try
            {
                using (var img = LoadImage(path))
                {
                    float k = Math.Min(1f, maxW / (float)img.Width);
                    int w = Math.Max(1, (int)(img.Width * k)), h = Math.Max(1, (int)(img.Height * k));
                    var bmp = new Bitmap(w, h);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(img, 0, 0, w, h);
                    }
                    return bmp;
                }
            }
            catch { return null; }
        }

        public static void OpenInShell(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch
            {
                try { Process.Start("explorer.exe", "/select,\"" + path + "\""); }
                catch (Exception ex) { Toast.Show("Não foi possível abrir: " + ex.Message, true); }
            }
        }

        public static void OpenFolder(string folder)
        {
            try { Directory.CreateDirectory(folder); Process.Start("explorer.exe", "\"" + folder + "\""); }
            catch (Exception ex) { Toast.Show("Não foi possível abrir a pasta: " + ex.Message, true); }
        }
    }

    static class ScreenCapture
    {
        public static Bitmap Region(Rectangle r)
        {
            r.Intersect(SystemInformation.VirtualScreen);
            if (r.Width <= 0 || r.Height <= 0) throw new InvalidOperationException("Janela fora da tela.");
            var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(r.Location, Point.Empty, r.Size, CopyPixelOperation.SourceCopy);
            return bmp;
        }

        public static Bitmap Window(IntPtr hwnd) { return Region(Native.WindowBounds(hwnd)); }

        // Tela inteira do monitor onde está a janela ativa.
        public static Bitmap ScreenOf(IntPtr hwnd)
        {
            var screen = hwnd != IntPtr.Zero ? Screen.FromHandle(hwnd) : Screen.FromPoint(Cursor.Position);
            return Region(screen.Bounds);
        }
    }

    // Aviso flutuante no canto da tela que não rouba o foco (funciona com o app em segundo plano).
    class Toast : Form
    {
        static Toast current;
        readonly string message;
        readonly bool error;
        readonly Timer timer = new Timer();

        Toast(string message, bool error)
        {
            this.message = message; this.error = error;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.TitleBar;
            Font = Theme.Body;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            var size = TextRenderer.MeasureText(message, Font, new Size(Theme.S(460), 0), TextFormatFlags.WordBreak);
            Size = new Size(size.Width + Theme.S(56), Math.Max(Theme.S(56), size.Height + Theme.S(28)));
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(wa.Right - Width - Theme.S(20), wa.Bottom - Height - Theme.S(20));
            timer.Interval = error ? 4500 : 2600;
            timer.Tick += (s, e) => Close();
            Click += (s, e) => Close();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 /*NOACTIVATE*/ | 0x00000080 /*TOOLWINDOW*/ | 0x00000008 /*TOPMOST*/; return cp; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Smooth(g);
            using (var b = new SolidBrush(error ? Theme.PillRed[1] : Theme.Accent))
                g.FillRectangle(b, 0, 0, Theme.S(6), Height);
            using (var pen = new Pen(Theme.Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            TextRenderer.DrawText(g, message, Font, new Rectangle(Theme.S(22), 0, Width - Theme.S(34), Height), Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); timer.Start(); }
        protected override void OnFormClosed(FormClosedEventArgs e) { timer.Dispose(); if (current == this) current = null; base.OnFormClosed(e); }

        // Some com o aviso antes de um print em tela inteira, para ele não aparecer na evidência.
        public static void HideCurrent()
        {
            if (current != null && !current.IsDisposed) current.Close();
        }

        public static void Show(string message, bool error = false)
        {
            if (current != null && !current.IsDisposed) current.Close();
            current = new Toast(message, error);
            current.Show();
            if (error) System.Media.SystemSounds.Exclamation.Play();
        }
    }
}

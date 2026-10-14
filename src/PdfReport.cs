using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Diagnostico
{
    // Gerador de PDF mínimo, sem bibliotecas externas: cada foto vira uma página (JPEG embutido).
    class PdfWriter
    {
        readonly MemoryStream body = new MemoryStream();
        readonly List<long> offsets = new List<long>();
        readonly List<int> pageIds = new List<int>();
        static readonly Encoding Ansi = Encoding.GetEncoding(1252);
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const int PagesId = 2, FontId = 3, FontBoldId = 4;

        public PdfWriter()
        {
            Raw("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");
            offsets.Add(0); offsets.Add(0); offsets.Add(0); offsets.Add(0); // reserva 1..4
            WriteObject(FontId, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
            WriteObject(FontBoldId, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        }

        void Raw(string s) { var b = Ansi.GetBytes(s); body.Write(b, 0, b.Length); }
        void Raw(byte[] b) { body.Write(b, 0, b.Length); }

        int NewId() { offsets.Add(0); return offsets.Count; }

        void WriteObject(int id, string content)
        {
            offsets[id - 1] = body.Position;
            Raw(id + " 0 obj\n" + content + "\nendobj\n");
        }

        void WriteStream(int id, string dict, byte[] data)
        {
            offsets[id - 1] = body.Position;
            Raw(id + " 0 obj\n<< " + dict + " /Length " + data.Length + " >>\nstream\n");
            Raw(data);
            Raw("\nendstream\nendobj\n");
        }

        static string N(double v) { return v.ToString("0.##", Inv); }

        public static string Esc(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)").Replace("\r", "").Replace("\n", " ");
        }

        public static string Text(double x, double y, double size, string text, bool bold = false, string rgb = "0.1 0.1 0.12")
        {
            return "BT /" + (bold ? "F2" : "F1") + " " + N(size) + " Tf " + rgb + " rg " + N(x) + " " + N(y) + " Td (" + Esc(text) + ") Tj ET\n";
        }

        // Largura aproximada do texto em Helvetica (para cortar linhas longas).
        public static double Width(string s, double size) { return (s ?? "").Length * size * 0.5; }

        public void AddPage(double w, double h, string content, Dictionary<string, int> images = null)
        {
            int contentId = NewId();
            WriteStream(contentId, "", Ansi.GetBytes(content));
            string xobj = "";
            if (images != null && images.Count > 0)
                xobj = " /XObject << " + string.Join(" ", images.Select(kv => "/" + kv.Key + " " + kv.Value + " 0 R")) + " >>";
            int pageId = NewId();
            WriteObject(pageId, "<< /Type /Page /Parent " + PagesId + " 0 R /MediaBox [0 0 " + N(w) + " " + N(h) + "] " +
                "/Resources << /Font << /F1 " + FontId + " 0 R /F2 " + FontBoldId + " 0 R >>" + xobj + " >> /Contents " + contentId + " 0 R >>");
            pageIds.Add(pageId);
        }

        public int AddJpeg(Image img, out int w, out int h)
        {
            w = img.Width; h = img.Height;
            byte[] jpg;
            using (var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format24bppRgb))
            {
                using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.White); g.DrawImage(img, 0, 0, img.Width, img.Height); }
                var codec = ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
                var ps = new EncoderParameters(1);
                ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
                using (var ms = new MemoryStream()) { bmp.Save(ms, codec, ps); jpg = ms.ToArray(); }
            }
            int id = NewId();
            WriteStream(id, "/Type /XObject /Subtype /Image /Width " + w + " /Height " + h +
                " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode", jpg);
            return id;
        }

        public void Save(string path, string title)
        {
            WriteObject(PagesId, "<< /Type /Pages /Kids [" + string.Join(" ", pageIds.Select(i => i + " 0 R")) + "] /Count " + pageIds.Count + " >>");
            WriteObject(1, "<< /Type /Catalog /Pages " + PagesId + " 0 R >>");
            int infoId = NewId();
            WriteObject(infoId, "<< /Title (" + Esc(title) + ") /Producer (Diagnostico Tecnico) /CreationDate (D:" + DateTime.Now.ToString("yyyyMMddHHmmss") + ") >>");
            long xref = body.Position;
            var sb = new StringBuilder();
            sb.Append("xref\n0 " + (offsets.Count + 1) + "\n0000000000 65535 f \n");
            foreach (var o in offsets) sb.Append(o.ToString("0000000000") + " 00000 n \n");
            sb.Append("trailer\n<< /Size " + (offsets.Count + 1) + " /Root 1 0 R /Info " + infoId + " 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
            Raw(sb.ToString());
            File.WriteAllBytes(path, body.ToArray());
        }
    }

    // Monta as páginas de texto do laudo (A4 retrato), quebrando de página quando acaba o espaço.
    class LaudoPages
    {
        public readonly List<StringBuilder> Pages = new List<StringBuilder>();
        const double M = 50, W = 595, H = 842;
        double y;
        StringBuilder c;

        public const string Gray = "0.4 0.42 0.47", Green = "0.1 0.55 0.25", Red = "0.8 0.15 0.15", Dark = "0.1 0.1 0.12";

        static string Fmt(double v) { return v.ToString("0.##", CultureInfo.InvariantCulture); }

        public LaudoPages(string os, string date)
        {
            NewPage();
            c.Append("0.11 0.12 0.15 rg 0 762 595 80 re f\n");
            c.Append(PdfWriter.Text(M, 800, 22, "Relatório de Diagnóstico Técnico", true, "1 1 1"));
            c.Append(PdfWriter.Text(M, 778, 12, "Ordem de Serviço " + os + "   |   " + date, false, "1 1 0.33"));
            y = 730;
        }

        void NewPage() { c = new StringBuilder(); Pages.Add(c); y = H - 60; }
        void Need(double h) { if (y - h < 50) NewPage(); }

        static string Fit(string s, double size, double width)
        {
            s = s ?? "";
            int max = Math.Max(4, (int)(width / (size * 0.5)));
            return s.Length > max ? s.Substring(0, max - 3) + "..." : s;
        }

        public void Section(string title)
        {
            Need(70);
            y -= 10;
            c.Append(PdfWriter.Text(M, y, 13, title, true));
            c.Append("0.85 0.85 0.88 RG 0.8 w " + Fmt(M) + " " + Fmt(y - 6) + " m " + Fmt(W - M) + " " + Fmt(y - 6) + " l S\n");
            y -= 26;
        }

        public void Row(string k, string v, string color = Dark)
        {
            Need(19);
            c.Append(PdfWriter.Text(M, y, 10.5, Fit(k, 10.5, 190), false, Gray));
            c.Append(PdfWriter.Text(M + 200, y, 10.5, Fit(v, 10.5, 295), true, color));
            y -= 19;
        }

        public void Row3(string k, string a, string b, bool header = false)
        {
            Need(19);
            c.Append(PdfWriter.Text(M, y, 10.5, Fit(k, 10.5, 150), header, Gray));
            c.Append(PdfWriter.Text(M + 160, y, 10.5, Fit(a, 10.5, 160), !header, header ? Gray : Dark));
            c.Append(PdfWriter.Text(M + 330, y, 10.5, Fit(b, 10.5, 160), !header, header ? Gray : Dark));
            y -= 19;
        }

        public void Paragraph(string text)
        {
            foreach (var rawLine in (text ?? "").Replace("\r", "").Split('\n'))
            {
                var line = new StringBuilder();
                foreach (var word in rawLine.Split(' '))
                {
                    if (line.Length + word.Length + 1 > 92) { Need(16); c.Append(PdfWriter.Text(M, y, 10.5, line.ToString())); y -= 16; line.Clear(); }
                    if (line.Length > 0) line.Append(' ');
                    line.Append(word);
                }
                Need(16); c.Append(PdfWriter.Text(M, y, 10.5, line.ToString())); y -= 16;
            }
        }
    }

    static class PdfReport
    {
        // Gera o relatório: laudo (dados do JSON, checklist, comparação) + uma página por foto/print.
        public static string Generate(string folder, IList<string> photos)
        {
            string os = Path.GetFileName(folder.TrimEnd('\\'));
            var pdf = new PdfWriter();
            string date = DateTime.Now.ToString("dd/MM/yyyy HH:mm");

            // O número de páginas do laudo define a numeração das fotos; monta de novo se mudar.
            var laudo = Laudo(folder, os, date, photos, 1);
            for (int pass = 0, used = 1; pass < 3 && laudo.Pages.Count != used; pass++)
            {
                used = laudo.Pages.Count;
                laudo = Laudo(folder, os, date, photos, used);
            }
            int first = laudo.Pages.Count, total = first + photos.Count;

            for (int i = 0; i < laudo.Pages.Count; i++)
            {
                laudo.Pages[i].Append(PdfWriter.Text(595 - 50 - 60, 20, 8, "Página " + (i + 1) + "/" + total, false, "0.45 0.45 0.5"));
                pdf.AddPage(595, 842, laudo.Pages[i].ToString());
            }

            for (int i = 0; i < photos.Count; i++)
            {
                using (var img = PhotoStore.LoadImage(photos[i]))
                {
                    int iw, ih;
                    int imgId = pdf.AddJpeg(img, out iw, out ih);
                    bool landscape = iw > ih;
                    double pw = landscape ? 842 : 595, ph = landscape ? 595 : 842;
                    double m = 28, top = 46, bottom = 30;
                    double aw = pw - m * 2, ah = ph - top - bottom;
                    double k = Math.Min(aw / iw, ah / ih);
                    double w = iw * k, h = ih * k;
                    double x = (pw - w) / 2, y = bottom + (ah - h) / 2;
                    var c = new StringBuilder();
                    c.Append(PdfWriter.Text(m, ph - 30, 12, "OS " + os + "  -  " + PhotoStore.Label(photos[i]), true));
                    c.Append(PdfWriter.Text(pw - m - 120, ph - 30, 9, date, false, "0.45 0.45 0.5"));
                    c.Append("0.85 0.85 0.88 RG 0.8 w " + Fmt(m) + " " + Fmt(ph - 38) + " m " + Fmt(pw - m) + " " + Fmt(ph - 38) + " l S\n");
                    c.Append("q " + Fmt(w) + " 0 0 " + Fmt(h) + " " + Fmt(x) + " " + Fmt(y) + " cm /Im0 Do Q\n");
                    c.Append(PdfWriter.Text(m, 14, 8, Path.GetFileName(photos[i]), false, "0.45 0.45 0.5"));
                    c.Append(PdfWriter.Text(pw - m - 60, 14, 8, "Página " + (first + i + 1) + "/" + total, false, "0.45 0.45 0.5"));
                    pdf.AddPage(pw, ph, c.ToString(), new Dictionary<string, int> { { "Im0", imgId } });
                }
            }

            string docDir = Path.Combine(folder, PhotoStore.DocDir);
            Directory.CreateDirectory(docDir);
            string outPath = Path.Combine(docDir, "Relatorio_OS_" + PhotoStore.Sanitize(os) + ".pdf");
            pdf.Save(outPath, "Relatório OS " + os);
            return outPath;
        }

        static string Fmt(double v) { return v.ToString("0.##", CultureInfo.InvariantCulture); }

        static LaudoPages Laudo(string folder, string os, string date, IList<string> photos, int laudoPages)
        {
            var p = new LaudoPages(os, date);
            var data = File.Exists(OsJson.PathOf(folder)) ? OsJson.Load(folder) : null;

            var eq = OsJson.Section(data, "equipamento");
            p.Section("Equipamento");
            p.Row("Ordem de Serviço", os);
            if (data != null)
            {
                if (OsJson.Str(data, "cliente") != "") p.Row("Cliente", OsJson.Str(data, "cliente"));
                p.Row("Técnico", OsJson.Str(data, "tecnico"));
            }
            if (eq != null)
            {
                p.Row("Fabricante", OsJson.Str(eq, "fabricante"));
                p.Row("Modelo", OsJson.Str(eq, "modelo"));
                p.Row("Serial", OsJson.Str(eq, "serial"));
                p.Row("Tipo", OsJson.Str(eq, "tipo"));
                p.Row("Processador", OsJson.Str(eq, "processador"));
                p.Row("Memória RAM", OsJson.Str(eq, "memoria"));
                if (OsJson.Str(eq, "tela") != "") p.Row("Tela", OsJson.Str(eq, "tela"));
            }

            string[] saudeKeys = { "saude", "saude_depois" };
            string[] saudeTitles = { "Saúde dos componentes", "Saúde dos componentes (depois da manutenção)" };
            for (int i = 0; i < 2; i++)
            {
                var saude = OsJson.Section(data, saudeKeys[i]);
                if (saude == null) continue;
                p.Section(saudeTitles[i]);
                var discos = saude.ContainsKey("discos") ? saude["discos"] as System.Collections.ArrayList : null;
                if (discos != null)
                    foreach (var d in discos.OfType<Dictionary<string, object>>())
                        p.Row(OsJson.Str(d, "titulo"), OsJson.Str(d, "valor") + "  (" + OsJson.Str(d, "modelo") + ")");
                p.Row("Bateria", OsJson.Str(saude, "bateria"));
            }

            var antes = OsJson.Section(data, "testes");
            var depois = OsJson.Section(data, "testes_depois");
            if (antes != null && antes.Count > 0)
            {
                p.Section("Testes funcionais");
                p.Row3("Teste", "Antes", "Depois", true);
                foreach (var kv in antes)
                {
                    object dv = null;
                    if (depois != null) depois.TryGetValue(kv.Key, out dv);
                    p.Row3(kv.Key, Answer(kv.Value as string), Answer(dv as string));
                }
            }

            var comp = OsJson.Section(data, "comparacao");
            if (comp != null && comp.Values.OfType<Dictionary<string, object>>().Any(v => OsJson.Str(v, "antes") != "" || OsJson.Str(v, "depois") != ""))
            {
                p.Section("Validação pós-manutenção");
                p.Row3("Item", "Antes", "Depois", true);
                foreach (var kv in comp)
                {
                    var v = kv.Value as Dictionary<string, object>;
                    p.Row3(kv.Key, Dash(OsJson.Str(v, "antes")), Dash(OsJson.Str(v, "depois")));
                }
            }

            // Checklist das evidências (seções 14 a 16 da diretriz)
            bool anyAfter = Evidence.All().Any(e => Evidence.Has(folder, e.Codigo, true));
            p.Section("Checklist de evidências");
            foreach (var e in Evidence.All())
            {
                bool has = Evidence.Has(folder, e.Codigo, false);
                string text = has ? "OK" : e.Condicional ? "Não realizado (condicional)" : "FALTA";
                string color = has ? LaudoPages.Green : e.Condicional ? LaudoPages.Gray : LaudoPages.Red;
                if (anyAfter && !e.IsPhoto && !e.Condicional)
                    text += Evidence.Has(folder, e.Codigo, true) ? "   |   depois: OK" : "   |   depois: FALTA";
                p.Row((e.IsPhoto ? "Foto: " : "Print: ") + e.Nome, text, color);
            }

            string obs = OsJson.Str(data, "observacoes");
            if (!string.IsNullOrWhiteSpace(obs))
            {
                p.Section("Observações / laudo técnico");
                p.Paragraph(obs);
            }

            p.Section("Evidências anexadas (" + photos.Count + ")");
            for (int i = 0; i < photos.Count; i++)
                p.Row("Página " + (laudoPages + i + 1), Path.GetFileName(photos[i]));
            return p;
        }

        static string Dash(string s) { return string.IsNullOrWhiteSpace(s) ? "-" : s; }

        static string Answer(string v)
        {
            switch (v)
            {
                case "sim": return "Sim (funcionando)";
                case "nao": return "Não (com defeito)";
                case "nao_tem": return "Não tem";
                default: return "Não testado";
            }
        }
    }
}

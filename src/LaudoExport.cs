using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Diagnostico
{
    // Gera os JSON que o laudo web da Cantão Informática importa (botão "Importar" do index.html).
    //   <OS> - RELATORIO - <cliente>.json      preenche a coluna TÉCNICO + parecer   (fase Antes)
    //   <OS> - TESTES_FINAL - <cliente>.json   preenche a coluna TESTES + checklist  (fase Depois)
    // As chaves (campo_NN / check_NN) são os data-id dos campos da página.
    static class LaudoExport
    {
        // Linha do laudo: campo da coluna TÉCNICO, campo da coluna TESTES e checkbox do atendente.
        class Row
        {
            public string Tecnico, Testes, Check;
            public Row(string tecnico, string testes, string check) { Tecnico = tecnico; Testes = testes; Check = check; }
        }

        static readonly Dictionary<string, Row[]> Map = new Dictionary<string, Row[]>
        {
            { "teclado",       new[] { new Row("campo_102", "campo_104", "check_104") } },
            { "touchpad",      new[] { new Row("campo_108", "campo_109", "check_109") } },
            { "webcam",        new[] { new Row("campo_112", "campo_113", "check_113") } },
            { "camera",        new[] { new Row("campo_112", "campo_113", "check_113") } },
            { "microfone",     new[] { new Row("campo_116", "campo_117", "check_117") } },
            { "audio interno", new[] { new Row("campo_120", "campo_121", "check_121") } },
            { "audio",         new[] { new Row("campo_120", "campo_121", "check_121") } },
            { "conector p2",   new[] { new Row("campo_124", "campo_125", "check_125") } },
            { "p2",            new[] { new Row("campo_124", "campo_125", "check_125") } },
            { "audio / p2",    new[] { new Row("campo_120", "campo_121", "check_121"), new Row("campo_124", "campo_125", "check_125") } },
            { "hdmi",          new[] { new Row("campo_77h", "campo_78h", "check_78h") } },
            { "usb",           new[] { new Row("campo_81", "campo_82", "check_82") } },
            { "bluetooth",     new[] { new Row("campo_97", "campo_98", "check_98") } },
            { "wi-fi",         new[] { new Row("campo_93", "campo_94", "check_94") } },
            { "wifi",          new[] { new Row("campo_93", "campo_94", "check_94") } },
            { "lan",           new[] { new Row("campo_89", "campo_90", "check_90") } },
            { "bateria",       new[] { new Row("campo_9", "campo_11", "check_11") } },
            { "armazenamento", new[] { new Row("campo_138", "campo_141", "check_141") } },
        };

        static readonly string[] Status = { "Funcionando", "Com defeito", "Não Se Aplica" };
        const string NotTested = "Não Testado";

        public static string Normalize(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in (s ?? "").Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
            return Regex.Replace(sb.ToString().Trim(), @"\s+", " ");
        }

        static string StatusOf(Dictionary<string, int> answers, string test)
        {
            int v;
            return answers.TryGetValue(test, out v) && v >= 0 && v < Status.Length ? Status[v] : NotTested;
        }

        static int Answer(Dictionary<string, int> answers, string normalizedName)
        {
            foreach (var kv in answers) if (Normalize(kv.Key) == normalizedName) return kv.Value;
            return -1;
        }

        static string SafeName(string s, string fallback)
        {
            s = Regex.Replace((s ?? "").Trim(), "[\\\\/:*?\"<>|]", "_");
            return s == "" ? fallback : s;
        }

        static string Html(string s) { return System.Net.WebUtility.HtmlEncode(s ?? ""); }

        static string CpuOption(string cpu)
        {
            string c = (cpu ?? "").ToLowerInvariant();
            if (c.Contains("celeron")) return "Intel Celeron";
            if (c.Contains("pentium")) return "Intel Pentium";
            var m = Regex.Match(c, @"\bi([3579])\b");
            if (m.Success) return "Core i" + m.Groups[1].Value;
            m = Regex.Match(c, @"ryzen\D{0,3}([3579])\b");
            if (m.Success) return "AMD Ryzen " + m.Groups[1].Value;
            return null;
        }

        static string RamOption(string ram)
        {
            var m = Regex.Match(ram ?? "", @"(\d+)");
            string v = m.Success ? m.Groups[1].Value + "GB" : "";
            return new[] { "2GB", "4GB", "6GB", "8GB", "10GB", "12GB", "16GB", "32GB", "64GB" }.Contains(v) ? v : null;
        }

        // Menu RESOLUÇÃO do laudo: HD / FULL HD / 2K (QHD) / 4K (UHD)
        static string ResolutionOption(int w, int h)
        {
            if (w >= 3840) return "4K (UHD)";
            if (w >= 2560) return "2K (QHD)";
            if (w >= 1920) return "FULL HD";
            if (w >= 1280) return "HD";
            return null;
        }

        // Menu TAXA DE ATUALIZAÇÃO: só preenche quando o valor existe no menu (não arredonda 90 Hz para 75, por exemplo).
        static string HzOption(int hz)
        {
            return new[] { 60, 75, 120, 144, 165, 240 }.Contains(hz) ? hz + "Hz" : null;
        }

        static string DiskType(DiskHealth d)
        {
            if ((d.Interface ?? "").IndexOf("NVM", StringComparison.OrdinalIgnoreCase) >= 0) return "NVMe";
            if (d.Kind == "SSD") return "SSD";
            if (d.Kind == "HD") return "HD";
            return null;
        }

        // Cabeçalho comum + armazenamento/bateria. techColumn: true = coluna TÉCNICO, false = coluna TESTES.
        static Dictionary<string, object> Build(string os, string cliente, string tecnico, MachineInfo m,
                                                Dictionary<string, int> answers, IEnumerable<string> tests, bool techColumn)
        {
            var j = new Dictionary<string, object>();
            j["campo_1"] = cliente;
            j["campo_2"] = (m.Fabricante + " " + m.Modelo).Trim();
            j["campo_os"] = os;
            j[techColumn ? "campo_tecnico_tecnico" : "campo_tecnico_testes"] = tecnico;

            foreach (var test in tests)
            {
                Row[] rows;
                if (!Map.TryGetValue(Normalize(test), out rows)) continue;
                string st = StatusOf(answers, test);
                foreach (var r in rows)
                {
                    j[techColumn ? r.Tecnico : r.Testes] = st;
                    if (!techColumn && st == "Funcionando") j[r.Check] = true;
                }
            }

            // Sistema operacional: o Windows do cliente iniciou e rodou o diagnóstico (no WinPE isso não diz nada).
            if (!SystemInfo.IsWinPE)
            {
                j[techColumn ? "campo_128" : "campo_129"] = "Funcionando";
                if (!techColumn) j["check_129"] = true;
            }

            if (techColumn)
            {
                string cpu = CpuOption(m.Processador), ram = RamOption(m.Memoria);
                if (cpu != null) j["campo_34"] = cpu;
                if (ram != null) j["campo_43"] = ram;
            }

            if (m.BateriaPercent >= 0) j[techColumn ? "campo_10" : "campo_13"] = m.BateriaPercent.ToString();

            // Tela (linhas que só existem no modo notebook do laudo)
            if ((m.Tipo ?? "").StartsWith("notebook", StringComparison.OrdinalIgnoreCase))
            {
                string res = ResolutionOption(m.TelaLargura, m.TelaAltura), hz = HzOption(m.TelaHz);
                if (res != null) j[techColumn ? "campo_56" : "campo_57"] = res;
                if (hz != null) j[techColumn ? "campo_60" : "campo_61"] = hz;
            }

            // Discos: o 1º usa os campos fixos; os demais, as linhas extras "SSD/NVMe/HD (2)", "(3)"...
            int arm = Answer(answers, "armazenamento");
            string stArm = arm >= 0 && arm < Status.Length ? Status[arm] : NotTested;
            for (int i = 0; i < m.Discos.Count; i++)
            {
                var d = m.Discos[i];
                string kSt, kTp, kSa, kCk;
                if (i == 0)
                {
                    kSt = techColumn ? "campo_138" : "campo_141"; kTp = techColumn ? "campo_139" : "campo_143";
                    kSa = techColumn ? "campo_140" : "campo_144"; kCk = "check_141";
                }
                else
                {
                    int n = i + 1, b = techColumn ? 4 : 7;
                    kSt = "campo_arm" + n + "_" + b; kTp = "campo_arm" + n + "_" + (b + 1); kSa = "campo_arm" + n + "_" + (b + 2); kCk = "check_arm" + n;
                }
                j[kSt] = stArm;
                string tp = DiskType(d);
                if (tp != null) j[kTp] = tp;
                if (d.Percent >= 0) j[kSa] = d.Percent.ToString();
                if (!techColumn && stArm == "Funcionando") j[kCk] = true;
            }
            if (m.Discos.Count > 1) j["_armazenamentoCount"] = m.Discos.Count - 1;

            j["_modo"] = (m.Tipo ?? "").StartsWith("notebook", StringComparison.OrdinalIgnoreCase) ? "notebook" : "pc";
            return j;
        }

        // Texto do parecer técnico (HTML simples, como o campo do laudo aceita).
        static string Parecer(MachineInfo m, Dictionary<string, int> answers, IEnumerable<string> tests, string obs)
        {
            var itens = new List<string>();
            var naoTestado = new List<string>();
            bool problema = false;
            foreach (var test in tests)
            {
                string n = Normalize(test);
                if (n == "bateria" || n == "armazenamento") continue;
                int v;
                if (!answers.TryGetValue(test, out v)) { naoTestado.Add(test.ToLowerInvariant()); continue; }
                if (v == 1) { problema = true; itens.Add("<li><b>" + Html(test) + ":</b> não funcionou corretamente nos testes realizados.</li>"); }
            }

            int bat = Answer(answers, "bateria");
            if (m.BateriaPercent >= 0)
            {
                if (bat == 1) { problema = true; itens.Add("<li><b>Bateria:</b> com " + m.BateriaPercent + "% da capacidade original, recomenda-se a troca.</li>"); }
                else if (bat == 0) itens.Add("<li><b>Bateria:</b> em bom estado, com " + m.BateriaPercent + "% da capacidade original.</li>");
            }
            else if (bat == 1) { problema = true; itens.Add("<li><b>Bateria:</b> apresenta defeito.</li>"); }

            int arm = Answer(answers, "armazenamento");
            foreach (var d in m.Discos)
            {
                string tp = DiskType(d);
                string linha = "<li><b>Armazenamento</b> " + Html(d.Model) + (tp != null ? " (" + tp + ")" : "") + ": estado " + Html(d.Status == "" ? "desconhecido" : d.Status.ToLowerInvariant());
                if (d.Percent >= 0) linha += ", saúde de " + d.Percent + "%";
                linha += ".";
                if (arm == 1) { problema = true; linha += " Recomenda-se a substituição e o backup dos dados."; }
                itens.Add(linha + "</li>");
            }

            if (naoTestado.Count > 0) itens.Add("<li><b>Não testado:</b> " + Html(string.Join(", ", naoTestado)) + ".</li>");
            if (!problema) itens.Add("<li>Nenhum defeito encontrado nos itens testados.</li>");

            string html = "<p>Após análise técnica em bancada, seguem os resultados:</p><ul>" + string.Join("", itens) + "</ul>";
            if (!string.IsNullOrWhiteSpace(obs))
                html += "<p>" + Html(obs.Trim()).Replace("\r", "").Replace("\n", "<br>") + "</p>";
            return html;
        }

        // Grava os arquivos em Documentos\ e devolve os nomes gerados.
        public static List<string> Write(string folder, string cliente, string tecnico, MachineInfo m, IList<string> tests,
                                         Dictionary<string, int> antes, Dictionary<string, int> depois, bool writeFinal, string obs)
        {
            m = m ?? new MachineInfo();
            string os = Evidence.OsOf(folder);
            string dir = Path.Combine(folder, PhotoStore.DocDir);
            Directory.CreateDirectory(dir);
            var files = new List<string>();
            var utf8 = new UTF8Encoding(false);

            var rel = Build(os, cliente, tecnico, m, antes, tests, true);
            rel["PARECERTECNICOField"] = Parecer(m, antes, tests, obs);
            rel["defeitosObs"] = new Dictionary<string, object>();
            string name = SafeName(os, "OS") + " - RELATORIO - " + SafeName(cliente, "Cliente") + ".json";
            File.WriteAllText(Path.Combine(dir, name), Json.Pretty(Json.Serializer.Serialize(rel)), utf8);
            files.Add(name);

            if (writeFinal)
            {
                var fin = Build(os, cliente, tecnico, m, depois, tests, false);
                name = SafeName(os, "OS") + " - TESTES_FINAL - " + SafeName(cliente, "Cliente") + ".json";
                File.WriteAllText(Path.Combine(dir, name), Json.Pretty(Json.Serializer.Serialize(fin)), utf8);
                files.Add(name);
            }
            return files;
        }
    }
}

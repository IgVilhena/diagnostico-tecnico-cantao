using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Diagnostico
{
    // Uma evidência exigida pela diretriz: foto física ou print de um programa.
    public class EvidenceItem
    {
        public string Codigo, Nome, Descricao;
        public bool IsPhoto, Condicional;
        public ToolConfig Tool;
        public FotoConfig Foto;
    }

    // Nomenclatura padronizada (seção 14 da diretriz):
    //   OS_<numero>_01_TampaSuperior.jpg            foto física
    //   OS_<numero>_PRINT_03_CrystalDiskInfo.png    print de tela
    //   ..._Depois                                  repetição após a manutenção
    //   ..._2, _3                                   evidência refeita
    static class Evidence
    {
        public const string AfterSuffix = "_Depois";

        public static string OsOf(string folder) { return PhotoStore.Sanitize(Path.GetFileName((folder ?? "").TrimEnd('\\'))); }

        public static string BaseName(string folder, string codigo, bool depois)
        {
            return "OS_" + OsOf(folder) + "_" + codigo + (depois ? AfterSuffix : "");
        }

        // Caminho livre para uma nova evidência (não sobrescreve: acrescenta _2, _3...).
        public static string NewPath(string folder, string codigo, bool depois, string ext)
        {
            string b = BaseName(folder, codigo, depois);
            string name = b;
            for (int n = 2; Directory.Exists(folder) && Directory.GetFiles(folder, name + ".*").Length > 0; n++) name = b + "_" + n;
            return Path.Combine(folder, name + ext);
        }

        public static List<string> FilesOf(string folder, string codigo, bool depois)
        {
            var rx = new Regex("^" + Regex.Escape(BaseName(folder, codigo, depois)) + @"(_\d+)?$", RegexOptions.IgnoreCase);
            return PhotoStore.Photos(folder).Where(f => rx.IsMatch(Path.GetFileNameWithoutExtension(f))).ToList();
        }

        public static bool Has(string folder, string codigo, bool depois)
        {
            return !string.IsNullOrEmpty(folder) && FilesOf(folder, codigo, depois).Count > 0;
        }

        // Evidências da análise atual (respeita a escolha de testes offline/online).
        public static List<EvidenceItem> All() { return Items(false); }

        // includeHidden: também as etapas escondidas pela escolha offline/online (para dar nome a arquivos já salvos).
        static List<EvidenceItem> Items(bool includeHidden)
        {
            var list = new List<EvidenceItem>();
            foreach (var f in Session.Cfg.Fotos)
                list.Add(new EvidenceItem { Codigo = f.Codigo, Nome = f.Nome, Descricao = f.Descricao, IsPhoto = true, Foto = f });
            foreach (var t in Session.Cfg.Programas)
            {
                if (!includeHidden && !Session.Allowed(t)) continue;
                list.Add(new EvidenceItem { Codigo = t.Codigo, Nome = t.Nome, Descricao = t.Descricao, Condicional = Session.IsConditional(t), Tool = t });
            }
            return list;
        }

        // Obrigatórias: todas as fotos e prints na fase Antes; na fase Depois, os prints (as fotos são opcionais).
        public static bool Required(EvidenceItem e, bool depois)
        {
            if (e.Condicional) return false;
            return !depois || !e.IsPhoto;
        }

        public static List<EvidenceItem> Missing(string folder, bool depois)
        {
            return All().Where(e => Required(e, depois) && !Has(folder, e.Codigo, depois)).ToList();
        }

        // Pendências só do modo de análise escolhido (Rápido, Periféricos...): os prints das etapas desse modo
        // e, nos modos Completo e Todos, também as fotos físicas.
        public static List<EvidenceItem> Missing(string folder, bool depois, string mode)
        {
            var tools = new HashSet<ToolConfig>(Session.ToolsForMode(mode));
            bool photos = mode != "Rápido" && mode != "Estresse" && mode != "Periféricos";
            return Missing(folder, depois).Where(e => e.IsPhoto ? photos : tools.Contains(e.Tool)).ToList();
        }

        // Nome amigável para títulos (PDF, miniaturas).
        public static string DisplayName(string path)
        {
            string n = Path.GetFileNameWithoutExtension(path);
            var m = Regex.Match(n, @"^OS_[^_]+_(.+?)(_Depois)?(_\d+)?$", RegexOptions.IgnoreCase);
            if (!m.Success) return Regex.Replace(n, @"^\d+_", "");
            string code = m.Groups[1].Value;
            string suffix = m.Groups[2].Success ? " (depois)" : "";
            var e = Items(true).FirstOrDefault(x => string.Equals(x.Codigo, code, StringComparison.OrdinalIgnoreCase));
            string name = e != null ? e.Nome : code.Replace('_', ' ');
            return name + suffix;
        }

        // Ordem do relatório: fotos físicas, prints da diretriz, demais arquivos.
        public static int SortGroup(string path)
        {
            string n = Path.GetFileNameWithoutExtension(path);
            if (Regex.IsMatch(n, @"^OS_[^_]+_\d", RegexOptions.IgnoreCase)) return 0;
            if (Regex.IsMatch(n, @"^OS_[^_]+_PRINT_", RegexOptions.IgnoreCase)) return 1;
            return 2;
        }
    }

    // Leitura/gravação de Documentos\OS_<numero>.json preservando campos de outras telas.
    static class OsJson
    {
        public static string PathOf(string folder)
        {
            return Path.Combine(Path.Combine(folder, PhotoStore.DocDir), "OS_" + Evidence.OsOf(folder) + ".json");
        }

        public static Dictionary<string, object> Load(string folder)
        {
            try
            {
                string p = PathOf(folder);
                if (File.Exists(p))
                {
                    var d = Json.Serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(p, Encoding.UTF8));
                    if (d != null) return d;
                }
            }
            catch (Exception ex) { Log.Write("Erro lendo JSON da OS: " + ex.Message); }
            return new Dictionary<string, object>();
        }

        public static void Save(string folder, Dictionary<string, object> d)
        {
            Directory.CreateDirectory(Path.Combine(folder, PhotoStore.DocDir));
            File.WriteAllText(PathOf(folder), Json.Pretty(Json.Serializer.Serialize(d)), new UTF8Encoding(false));
        }

        public static Dictionary<string, object> Section(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        public static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? v.ToString() : "";
        }
    }
}

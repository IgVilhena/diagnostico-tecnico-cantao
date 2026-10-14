using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.Serialization;
using System.Windows.Forms;

namespace Diagnostico
{
    // Uma etapa da análise: programa (ou site) + nome padronizado da evidência.
    public class ToolConfig
    {
        public string Nome { get; set; }
        public string Exe { get; set; }            // .exe, ou endereço http(s) para testes online
        public string Argumentos { get; set; }     // para sites: outros endereços abertos juntos
        public string Processo { get; set; }       // nome do processo (sem .exe); vazio = nome do executável
        public string Arquivo { get; set; }        // código da evidência, ex.: PRINT_03_CrystalDiskInfo
        public string Modo { get; set; }           // manual | automatico
        public string Janela { get; set; }         // maximizada | normal | minimizada
        public int Espera { get; set; }            // segundos até o print no modo automático
        public int MinutosMinimos { get; set; }    // tempo mínimo rodando antes do print (ex.: FurMark 10 min)
        public bool MaximizarAntesPrint { get; set; }
        public bool ModoRapido { get; set; }
        public bool SoEstresse { get; set; }
        public bool SoPerifericos { get; set; }
        public bool Condicional { get; set; }      // só quando necessário (não conta como pendência)
        public string Descricao { get; set; }

        public ToolConfig()
        {
            Modo = "manual"; Janela = "maximizada"; Espera = 8;
            MaximizarAntesPrint = true; ModoRapido = false;
            Argumentos = ""; Processo = ""; Descricao = ""; Arquivo = "";
        }

        public static ToolConfig Make(string nome, string exe, string arquivo, string desc)
        {
            return new ToolConfig { Nome = nome, Exe = exe, Arquivo = arquivo, Descricao = desc };
        }

        [IgnoreDataMember]
        public string ProcessName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Processo)) return Processo.Trim().Replace(".exe", "");
                if (IsUrl || IsInternal) return "";
                return Path.GetFileNameWithoutExtension(Exe ?? "");
            }
        }

        [IgnoreDataMember] public bool IsUrl { get { return Exe != null && Regex.IsMatch(Exe.Trim(), "^https?://", RegexOptions.IgnoreCase); } }
        // Teste embutido no programa (offline): interno:teclado, interno:audio
        [IgnoreDataMember] public bool IsInternal { get { return Exe != null && Exe.Trim().StartsWith(InternalTests.Prefix, StringComparison.OrdinalIgnoreCase); } }
        // Teste interno que tem um site equivalente (hoje só o teclado; o de áudio testa os alto-falantes, que não têm versão online)
        [IgnoreDataMember] public bool HasOnlineVersion { get { return IsInternal && Exe.Trim().Substring(InternalTests.Prefix.Length).Equals("teclado", StringComparison.OrdinalIgnoreCase); } }
        [IgnoreDataMember] public string FullPath { get { return IsUrl || IsInternal ? Exe.Trim() : AppPaths.Resolve(Exe); } }
        [IgnoreDataMember] public bool Exists { get { try { return IsUrl || IsInternal || (!string.IsNullOrWhiteSpace(Exe) && File.Exists(FullPath)); } catch { return false; } } }

        // Código usado no nome do arquivo: OS_<numero>_<Codigo>.png
        [IgnoreDataMember]
        public string Codigo
        {
            get
            {
                string c = string.IsNullOrWhiteSpace(Arquivo) ? "PRINT_" + (Nome ?? "Print") : Arquivo;
                return PhotoStore.Sanitize(c).Replace(' ', '_');
            }
        }
    }

    // Foto física (câmera/celular) exigida pela diretriz.
    public class FotoConfig
    {
        public string Codigo { get; set; }         // ex.: 01_TampaSuperior
        public string Nome { get; set; }
        public string Descricao { get; set; }

        public FotoConfig() { }
        public FotoConfig(string codigo, string nome, string desc) { Codigo = codigo; Nome = nome; Descricao = desc; }
    }

    public class AppConfig
    {
        public const int VersaoAtual = 4;

        public int Versao { get; set; }
        public string PastaOS { get; set; }
        public string PastaProgramas { get; set; }
        public string ModoAnalise { get; set; }
        public string TipoTestes { get; set; }         // Offline | Online | Ambos: quais testes de periféricos entram nas etapas
        public string LayoutTeclado { get; set; }      // ABNT2 | Compacto: desenho usado no teste de teclado
        public string Tecnico { get; set; }           // último técnico usado (sugestão na próxima OS)
        public string Tema { get; set; }
        public string CorDestaque { get; set; }
        public bool MenuCompacto { get; set; }
        public bool MostrarDicas { get; set; }
        public bool PrintTelaInteira { get; set; }     // Regra de Ouro: todo print em tela inteira
        public List<string> Testes { get; set; }
        public List<string> Comparacao { get; set; }   // itens da validação pós-manutenção
        public List<FotoConfig> Fotos { get; set; }
        public List<ToolConfig> Programas { get; set; }

        public AppConfig()
        {
            Versao = VersaoAtual;
            PastaOS = @".\OS";
            PastaProgramas = @".\Ferramentas";
            ModoAnalise = "Completo";
            TipoTestes = "Ambos";
            LayoutTeclado = "ABNT2";
            Tecnico = "";
            Tema = "Claro";
            CorDestaque = "Turquesa";
            MostrarDicas = true;
            PrintTelaInteira = true;
            Testes = DefaultTests();
            Comparacao = DefaultComparison();
            Fotos = DefaultPhotos();
            Programas = DefaultTools();
        }

        public static List<string> DefaultTests()
        {
            // Mesmos itens das linhas do laudo web (cada um preenche um campo da página)
            return new List<string> { "Teclado", "Touchpad", "Webcam", "Microfone", "Áudio interno", "Conector P2", "HDMI", "USB",
                "Bluetooth", "Wi-Fi", "LAN", "Bateria", "Armazenamento" };
        }

        public static List<string> DefaultComparison()
        {
            return new List<string> { "Temperatura CPU", "Temperatura GPU", "Desempenho CPU", "Desempenho GPU", "Saúde SSD/HDD",
                "Bateria", "Periféricos", "Ruído/rotação", "Thermal Throttling", "Power Limit" };
        }

        public static List<FotoConfig> DefaultPhotos()
        {
            return new List<FotoConfig>
            {
                new FotoConfig("01_TampaSuperior", "Tampa superior", "Vista de cima com o notebook fechado"),
                new FotoConfig("02_BaseInferior", "Base inferior e identificação", "Parafusos, saídas de ar, pés e etiqueta de série"),
                new FotoConfig("03_Tela", "Tela e moldura", "Notebook aberto: riscos, manchas, moldura e webcam"),
                new FotoConfig("04_Teclado", "Teclado e touchpad", "Teclas, touchpad, desgaste e etiquetas"),
                new FotoConfig("05_Interno_Geral", "Placa-mãe / estado inicial", "Antes de qualquer limpeza ou intervenção"),
                new FotoConfig("06_Componentes_RAM_SSD", "Componentes removíveis", "RAM, SSD/HD, Wi-Fi: marcas e modelos"),
                new FotoConfig("07_Placa_Sem_Dissipador", "Sistema térmico removido", "Pasta térmica, pads e sinais de superaquecimento"),
            };
        }

        public static List<ToolConfig> DefaultTools()
        {
            var hw = @".\Ferramentas\HWiNFO\HWiNFO64.exe";
            var list = new List<ToolConfig>
            {
                ToolConfig.Make("HWiNFO - Hardware", hw, "PRINT_01_HWiNFO_Hardware", "CPU, RAM, armazenamento, GPU, placa-mãe e BIOS"),
                ToolConfig.Make("Teclado (online)", "https://www.testarteclado.com.br", "PRINT_02_Teclado_Online", "Testar 100% das teclas"),
                ToolConfig.Make("Webcam e Microfone (online)", "https://pt.webcamtests.com", "PRINT_02B_Webcam_Mic_Online", "Imagem da webcam e captação do microfone"),
                ToolConfig.Make("HDMI / Monitor externo", @"%WINDIR%\System32\DisplaySwitch.exe", "PRINT_02C_HDMI_Tela_Estendida", "Win+P > Estender estas telas e comprovar o sinal"),
                ToolConfig.Make("CrystalDiskInfo", @".\Ferramentas\CrystalDiskInfo\DiskInfo64.exe", "PRINT_03_CrystalDiskInfo", "S.M.A.R.T., desgaste, horas de uso, erros e temperatura"),
                ToolConfig.Make("HDDScan", @".\Ferramentas\HDDScan\HDDScan.exe", "PRINT_04_HDDScan_Superficie", "Mapa de blocos, tempos de resposta e setores lentos"),
                ToolConfig.Make("H2testw", @".\Ferramentas\H2testw\h2testw.exe", "PRINT_04B_H2testw_Escrita", "Condicional: integridade, capacidade e velocidade de escrita"),
                ToolConfig.Make("BatteryInfoView", @".\Ferramentas\BatteryInfoView\BatteryInfoView.exe", "PRINT_05_BatteryInfoView", "Capacidade, Wear Level, ciclos, descarga e voltagem"),
                ToolConfig.Make("HWiNFO - Sensores", hw, "PRINT_06_HWiNFO_Sensores", "Temperaturas máximas, hotspot, throttling e power limit"),
                ToolConfig.Make("FurMark", @".\Ferramentas\FurMark\FurMark.exe", "PRINT_07_FurMark_Estresse", "Mínimo 10 minutos: temperatura, estabilidade e limitações"),
                ToolConfig.Make("Unigine Superposition", @".\Ferramentas\Superposition\Superposition.exe", "PRINT_08_Superposition_Benchmark", "Pontuação, FPS médio/mín./máx. e temperatura máxima"),
                ToolConfig.Make("Cinebench", @".\Ferramentas\Cinebench\Cinebench.exe", "PRINT_09_Cinebench_CPU", "Single/Multi-Core, frequência, temperatura e limitações"),
            };
            foreach (var t in list)
            {
                if (t.Arquivo.StartsWith("PRINT_01") || t.Arquivo.StartsWith("PRINT_03") || t.Arquivo.StartsWith("PRINT_05") || t.Arquivo.StartsWith("PRINT_06"))
                    t.ModoRapido = true;
                if (t.Arquivo.StartsWith("PRINT_02")) t.SoPerifericos = true;
                if (t.Arquivo.StartsWith("PRINT_07") || t.Arquivo.StartsWith("PRINT_08") || t.Arquivo.StartsWith("PRINT_09")) t.SoEstresse = true;
            }
            list[2].Argumentos = "https://pt.mictests.com";
            list[3].Argumentos = "/extend";
            list[6].Condicional = true;
            list[9].MinutosMinimos = 10;
            AddInternalTests(list);
            return list;
        }

        // Testes offline embutidos (teclado e áudio). O teclado online vira alternativa (condicional).
        static void AddInternalTests(List<ToolConfig> list)
        {
            Func<string, bool> has = exe => list.Any(t => string.Equals((t.Exe ?? "").Trim(), exe, StringComparison.OrdinalIgnoreCase));
            if (!has("interno:teclado"))
            {
                var t = ToolConfig.Make("Teclado (offline)", "interno:teclado", "PRINT_02_Teclado_Offline", "Teste interno, sem internet: pressione 100% das teclas");
                t.SoPerifericos = true;
                int i = list.FindIndex(x => x.Arquivo == "PRINT_02_Teclado_Online");
                if (i >= 0) { list[i].Condicional = true; list[i].Descricao = "Alternativa online ao teste interno"; list.Insert(i, t); }
                else list.Insert(Math.Min(1, list.Count), t);
            }
            if (!has("interno:audio"))
            {
                var t = ToolConfig.Make("Áudio e Microfone (offline)", "interno:audio", "PRINT_02D_Audio_Mic_Offline", "Teste interno: caixas esquerda/direita e gravação do microfone");
                t.SoPerifericos = true;
                int i = list.FindIndex(x => x.Arquivo == "PRINT_02B_Webcam_Mic_Online");
                list.Insert(i >= 0 ? i + 1 : list.Count, t);
            }
        }

        // Converte um config.json da versão 1 para o padrão da diretriz, mantendo os caminhos já configurados.
        void Migrate()
        {
            var old = Programas ?? new List<ToolConfig>();
            var droppedDefaults = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "CrystalDiskInfo", "BatteryInfoView", "HWiNFO", "CPU-Z", "GPU-Z", "HWMonitor", "HDDScan", "HD Sentinel", "AIDA64", "FurMark", "Keyboard Test" };
            var fresh = DefaultTools();
            foreach (var t in fresh)
            {
                string file = Path.GetFileName(t.Exe ?? "");
                var match = old.FirstOrDefault(o => o.Exists && string.Equals(Path.GetFileName(o.Exe ?? ""), file, StringComparison.OrdinalIgnoreCase));
                if (match != null) t.Exe = match.Exe;
            }
            fresh.AddRange(old.Where(o => !droppedDefaults.Contains(o.Nome ?? "")));
            Programas = fresh;
            Testes = DefaultTests();
            if (Comparacao == null || Comparacao.Count == 0) Comparacao = DefaultComparison();
            if (Fotos == null || Fotos.Count == 0) Fotos = DefaultPhotos();
            PrintTelaInteira = true;
            Versao = VersaoAtual;
        }

        [IgnoreDataMember] public string OsRoot { get { return AppPaths.Resolve(PastaOS); } }
        [IgnoreDataMember] public string ToolsRoot { get { return AppPaths.Resolve(PastaProgramas); } }

        public static string FilePath { get { return Path.Combine(AppPaths.AppDir, "config.json"); } }

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var cfg = Json.Serializer.Deserialize<AppConfig>(File.ReadAllText(FilePath, Encoding.UTF8));
                    if (cfg != null)
                    {
                        if (cfg.Programas == null) cfg.Programas = new List<ToolConfig>();
                        // Um config antigo não tem "Versao" no arquivo (o construtor já preenche o valor atual),
                        // então o sinal confiável de formato antigo é nenhuma etapa ter nome de evidência.
                        bool antigo = cfg.Versao < 2 || !cfg.Programas.Any(p => !string.IsNullOrWhiteSpace(p.Arquivo));
                        if (antigo) { cfg.Migrate(); cfg.Versao = 2; }
                        bool mudou = cfg.Versao < VersaoAtual;
                        if (cfg.Versao < 3) cfg.Testes = DefaultTests();
                        if (cfg.Versao < 4) AddInternalTests(cfg.Programas);
                        if (mudou) { cfg.Versao = VersaoAtual; cfg.Save(); }
                        if (cfg.Testes == null || cfg.Testes.Count == 0) cfg.Testes = DefaultTests();
                        if (cfg.Comparacao == null || cfg.Comparacao.Count == 0) cfg.Comparacao = DefaultComparison();
                        if (cfg.Fotos == null) cfg.Fotos = DefaultPhotos();
                        return cfg;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Não foi possível ler o config.json, usando o padrão.\n\n" + ex.Message,
                    "Diagnóstico Técnico", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            var def = new AppConfig();
            def.Save();
            return def;
        }

        public void Save()
        {
            TrySave();
        }

        public bool TrySave()
        {
            try { File.WriteAllText(FilePath, Json.Pretty(Json.Serializer.Serialize(this)), new UTF8Encoding(false)); return true; }
            catch { return false; /* pendrive protegido contra gravação: segue sem salvar */ }
        }
    }

    static class AppPaths
    {
        public static readonly string AppDir = Path.GetDirectoryName(Application.ExecutablePath);

        // Caminhos relativos (.\pasta) são resolvidos a partir da pasta do .exe, então a letra do pendrive não importa.
        public static string Resolve(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return AppDir;
            p = Environment.ExpandEnvironmentVariables(p.Trim());
            // \\servidor\pasta (rede) é caminho completo; só "\pasta" é relativo à pasta do programa
            if (p.StartsWith(@"\\") || (Path.IsPathRooted(p) && !p.StartsWith(@"\"))) return Path.GetFullPath(p);
            return Path.GetFullPath(Path.Combine(AppDir, p.TrimStart('\\')));
        }

        public static string ToRelative(string full)
        {
            if (string.IsNullOrEmpty(full)) return full;
            string root = AppDir.TrimEnd('\\') + "\\";
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return ".\\" + full.Substring(root.Length);
            if (string.Equals(full.TrimEnd('\\'), AppDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return ".";
            return full;
        }
    }

    static class Json
    {
        public static readonly PortableJsonSerializer Serializer = new PortableJsonSerializer();

        // Indenta o JSON gerado pelo serializador e desfaz escapes desnecessários (<, >, &, ').
        public static string Pretty(string json)
        {
            json = json.Replace("\\u003c", "<").Replace("\\u003e", ">").Replace("\\u0026", "&").Replace("\\u0027", "'");
            var sb = new StringBuilder();
            int indent = 0;
            bool inStr = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inStr)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < json.Length) { sb.Append(json[++i]); continue; }
                    if (c == '"') inStr = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inStr = true; sb.Append(c); break;
                    case '{':
                    case '[':
                        sb.Append(c);
                        if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']')) { sb.Append(json[++i]); break; }
                        indent++; sb.Append("\r\n").Append(' ', indent * 2); break;
                    case '}':
                    case ']':
                        indent--; sb.Append("\r\n").Append(' ', indent * 2).Append(c); break;
                    case ',': sb.Append(",\r\n").Append(' ', indent * 2); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}

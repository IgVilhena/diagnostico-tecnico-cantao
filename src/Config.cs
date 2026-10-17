using System;
using System.Collections.Generic;
using System.Globalization;
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
        public bool Desativado { get; set; }       // etapa desativada em Configurações (testes internos só podem ser desativados)
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

        [IgnoreDataMember] public bool IsUrl { get { return LooksLikeUrl(Exe); } }
        // Teste embutido no programa (offline): interno:teclado, interno:audio
        [IgnoreDataMember] public bool IsInternal { get { return Exe != null && Exe.Trim().StartsWith(InternalTests.Prefix, StringComparison.OrdinalIgnoreCase); } }
        // Teste interno que tem um site equivalente (hoje só o teclado; o de áudio testa os alto-falantes, que não têm versão online)
        [IgnoreDataMember] public bool HasOnlineVersion { get { return IsInternal && Exe.Trim().Substring(InternalTests.Prefix.Length).Equals("teclado", StringComparison.OrdinalIgnoreCase); } }
        [IgnoreDataMember] public string FullPath { get { return IsInternal ? Exe.Trim() : IsUrl ? NormalizeUrl(Exe) : AppPaths.Resolve(Exe); } }
        [IgnoreDataMember]
        public bool Exists
        {
            get
            {
                try
                {
                    if (IsInternal) return true;
                    if (IsUrl) return HasValidUrl(Exe) || ExtraUrls().Count > 0;
                    return !string.IsNullOrWhiteSpace(Exe) && File.Exists(FullPath);
                }
                catch { return false; }
            }
        }

        public static bool LooksLikeUrl(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = Regex.Replace(s.Trim(), @"^(https?://)+", "https://", RegexOptions.IgnoreCase);
            if (Regex.IsMatch(s, "^https?://", RegexOptions.IgnoreCase)) return true;
            if (s.IndexOf('\\') >= 0 || s.StartsWith(".") || s.StartsWith("%") || Regex.IsMatch(s, @"^[a-zA-Z]:")) return false;
            if (Regex.IsMatch(s, @"\.(exe|bat|cmd|msc|cpl)$", RegexOptions.IgnoreCase)) return false;
            return Regex.IsMatch(s, @"^(www\.)?[a-z0-9-]+(\.[a-z0-9-]+)+(/.*)?$", RegexOptions.IgnoreCase);
        }

        public static bool HasValidUrl(string s)
        {
            if (!LooksLikeUrl(s)) return false;
            string n = Regex.Replace(s.Trim(), @"^(https?://)+", "", RegexOptions.IgnoreCase).Trim('/');
            return n.Length > 0 && n.IndexOf('.') > 0;
        }

        public static string NormalizeUrl(string s)
        {
            s = (s ?? "").Trim();
            if (s == "") return "";
            s = Regex.Replace(s, @"^(https?://)+", "https://", RegexOptions.IgnoreCase);
            if (Regex.IsMatch(s, "^https?://$", RegexOptions.IgnoreCase)) return s;
            return Regex.IsMatch(s, "^https?://", RegexOptions.IgnoreCase) ? s : "https://" + s;
        }

        public List<string> ExtraUrls()
        {
            if (!IsUrl && string.IsNullOrWhiteSpace(Argumentos)) return new List<string>();
            return (Argumentos ?? "")
                .Split(new[] { ' ', ';', ',', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(LooksLikeUrl)
                .ToList();
        }

        public List<string> AllUrls()
        {
            var list = new List<string>();
            if (HasValidUrl(Exe)) list.Add(NormalizeUrl(Exe));
            foreach (var u in ExtraUrls())
                if (HasValidUrl(u)) list.Add(NormalizeUrl(u));
            return list;
        }

        public static string NameFromUrl(string url)
        {
            if (!HasValidUrl(url)) return "";
            string host = Regex.Replace(url.Trim(), "^https?://", "", RegexOptions.IgnoreCase);
            int slash = host.IndexOfAny(new[] { '/', '?', '#', ':' });
            if (slash >= 0) host = host.Substring(0, slash);
            host = Regex.Replace(host, @"^(www|pt|br|en|es|app|web|m)\.", "", RegexOptions.IgnoreCase);
            host = Regex.Replace(host, @"\.(com\.br|net\.br|org\.br|gov\.br|edu\.br|co\.uk|com|net|org|io|dev|app|me|co|br|info|online|site|tech|test|tools)$", "", RegexOptions.IgnoreCase);
            if (host.IndexOf('.') >= 0) host = host.Split('.')[0];
            host = Regex.Replace(host.Replace('-', ' ').Replace('_', ' '), @"\s+", " ").Trim();
            if (host == "") return "";
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(host.ToLowerInvariant());
        }

        public static string AutoName(string exeOrUrl, IEnumerable<string> extraUrls = null)
        {
            var extras = extraUrls == null ? new List<string>() : extraUrls.Where(HasValidUrl).ToList();
            if (LooksLikeUrl(exeOrUrl) || extras.Count > 0)
            {
                var names = new List<string>();
                if (HasValidUrl(exeOrUrl))
                {
                    string n = NameFromUrl(exeOrUrl);
                    if (n != "") names.Add(n);
                }
                foreach (var u in extras)
                {
                    string n = NameFromUrl(u);
                    if (n != "" && !names.Contains(n, StringComparer.OrdinalIgnoreCase)) names.Add(n);
                }
                return names.Count == 0 ? "" : string.Join(" + ", names.Take(3));
            }
            if (string.IsNullOrWhiteSpace(exeOrUrl) || exeOrUrl.Trim().StartsWith(InternalTests.Prefix, StringComparison.OrdinalIgnoreCase))
                return "";
            var preset = ToolDiscovery.MatchPreset(exeOrUrl);
            if (preset != null && !string.IsNullOrWhiteSpace(preset.Nome)) return preset.Nome;
            string raw = Path.GetFileNameWithoutExtension(ToolDiscovery.FileNameOf(exeOrUrl));
            if (string.IsNullOrWhiteSpace(raw)) return "";
            raw = Regex.Replace(raw, @"([_-](x64|x86|64|32|gui|win64|win32))+$", "", RegexOptions.IgnoreCase);
            if (raw.Length > 4) raw = Regex.Replace(raw, @"(?<=[a-zA-Z])(64|32)$", "");
            raw = Regex.Replace(raw.Replace('_', ' '), @"\s+", " ").Trim();
            if (raw.Length > 0 && raw == raw.ToLowerInvariant())
                raw = char.ToUpper(raw[0]) + raw.Substring(1);
            return raw;
        }

        public static string AutoFileCode(string nome)
        {
            string clean = (nome ?? "").Trim();
            if (clean == "" || clean == "Novo programa" || clean == "Novo site") return "";
            clean = Regex.Replace(PhotoStore.Sanitize(clean), @"[\s\+]+", "_").Trim('_');
            if (clean == "") return "";
            return clean.StartsWith("PRINT_", StringComparison.OrdinalIgnoreCase) ? clean : "PRINT_" + clean;
        }

        // Código usado no nome do arquivo: OS_<numero>_<Codigo>.png
        [IgnoreDataMember]
        public string Codigo
        {
            get
            {
                string c = string.IsNullOrWhiteSpace(Arquivo) ? AutoFileCode(Nome) : Arquivo;
                if (string.IsNullOrWhiteSpace(c)) c = "PRINT_Print";
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
        public const int VersaoAtual = 5;

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
            CorDestaque = "Verde";
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

        // Apenas etapas prontas para uso (testes internos e online), sem poluir a lista com programas externos não instalados.
        public static List<ToolConfig> DefaultTools()
        {
            var list = new List<ToolConfig>
            {
                new ToolConfig { Nome = "Teclado (offline)", Exe = "interno:teclado", Arquivo = "PRINT_02_Teclado_Offline", Descricao = "Teste interno, sem internet: pressione 100% das teclas", SoPerifericos = true },
                new ToolConfig { Nome = "Teclado (online)", Exe = "https://www.testarteclado.com.br", Arquivo = "PRINT_02_Teclado_Online", Descricao = "Alternativa online ao teste interno", SoPerifericos = true, Condicional = true },
                new ToolConfig { Nome = "Webcam e Microfone (online)", Exe = "https://pt.webcamtests.com", Argumentos = "https://pt.mictests.com", Arquivo = "PRINT_02B_Webcam_Mic_Online", Descricao = "Imagem da webcam e captação do microfone", SoPerifericos = true },
                new ToolConfig { Nome = "Áudio e Microfone (offline)", Exe = "interno:audio", Arquivo = "PRINT_02D_Audio_Mic_Offline", Descricao = "Teste interno: caixas esquerda/direita e gravação do microfone", SoPerifericos = true },
            };
            return list;
        }

        // Pré-configurações prontas para quando o técnico adicionar ou localizar um programa de bancada.
        // CrystalDiskInfo, BatteryInfoView e Hard Disk Sentinel já vêm no modo automático.
        public static List<ToolConfig> PresetTools()
        {
            var hw = @".\Ferramentas\HWiNFO\HWiNFO64.exe";
            return new List<ToolConfig>
            {
                new ToolConfig { Nome = "CrystalDiskInfo", Exe = @".\Ferramentas\CrystalDiskInfo\DiskInfo64.exe", Arquivo = "PRINT_03_CrystalDiskInfo", Descricao = "S.M.A.R.T., desgaste, horas de uso, erros e temperatura", Modo = "automatico", Espera = 6, ModoRapido = true },
                new ToolConfig { Nome = "BatteryInfoView", Exe = @".\Ferramentas\BatteryInfoView\BatteryInfoView.exe", Arquivo = "PRINT_05_BatteryInfoView", Descricao = "Capacidade, Wear Level, ciclos, descarga e voltagem", Modo = "automatico", Espera = 5, ModoRapido = true },
                new ToolConfig { Nome = "Hard Disk Sentinel", Exe = @".\Ferramentas\HDSentinel\HDSentinel.exe", Arquivo = "PRINT_03B_HDSentinel", Descricao = "Saúde, temperatura, horas de uso e alertas S.M.A.R.T. do disco", Modo = "automatico", Espera = 6, ModoRapido = true },
                new ToolConfig { Nome = "HWiNFO - Hardware", Exe = hw, Arquivo = "PRINT_01_HWiNFO_Hardware", Descricao = "CPU, RAM, armazenamento, GPU, placa-mãe e BIOS", ModoRapido = true },
                new ToolConfig { Nome = "HWiNFO - Sensores", Exe = hw, Arquivo = "PRINT_06_HWiNFO_Sensores", Descricao = "Temperaturas máximas, hotspot, throttling e power limit", ModoRapido = true },
                new ToolConfig { Nome = "HDDScan", Exe = @".\Ferramentas\HDDScan\HDDScan.exe", Arquivo = "PRINT_04_HDDScan_Superficie", Descricao = "Mapa de blocos, tempos de resposta e setores lentos" },
                new ToolConfig { Nome = "H2testw", Exe = @".\Ferramentas\H2testw\h2testw.exe", Arquivo = "PRINT_04B_H2testw_Escrita", Descricao = "Condicional: integridade, capacidade e velocidade de escrita", Condicional = true },
                new ToolConfig { Nome = "FurMark", Exe = @".\Ferramentas\FurMark\FurMark.exe", Arquivo = "PRINT_07_FurMark_Estresse", Descricao = "Mínimo 10 minutos: temperatura, estabilidade e limitações", SoEstresse = true, MinutosMinimos = 10 },
                new ToolConfig { Nome = "Unigine Superposition", Exe = @".\Ferramentas\Superposition\Superposition.exe", Arquivo = "PRINT_08_Superposition_Benchmark", Descricao = "Pontuação, FPS médio/mín./máx. e temperatura máxima", SoEstresse = true },
                new ToolConfig { Nome = "Cinebench", Exe = @".\Ferramentas\Cinebench\Cinebench.exe", Descricao = "Single/Multi-Core, frequência, temperatura e limitações", Arquivo = "PRINT_09_Cinebench_CPU", SoEstresse = true },
                new ToolConfig { Nome = "HDMI / Monitor externo", Exe = @"%WINDIR%\System32\DisplaySwitch.exe", Argumentos = "/extend", Arquivo = "PRINT_02C_HDMI_Tela_Estendida", Descricao = "Win+P > Estender estas telas e comprovar o sinal", SoPerifericos = true },
            };
        }

        public static void CopyPresetTo(ToolConfig preset, ToolConfig target, string exePath = null)
        {
            target.Nome = preset.Nome;
            target.Exe = string.IsNullOrWhiteSpace(exePath) ? preset.Exe : exePath;
            target.Argumentos = preset.Argumentos;
            target.Processo = preset.Processo;
            target.Arquivo = preset.Arquivo;
            target.Modo = preset.Modo;
            target.Janela = preset.Janela;
            target.Espera = preset.Espera;
            target.MinutosMinimos = preset.MinutosMinimos;
            target.MaximizarAntesPrint = preset.MaximizarAntesPrint;
            target.ModoRapido = preset.ModoRapido;
            target.SoEstresse = preset.SoEstresse;
            target.SoPerifericos = preset.SoPerifericos;
            target.Condicional = preset.Condicional;
            target.Descricao = preset.Descricao;
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

        // Remove etapas externas padrão que nunca foram instaladas e aplica modo automático em CrystalDiskInfo/BatteryInfoView/Sentinel.
        static void CleanUninstalledPresets(List<ToolConfig> list)
        {
            var presetCodes = new HashSet<string>(PresetTools().Select(p => p.Arquivo), StringComparer.OrdinalIgnoreCase);
            list.RemoveAll(t => !t.IsInternal && !t.IsUrl && presetCodes.Contains(t.Arquivo ?? "") && !t.Exists);
            foreach (var t in list)
            {
                string n = (t.Nome ?? "").ToLowerInvariant();
                if (n.Contains("crystal") || n.Contains("batteryinfo") || n.Contains("sentinel"))
                {
                    t.Modo = "automatico";
                    if (t.Espera <= 0 || t.Espera == 8) t.Espera = n.Contains("batteryinfo") ? 5 : 6;
                }
            }
        }

        // Converte um config.json da versão 1 para o padrão da diretriz, mantendo os caminhos já configurados.
        void Migrate()
        {
            var old = Programas ?? new List<ToolConfig>();
            var droppedDefaults = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "CrystalDiskInfo", "BatteryInfoView", "HWiNFO", "CPU-Z", "GPU-Z", "HWMonitor", "HDDScan", "HD Sentinel", "AIDA64", "FurMark", "Keyboard Test" };
            var fresh = DefaultTools();
            foreach (var preset in PresetTools())
            {
                string file = Path.GetFileName((preset.Exe ?? "").Replace('\\', '/'));
                var match = old.FirstOrDefault(o => o.Exists && string.Equals(Path.GetFileName((o.Exe ?? "").Replace('\\', '/')), file, StringComparison.OrdinalIgnoreCase));
                if (match != null) { preset.Exe = match.Exe; fresh.Add(preset); }
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
                        if (cfg.Versao < 5) { AddInternalTests(cfg.Programas); CleanUninstalledPresets(cfg.Programas); }
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
            if (!Native.IsWindows) p = p.Replace('\\', '/');
            // \\servidor\pasta (rede) é caminho completo; só "\pasta" é relativo à pasta do programa
            if (p.StartsWith(@"\\") || (Path.IsPathRooted(p) && !p.StartsWith(@"\"))) return Path.GetFullPath(p);
            return Path.GetFullPath(Path.Combine(AppDir, p.TrimStart('\\', '/')));
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

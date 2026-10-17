using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Diagnostico;

public class UiSmoke
{
    static int checks;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
    static T Field<T>(object obj, string name) { return (T)obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(obj); }
    static void Key(Control control, Keys key)
    {
        control.GetType().GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { new KeyEventArgs(key) });
    }
    static double Luminance(Color color)
    {
        Func<byte, double> linear = value => { double v = value / 255.0; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); };
        return 0.2126 * linear(color.R) + 0.7152 * linear(color.G) + 0.0722 * linear(color.B);
    }
    static double Contrast(Color a, Color b)
    {
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }
    static void Snapshot(Control control, string name)
    {
        control.PerformLayout();
        using (var bitmap = new Bitmap(control.Width, control.Height))
        {
            control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(AppPaths.AppDir, name + ".png"));
        }
    }
    class HiddenMain : MainForm { protected override bool ShowWithoutActivation { get { return true; } } }
    [STAThread]
    public static int Main()
    {
        try
        {
            string isolatedRoot = Environment.GetEnvironmentVariable("DIAG_UI_TEST_DIR");
            if (!string.IsNullOrEmpty(isolatedRoot))
                typeof(AppPaths).GetField("AppDir").SetValue(null, isolatedRoot);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Theme.Scale = 1;
            Session.Cfg = Json.Serializer.Deserialize<AppConfig>("{\"Versao\":4,\"PastaOS\":\".\\\\test-orders\",\"Tecnico\":\"Técnico de teste\"}");
            Check(Session.Cfg.Tema == "Claro" && Session.Cfg.MostrarDicas, "Defaults for legacy config");
            Session.Cfg.Programas = AppConfig.DefaultTools();
            Directory.CreateDirectory(Session.Cfg.OsRoot);
            string order = Path.Combine(Session.Cfg.OsRoot, "12345");
            Directory.CreateDirectory(order);
            OsJson.Save(order, new System.Collections.Generic.Dictionary<string, object> { { "cliente", "Cliente de teste" } });
            Theme.Configure(Session.Cfg);
            typeof(AnalysisPage).GetField("machine", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null,
                new MachineInfo { Modelo = "Equipamento de teste", Processador = "Processador de exemplo", Memoria = "16 GB", Sistema = "Windows" });
            using (var form = new HiddenMain())
            {
                form.Opacity = 0; form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000);
                form.Size = new Size(1420, 880); form.Show(); Application.DoEvents();
                Snapshot(form, "home-claro");
                var pages = (Control[])typeof(MainForm).GetField("pages", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                var home = pages[0];
                var search = Field<DarkInput>(home, "search");
                search.Text = "cliente de TESTE";
                Check(Field<RowList>(home, "recent").Rows.Count == 1, "Search by client without case sensitivity");
                search.Text = "not-found";
                Check(Field<RowList>(home, "recent").Rows.Count == 0, "Empty search result");
                search.Text = "12345";
                Check(Field<RowList>(home, "recent").Rows.Count == 1, "Search by order number");
                search.Text = "";
                for (int i = 1; i < 5; i++) { form.Go(i); Application.DoEvents(); Snapshot(form, "page-" + i + "-claro"); }
                form.Go(MainForm.PageAnalysis); Application.DoEvents();
                var client = Field<DarkInput>(pages[1], "cliente");
                client.Text = "Edição não exportada";
                var tests = Field<System.Collections.Generic.List<Tuple<Label, Segmented>>>(pages[1], "tests");
                tests[0].Item2.SelectedIndex = 1;
                form.Go(MainForm.PageHome); // página de análise fica fora da árvore visível.
                Color[] pills = Theme.PillGreen;
                Session.Cfg.Tema = "Escuro"; form.ApplyAppearance();
                Check(Theme.IsDark && form.BackColor == Theme.Bg, "Live dark theme");
                Check(client.Text == "Edição não exportada" && tests[0].Item2.SelectedIndex == 1, "Unsaved inputs survive theme change");
                Check(client.Box.BackColor == Theme.Input && client.Box.ForeColor == Theme.Text, "Detached page text fields receive dark theme");
                Check(object.ReferenceEquals(pills, Theme.PillGreen), "Existing status badges receive new palette");
                Snapshot(form, "home-escuro");
                var rail = (Control)typeof(MainForm).GetField("rail", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                Key(rail.Controls[1], Keys.Enter);
                Check(pages[1].Parent != null, "Navigate by keyboard");
                Key(tests[0].Item2, Keys.Right);
                Check(tests[0].Item2.SelectedIndex == 2, "Functional test keyboard selection");
                for (int i = 1; i < 5; i++) { form.Go(i); Application.DoEvents(); Snapshot(form, "page-" + i + "-escuro"); }
                form.Go(0);
                using (var appearance = new AppearanceDialog(form))
                {
                    appearance.Opacity = 0; appearance.ShowInTaskbar = false;
                    appearance.StartPosition = FormStartPosition.Manual; appearance.Location = new Point(-20000, -20000);
                    appearance.Show(form); Application.DoEvents(); Snapshot(appearance, "aparencia-escuro");
                    Session.Cfg.Tema = "Claro"; Session.Cfg.CorDestaque = "Esmeralda"; Session.Cfg.MenuCompacto = true;
                    form.ApplyAppearance();
                    Check(appearance.BackColor == Theme.Bg, "Open dialog receives light theme");
                    Check(client.Box.BackColor == Theme.Input && client.Box.ForeColor == Theme.Text, "Round trip to light preserves roles");
                    Snapshot(appearance, "aparencia-claro"); appearance.Close();
                }
                Session.Cfg.MostrarDicas = false;
                Check(Session.Cfg.TrySave(), "Save settings");
                var restored = AppConfig.Load();
                Check(restored.Tema == "Claro" && restored.CorDestaque == "Esmeralda" && restored.MenuCompacto && !restored.MostrarDicas, "Persist appearance settings");
                Check(restored.Tecnico == "Técnico de teste" && restored.Programas.Count == Session.Cfg.Programas.Count, "Preserve existing configuration");
                foreach (string themeName in new[] { "Claro", "Escuro" })
                    foreach (string accentName in new[] { "Verde", "Esmeralda" })
                    {
                        Session.Cfg.Tema = themeName; Session.Cfg.CorDestaque = accentName; form.ApplyAppearance();
                        Check(Contrast(Theme.Text, Theme.Panel) >= 4.5 && Contrast(Theme.TextDim, Theme.Bg) >= 4.5 && Contrast(Theme.AccentText, Theme.Accent) >= 4.5, "Readable palette " + themeName + "/" + accentName);
                        Check(Field<Control>(home, "hero").BackColor == Theme.Brand, "Brand color survives accent changes");
                    }
                Session.Cfg.Tema = "Claro"; Session.Cfg.CorDestaque = "Verde"; form.ApplyAppearance();
                form.Size = new Size(1080, 640);
                for (int i = 0; i < 5; i++) { form.Go(i); Application.DoEvents(); Snapshot(form, "small-" + i); }
                var detail = Field<RoundPanel>(pages[4], "detail");
                Check(detail.VerticalScroll.Visible, "Configuration fields scroll at minimum window size");
                detail.AutoScrollPosition = new Point(0, detail.AutoScrollMinSize.Height);
                Snapshot(form, "config-rolagem");
                var fileField = Field<DarkInput>(pages[4], "file");
                Check(fileField.Bottom <= detail.ClientSize.Height, "Last configuration field is reachable");
                Check(ToolConfig.AutoName(@"C:\Tools\DiskInfo64.exe") == "CrystalDiskInfo", "Auto name from preset exe");
                Check(ToolConfig.AutoName(@"C:\Tools\Prime95_x64.exe") == "Prime95", "Auto name from custom exe");
                Check(ToolConfig.AutoName("https://www.testarteclado.com.br", new[] { "https://pt.mictests.com" }) == "Testarteclado + Mictests", "Auto name from URLs");
                pages[4].GetType().GetMethod("AddCustomSite", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(pages[4], null);
                var exeField = Field<DarkInput>(pages[4], "exe");
                var nameField = Field<DarkInput>(pages[4], "name");
                exeField.Text = "https://www.testarteclado.com.br";
                Check(nameField.Text == "Testarteclado", "Typing URL auto-fills stage name");
                pages[4].GetType().GetMethod("AddExtraUrlRow", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(pages[4], new object[] { "https://pt.mictests.com", false });
                var extraUrls = Field<System.Collections.Generic.List<Tuple<DarkInput, FlatButton>>>(pages[4], "extraUrls");
                Check(extraUrls.Count == 1, "Extra URL row added");
                extraUrls[0].Item1.Text = "https://pt.webcamtests.com";
                Check(nameField.Text == "Testarteclado + Webcamtests", "Adding second URL updates auto-filled stage name");
                Check(fileField.Text == "PRINT_Testarteclado_Webcamtests", "Evidence file code auto-fills from stage name");
                var enabledSwitch = Field<ToggleSwitch>(pages[4], "enabledCheck");
                Check(enabledSwitch.Checked, "New stage starts active in ToggleSwitch");
                form.CloseFromButton();
                foreach (var page in pages) if (page != null && !page.IsDisposed) page.Dispose();
            }
            Theme.Scale = 0.8f;
            Session.Cfg.MenuCompacto = false;
            using (var form = new HiddenMain())
            {
                form.Opacity = 0; form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000);
                form.Size = new Size(1136, 704); form.Show(); Application.DoEvents();
                for (int i = 0; i < 5; i++) { form.Go(i); Application.DoEvents(); Snapshot(form, "scaled-" + i); }
                form.CloseFromButton();
            }
            File.WriteAllText(Path.Combine(AppPaths.AppDir, "test-results.txt"), "PASS: " + checks + " checks. Screenshots rendered for five pages in light/dark and compact layouts.");
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(AppPaths.AppDir, "test-results.txt"), "FAIL: " + ex); return 1; }
    }
}

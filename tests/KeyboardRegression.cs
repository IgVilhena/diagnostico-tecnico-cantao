using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Diagnostico;

public static class KeyboardRegression
{
    static int checks;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static object Find(KeyboardTestForm form, int vk, int scan, bool ext)
    {
        return typeof(KeyboardTestForm).GetMethod("Find", Private).Invoke(form, new object[] { vk, scan, ext });
    }
    static bool State(object key, string name) { return (bool)key.GetType().GetField(name).GetValue(key); }
    static void Send(KeyboardTestForm form, int vk, int scan, bool ext, bool down)
    {
        typeof(KeyboardTestForm).GetMethod("ProcessKey", Private).Invoke(form, new object[] { vk, scan, ext, down });
    }

    public static int Run()
    {
        checks = 0;
        string output = Path.Combine(Environment.GetEnvironmentVariable("CANTAO_UI_TEST_DIR") ?? Path.Combine(Environment.CurrentDirectory, "artifacts"), "keyboard-results.txt");
        var originalWindow = Session.MainWindow;
        try
        {
            using (var owner = new Form())
            {
                Session.MainWindow = owner;
                // Não abre a janela nem instala o hook global: apenas eventos simulados.
                using (var form = new KeyboardTestForm(ToolConfig.Make("Teclado", "interno:teclado", "PRINT_TECLADO", "")))
                {
                    var left = Find(form, 0xA0, 0x2A, false);
                    var right = Find(form, 0xA1, 0x36, false);
                    Check(left != null && right != null && !ReferenceEquals(left, right), "Left and right Shift must be separate keys");
                    Check(ReferenceEquals(right, Find(form, 0xA1, 0x36, true)), "Right Shift with extended flag must be recognized");
                    Check(ReferenceEquals(left, Find(form, 0xA0, 0x2A, true)), "Left Shift identification must use its virtual key");
                    Check(ReferenceEquals(right, Find(form, 0xA1, 0, false)), "Right Shift virtual key without scan code");
                    Check(ReferenceEquals(left, Find(form, 0xA0, 0, false)), "Left Shift virtual key without scan code");
                    foreach (bool ext in new[] { false, true })
                    {
                        Check(ReferenceEquals(right, Find(form, 0x10, 0x36, ext)), "Generic Shift uses right scan code");
                        Check(ReferenceEquals(left, Find(form, 0x10, 0x2A, ext)), "Generic Shift uses left scan code");
                    }
                    Check(Find(form, 0x10, 0, false) == null, "Ambiguous Shift must not mark either side");
                    Check(Find(form, 0xFF, 0, false) == null, "Unknown key must not mark a key");
                    Check(!ReferenceEquals(Find(form, 0x0D, 0x1C, false), Find(form, 0x0D, 0x1C, true)), "Keep main Enter separate from numpad Enter");
                    Check(!ReferenceEquals(Find(form, 0x11, 0x1D, false), Find(form, 0x11, 0x1D, true)), "Keep left and right Ctrl separate");
                    Check(!ReferenceEquals(Find(form, 0x12, 0x38, false), Find(form, 0x12, 0x38, true)), "Keep Alt and AltGr separate");
                    Check(!ReferenceEquals(Find(form, 0xBF, 0x35, false), Find(form, 0x6F, 0x35, true)), "Keep punctuation separate from numpad divide");

                    Send(form, 0xA1, 0x36, true, true);
                    Check(State(right, "Down") && State(right, "Pressed") && !State(left, "Pressed"), "Right Shift press marks only the right key");
                    string last = (string)typeof(KeyboardTestForm).GetField("lastKey", Private).GetValue(form);
                    Check(last.Contains("dir"), "Last key must identify the right Shift");
                    Send(form, 0xA0, 0x2A, false, true);
                    Check(State(left, "Down") && State(right, "Down"), "Both Shift keys can be held together");
                    Send(form, 0xA1, 0x36, false, false);
                    Check(!State(right, "Down") && State(right, "Pressed") && State(left, "Down"), "Right Shift release preserves left Shift and tested state");
                    Send(form, 0x10, 0x2A, true, false);
                    Check(!State(left, "Down") && State(left, "Pressed"), "Generic Shift release updates the correct side");
                    var all = (IEnumerable)typeof(KeyboardTestForm).GetField("keys", Private).GetValue(form);
                    int pressed = 0;
                    foreach (object key in all) if (State(key, "Pressed")) pressed++;
                    Check(pressed == 2, "Shift events must not mark unrelated keys");
                }
            }
            File.WriteAllText(output, "PASS: " + checks + " keyboard regression checks (simulated events; no global hook).");
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(output, "FAIL: " + ex); return 1; }
        finally { Session.MainWindow = originalWindow; }
    }
}

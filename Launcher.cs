using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class Launcher
{
    private static readonly object LogLock = new object();
    internal static string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NOMAD-v6.log");

    internal static string Log(string message)
    {
        string line = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz") + "  " + message + Environment.NewLine;
        lock (LogLock) {
            try { File.AppendAllText(LogPath, line, Encoding.UTF8); }
            catch (Exception error) {
                if (!(error is IOException || error is UnauthorizedAccessException)) throw;
                LogPath = Path.Combine(Path.GetTempPath(), "NOMAD-v6.log");
                try { File.AppendAllText(LogPath, line, Encoding.UTF8); }
                catch (Exception fallback) {
                    if (!(fallback is IOException || fallback is UnauthorizedAccessException)) throw;
                    line += "Falha ao salvar o log: " + fallback.Message + Environment.NewLine;
                }
            }
        }
        return line;
    }
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--self-test")
            return Execute("demonstrar", Path.GetTempPath(), "", "", "", new CatalogItem[0], Console.WriteLine);
        if (args.Length == 2 && args[0] == "--verify-test")
            return Execute("verificar", args[1], "", "", "", new CatalogItem[0], Console.WriteLine);
        if (args.Length == 1 && args[0] == "--catalog-test") {
            var catalog = KiwixCatalog.Load();
            Console.WriteLine("Total: " + catalog.Length);
            foreach (string lang in new[] {"pt", "en"}) {
                var wiki = catalog.Where(i => i.file.StartsWith("wikipedia_" + lang + "_all_")).ToArray();
                Console.WriteLine(lang + ": " + string.Join(", ", wiki.Select(i => i.edition).Distinct()));
                if (!new[] {"mini", "nopic", "maxi"}.All(e => wiki.Any(i => i.edition == e))) return 1;
            }
            return catalog.Length > 1000 ? 0 : 1;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        AppDomain.CurrentDomain.UnhandledException += (s,e) => Log("Falha fatal: " + e.ExceptionObject);
        if (args.Length == 3 && args[0] == "--catalog-preview") {
            int total, count;
            var sample = KiwixCatalog.Parse(File.ReadAllText(args[1]), out total, out count);
            using (var window = new CatalogWindow(new CatalogItem[0], sample)) {
                window.Shown += (s,e) => Capture(window, args[2]);
                Application.Run(window);
            }
            return 0;
        }
        using (var window = new KitWindow(args.Contains("--demo")))
        {
            Application.ThreadException += (s,e) => window.ReportError(e.Exception);
            if (args.Length == 2 && args[0] == "--logs-test") {
                LogPath = args[1];
                int result = 1;
                window.Shown += (s,e) => window.BeginInvoke(new Action(() => {
                    try { window.CheckMenuAndLogs(); result = 0; }
                    catch (Exception error) { window.ReportError(error); }
                    finally { window.Close(); }
                }));
                Application.Run(window);
                return result;
            }
            if (args.Length == 2 && args[0] == "--preview")
                window.Shown += (s,e) => Capture(window, args[1]);
            Application.Run(window);
        }
        return 0;
    }

    private static void Capture(Form window, string path)
    {
        window.BeginInvoke(new Action(() => {
            using (var bitmap = new Bitmap(window.Width, window.Height)) {
                window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(path);
            }
            window.Close();
        }));
    }

    internal static int Execute(string mode, string folder, string selection, string pauseFile, string continueFile, CatalogItem[] items, Action<string> line)
    {
        string script = Path.Combine(Path.GetTempPath(), "nomad-" + Guid.NewGuid() + ".ps1");
        string pick = script + ".json";
        try
        {
            using (var reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("install")))
                File.WriteAllText(script, reader.ReadToEnd(), new UTF8Encoding(true));
            var info = new ProcessStartInfo(
                Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\" " + mode);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;
            info.EnvironmentVariables.Remove("PSModulePath");
            info.EnvironmentVariables["NOMAD_KIT_ROOT"] = folder;
            info.EnvironmentVariables["NOMAD_KIT_SELECTION"] = selection;
            info.EnvironmentVariables["NOMAD_KIT_PAUSE"] = pauseFile;
            info.EnvironmentVariables["NOMAD_KIT_CONTINUE"] = continueFile;
            File.WriteAllText(pick, KiwixCatalog.Json.Serialize(items), new UTF8Encoding(true));
            info.EnvironmentVariables["NOMAD_KIT_CATALOG"] = pick;
            using (var process = new Process { StartInfo = info })
            {
                process.OutputDataReceived += (s, e) => { if (e.Data != null) line(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) line(e.Data); };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                return process.ExitCode;
            }
        }
        catch (Exception error)
        {
            line("ERROR|" + error.Message);
            return 1;
        }
        finally
        {
            try { File.Delete(script); } catch (IOException) { }
            try { File.Delete(pick); } catch (IOException) { }
        }
    }
}

internal sealed class KitWindow : Form
{
    private readonly Color ink = Color.FromArgb(19, 37, 41);
    private readonly Color muted = Color.FromArgb(62, 85, 87);
    private readonly Color accent = Color.FromArgb(10, 91, 82);
    private readonly Color canvas = Color.FromArgb(244, 248, 247);
    private readonly CheckBox[] choices = new CheckBox[10];
    private readonly TextBox directory = new TextBox();
    private readonly TextBox detail = new TextBox();
    private readonly TextBox logView = new TextBox();
    private readonly TabControl activity = new TabControl();
    private readonly TabPage logs = new TabPage("Logs");
    private ContextMenuStrip toolsMenu;
    private readonly Label status = new Label();
    private readonly Label space = new Label();
    private readonly ProgressBar progress = new ProgressBar();
    private readonly Button prepare, install, verify, choose, pause, catalogButton, tools, finishCatalog;
    private bool busy, failed, closeAfterPause;
    private string pauseFile, continueFile;
    private CatalogItem[] items = new CatalogItem[0], cachedCatalog;

    internal KitWindow(bool demo)
    {
        Text = "N.O.M.A.D. | Kit de emergência";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(890, 755);
        MinimumSize = new Size(850, 740);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = canvas;
        ForeColor = ink;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(27, 18, 27, 18), ColumnCount = 1, RowCount = 10 };
        foreach (int height in new[] { 74, 32, 46, 46, 230, 54, 49, 44, 29 })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(layout);
        var intro = new Panel { Dock = DockStyle.Fill };
        intro.Controls.Add(new Label { Text = "N.O.M.A.D. de emergência", Location = new Point(0, 0), Size = new Size(680, 44), Font = new Font("Segoe UI", 25, FontStyle.Bold) });
        intro.Controls.Add(new Label { Text = "Prepare o kit com internet. Instale e rode quando precisar.", Location = new Point(2, 48), AutoSize = true, ForeColor = muted });
        layout.Controls.Add(intro, 0, 0);
        layout.Controls.Add(new Label { Text = "Pasta do kit", Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft, Font = new Font(Font, FontStyle.Bold) }, 0, 1);
        var path = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        path.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        path.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        string executableFolder = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        directory.Text = Directory.Exists(Path.Combine(executableFolder,"Dados")) || !Directory.Exists(@"D:\NOMAD")
            ? executableFolder : @"D:\NOMAD";
        directory.Dock = DockStyle.Fill;
        directory.Margin = new Padding(0, 9, 8, 0);
        directory.AccessibleName = "Pasta do kit";
        choose = MakeButton("Escolher", false);
        choose.Click += (s,e) => {
            using (var picker = new FolderBrowserDialog { Description = "Escolha a pasta que guardará o kit", SelectedPath = directory.Text })
                if (picker.ShowDialog(this) == DialogResult.OK) directory.Text = picker.SelectedPath;
        };
        path.Controls.Add(directory);
        path.Controls.Add(choose);
        layout.Controls.Add(path, 0, 2);

        var content = new GroupBox { Text = "Conteúdo para guardar", Dock = DockStyle.Fill, Padding = new Padding(12, 6, 10, 7), ForeColor = ink };
        var list = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 7 };
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int i = 0; i < 7; i++) list.RowStyles.Add(new RowStyle(SizeType.Percent, 100f/7));
        string[] labels = {
            "Wikipédia em português", "Catálogo Kiwix completo em português",
            "Mapa detalhado do Brasil", "Cursos Kolibri em português",
            "IA local · Qwen3 4B", "Guias brasileiros em PDF",
            "Kiwix portátil para Windows", "Instalador Kolibri para Windows",
            "OsmAnd + mapas do Brasil · Android", "Wikipédia completa em inglês (maxi)"
        };
        string[] keys = { "wiki", "kiwixpt", "brasil", "cursos", "ia", "guias", "portatil", "kolibriwin", "android", "wikien" };
        // Valores aproximados em GB decimais. O catálogo individual fornece o tamanho de cada arquivo.
        double[] sizes = { 21, 47, 5, 42, 8, 0.02, 0.16, 0.30, 6, 127.42, 2, 0 };
        for (int i = 0; i < choices.Length; i++) {
            var check = new CheckBox { Text = labels[i] + " · ~" + sizes[i].ToString("0.##") + " GB", Tag = keys[i], Dock = DockStyle.Fill, AutoEllipsis = true, Checked = i == 0 || i == 2 || i == 5 || i == 6 };
            choices[i] = check;
            list.Controls.Add(check, i % 2, i / 2);
        }
        var pi = new CheckBox { Text = "Internet-in-a-Box · Raspberry Pi · ~2 GB", Tag = "iiab", Dock = DockStyle.Fill, AutoEllipsis = true };
        list.Controls.Add(pi, 0, 5);
        var nomad = new CheckBox { Text = "Coleções e apps no painel N.O.M.A.D. · tamanho depende da escolha", Tag = "nomadcatalog", Dock = DockStyle.Fill };
        list.Controls.Add(nomad, 0, 6);
        list.SetColumnSpan(nomad, 2);
        var options = choices.Concat(new[] {pi,nomad}).ToArray();
        Action updateSpace = () => {
            double contentGb = items.Sum(item => item.size) / 1000000000.0;
            for (int i = 0; i < sizes.Length; i++)
                if (options[i].Checked && (i != 0 || !choices[1].Checked) &&
                    (i != 9 || !items.Any(item => item.file == "wikipedia_en_all_maxi_2026-08.zim"))) contentGb += sizes[i];
            double kitGb = 10 + contentGb;
            double workGb = kitGb + 10 + (choices[2].Checked ? sizes[2] : 0) +
                (choices[3].Checked ? sizes[3] : 0) + (choices[4].Checked ? sizes[4] : 0);
            string extra = nomad.Checked ? " + itens do painel" : "";
            space.Text = string.Format("Kit estimado: ~{0:N1} GB{2}   ·   Espaço livre para preparar: ~{1:N1} GB{2}\r\nInclui a base do servidor. A preparação usa espaço temporário.", kitGb, workGb, extra);
        };
        foreach (var option in options) option.CheckedChanged += (s,e) => updateSpace();
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        catalogButton = MakeButton("Catálogo Kiwix", false);
        catalogButton.Width = 220;
        tools = MakeButton("Abrir ferramentas", false);
        tools.Width = 150;
        toolbar.Controls.AddRange(new Control[] {catalogButton,tools});
        layout.Controls.Add(toolbar, 0, 3);
        catalogButton.Click += (s,e) => {
            using (var browser = new CatalogWindow(items, cachedCatalog)) {
                var result = browser.ShowDialog(this);
                cachedCatalog = browser.CachedCatalog;
                if (result == DialogResult.OK) {
                    items = browser.Selection;
                    if (items.Length > 0) { choices[0].Checked = false; choices[1].Checked = false; }
                    UpdateCatalogButton();
                    updateSpace();
                }
            }
        };
        tools.Click += (s,e) => OpenTools();
        Action loadSelection = () => {
            try {
                string file = Path.Combine(directory.Text, "Dados", "Selecao.txt");
                if (File.Exists(file)) {
                    var keysSaved = File.ReadAllText(file).Trim().Split(',');
                    foreach (var check in options) check.Checked = keysSaved.Contains((string)check.Tag);
                }
                string pick = Path.Combine(directory.Text, "Dados", "Selecao-Kiwix.json");
                items = File.Exists(pick) ? KiwixCatalog.Json.Deserialize<CatalogItem[]>(File.ReadAllText(pick)) : new CatalogItem[0];
                UpdateCatalogButton();
                updateSpace();
            } catch (Exception error) { ReportError(error); }
        };
        directory.TextChanged += (s,e) => loadSelection();
        loadSelection();
        content.Controls.Add(list);
        layout.Controls.Add(content, 0, 4);
        space.Dock = DockStyle.Fill;
        space.TextAlign = ContentAlignment.MiddleLeft;
        space.ForeColor = muted;
        layout.Controls.Add(space, 0, 5);
        updateSpace();

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        prepare = MakeButton("Baixar e preparar offline", true);
        install = MakeButton("Instalar / abrir", false);
        verify = MakeButton("Verificar kit", false);
        pause = MakeButton("Pausar preparo", false);
        pause.Enabled = false;
        pause.Click += (s,e) => RequestPause();
        finishCatalog = MakeButton("Concluir seleção", false);
        finishCatalog.Visible = false;
        finishCatalog.Click += (s,e) => {
            File.WriteAllText(continueFile, "continuar");
            finishCatalog.Visible = false;
            status.Text = "Conferindo os downloads e aplicativos selecionados.";
        };
        foreach (var button in new[] {prepare, install, verify, pause, finishCatalog}) { button.Width = 140; actions.Controls.Add(button); }
        prepare.Width = 186;
        prepare.Click += (s,e) => Run("preparar", options);
        install.Click += (s,e) => Run("instalar", options);
        verify.Click += (s,e) => Run("verificar", options);
        layout.Controls.Add(actions, 0, 6);
        status.Text = "Pronto para escolher o conteúdo";
        status.Dock = DockStyle.Fill;
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.AccessibleName = "Estado da operação";
        status.AutoEllipsis = true;
        layout.Controls.Add(status, 0, 7);
        progress.Dock = DockStyle.Fill;
        progress.AccessibleName = "Progresso da operação";
        progress.Margin = new Padding(0, 2, 0, 12);
        layout.Controls.Add(progress, 0, 8);
        detail.Multiline = true;
        detail.ReadOnly = true;
        detail.ScrollBars = ScrollBars.Vertical;
        detail.Dock = DockStyle.Fill;
        detail.BackColor = Color.White;
        detail.Margin = Padding.Empty;
        detail.ForeColor = muted;
        detail.AccessibleName = "Detalhes de download e instalação";
        detail.Text = "Baixar e preparar offline guarda todos os itens marcados. Instalar / abrir é uma ação separada.";
        var progressTab = new TabPage("Atividade");
        progressTab.Controls.Add(detail);
        activity.Dock = DockStyle.Fill;
        activity.TabPages.Add(progressTab);
        activity.TabPages.Add(logs);
        logView.Dock = DockStyle.Fill;
        logView.Multiline = true;
        logView.ReadOnly = true;
        logView.ScrollBars = ScrollBars.Both;
        logView.WordWrap = false;
        logView.AccessibleName = "Logs do instalador";
        var openLog = MakeButton("Abrir arquivo de log", false);
        openLog.Dock = DockStyle.Bottom;
        openLog.Click += (s,e) => StartTool(Launcher.LogPath);
        logs.Controls.Add(logView);
        logs.Controls.Add(openLog);
        layout.Controls.Add(activity, 0, 9);
        AppendLog("Instalador v6 aberto.");
        activity.Selected += (s,e) => {
            if (activity.SelectedTab != logs) return;
            try {
                using (var stream = new FileStream(Launcher.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                    if (stream.Length > 150000) stream.Seek(-150000, SeekOrigin.End);
                    using (var reader = new StreamReader(stream)) logView.Text = reader.ReadToEnd();
                }
                logView.SelectionStart = logView.TextLength;
                logView.ScrollToCaret();
            } catch (IOException error) { AppendLog(error.Message); }
            catch (UnauthorizedAccessException error) { AppendLog(error.Message); }
        };
        Disposed += (s,e) => { if (toolsMenu != null) toolsMenu.Dispose(); };
        AcceptButton = prepare;
        Shown += (s,e) => { if (demo) Run("demonstrar", options); };
        FormClosing += (s,e) => {
            if (!busy) return;
            e.Cancel = true;
            if (pause.Enabled) { closeAfterPause = true; RequestPause(); }
        };
    }

    private void UpdateCatalogButton()
    {
        catalogButton.Text = items.Length == 0 ? "Catálogo Kiwix" : "Catálogo Kiwix · " + items.Length + " selecionados";
    }

    private void OpenTools()
    {
        if (toolsMenu != null) toolsMenu.Dispose();
        var menu = toolsMenu = new ContextMenuStrip();
        string extras = Path.Combine(directory.Text, "Dados", "Extras");
        menu.Items.Add("Abrir painel N.O.M.A.D.", null, (s,e) => StartTool("http://localhost:8080"));
        menu.Items.Add("Abrir aplicativos do painel", null, (s,e) => StartTool("http://localhost:8080/supply-depot"));
        menu.Items.Add("Abrir biblioteca do servidor", null, (s,e) => StartTool("http://localhost:8090"));
        menu.Items.Add("Abrir cursos do servidor", null, (s,e) => StartTool("http://localhost:8310"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Abrir Kiwix portátil", null, (s,e) => StartPortableKiwix(extras)).Enabled = File.Exists(Path.Combine(extras,"Kiwix-Desktop-x64.zip"));
        menu.Items.Add("Instalar Kolibri para Windows", null, (s,e) => StartExtra(extras,"Kolibri-Windows.exe")).Enabled = File.Exists(Path.Combine(extras,"Kolibri-Windows.exe"));
        menu.Items.Add("Instalar Raspberry Pi Imager", null, (s,e) => StartExtra(extras,"Raspberry-Pi-Imager-Windows.exe")).Enabled = File.Exists(Path.Combine(extras,"Raspberry-Pi-Imager-Windows.exe"));
        menu.Items.Add("Abrir pasta de extras", null, (s,e) => StartTool(extras)).Enabled = Directory.Exists(extras);
        menu.Show(tools, new Point(0,tools.Height));
    }

    internal void CheckMenuAndLogs()
    {
        for (int i = 0; i < 3; i++) {
            OpenTools();
            if (!toolsMenu.Visible) throw new InvalidOperationException("Menu não abriu.");
            toolsMenu.Close(ToolStripDropDownCloseReason.AppClicked);
            if (toolsMenu.IsDisposed || toolsMenu.Visible)
                throw new InvalidOperationException("Menu descartado durante o fechamento.");
        }
        ReportError(new InvalidOperationException("Erro simulado do instalador."));
        if (activity.SelectedTab != logs || !logView.Text.Contains("Erro simulado do instalador.") ||
            !File.ReadAllText(Launcher.LogPath).Contains("Erro simulado do instalador."))
            throw new InvalidOperationException("A aba e o arquivo de log devem registrar o erro.");
    }

    private void AppendLog(string message)
    {
        string line = Launcher.Log(message);
        if (logView.TextLength > 150000) logView.Clear();
        logView.AppendText(line);
    }

    internal void ReportError(Exception error)
    {
        AppendLog(error.ToString());
        status.Text = error.Message;
        activity.SelectedTab = logs;
    }

    public sealed class KitEntry
    {
        public string path { get; set; }
        public string sha256 { get; set; }
    }

    public sealed class KitManifest
    {
        public KitEntry[] files { get; set; }
    }

    private string VerifyExtra(string extras, string name)
    {
        string file = Path.Combine(extras, name);
        string receipt = Path.Combine(directory.Text, "Dados", "Kit-pronto.json");
        var manifest = KiwixCatalog.Json.Deserialize<KitManifest>(File.ReadAllText(receipt));
        var expected = manifest.files.FirstOrDefault(entry =>
            string.Equals(entry.path, Path.Combine("Extras",name), StringComparison.OrdinalIgnoreCase));
        if (expected == null) throw new IOException("Este arquivo não está no manifesto do kit.");
        using (var stream = File.OpenRead(file))
        using (var sha = SHA256.Create()) {
            var hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            if (!hash.Equals(expected.sha256,StringComparison.OrdinalIgnoreCase)) throw new IOException("Arquivo alterado: " + name);
        }
        return file;
    }

    private void StartTool(string path)
    {
        try { Process.Start(path); } catch (Exception error) { ReportError(error); }
    }

    private void StartExtra(string extras, string name)
    {
        try { StartTool(VerifyExtra(extras,name)); } catch (Exception error) { ReportError(error); }
    }

    private async void StartPortableKiwix(string extras)
    {
        try {
            var zip = VerifyExtra(extras, "Kiwix-Desktop-x64.zip");
            status.Text = "Abrindo Kiwix portátil…";
            var app = await Task.Run(() => {
                string folder = Path.Combine(Path.GetTempPath(),"nomad-kiwix-" + Guid.NewGuid().ToString("N"));
                ZipFile.ExtractToDirectory(zip, folder);
                return Directory.GetFiles(folder,"kiwix-desktop.exe",SearchOption.AllDirectories).Single();
            });
            StartTool(app);
            status.Text = "Kiwix portátil aberto. Selecione um arquivo ZIM em Dados\\Acervo.";
        } catch (Exception error) { ReportError(error); }
    }

    private void RequestPause()
    {
        if (string.IsNullOrEmpty(pauseFile)) return;
        File.WriteAllText(pauseFile, "pause");
        pause.Enabled = false;
        status.Text = "Pausa solicitada. Aguardando um ponto seguro.";
    }

    private Button MakeButton(string text, bool primary)
    {
        var button = new Button { Text = text, Height = 37, Margin = new Padding(0, 7, 9, 0), FlatStyle = FlatStyle.Flat,
            BackColor = primary ? accent : Color.White, ForeColor = primary ? Color.White : ink, UseVisualStyleBackColor = false };
        button.FlatAppearance.BorderColor = primary ? accent : Color.FromArgb(164, 185, 184);
        return button;
    }

    private async void Run(string mode, CheckBox[] options)
    {
        if (busy) return;
        string folder;
        try {
            folder = Path.GetFullPath(directory.Text);
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Escolha uma pasta existente.");
        } catch (Exception ex) { ReportError(ex); return; }
        if (mode == "preparar") {
            try {
                string executable = Path.Combine(folder, Path.GetFileName(Application.ExecutablePath));
                if (!File.Exists(executable)) File.Copy(Application.ExecutablePath, executable);
            } catch (Exception ex) { ReportError(ex); return; }
        }
        string selection = string.Join(",", options.Where(c => c.Checked).Select(c => (string)c.Tag));
        AppendLog("Iniciando " + mode + " · " + folder);
        busy = true;
        pauseFile = mode == "preparar" ? Path.Combine(Path.GetTempPath(), "nomad-pause-" + Guid.NewGuid()) : "";
        continueFile = mode == "preparar" ? Path.Combine(Path.GetTempPath(), "nomad-continue-" + Guid.NewGuid()) : "";
        pause.Enabled = mode == "preparar";
        failed = false;
        detail.Clear();
        status.Text = "Iniciando";
        progress.Value = 0;
        foreach (Control control in new Control[] {prepare, install, verify, choose, directory, catalogButton, tools}.Concat(options)) control.Enabled = false;
        int result = await Task.Run(() => Launcher.Execute(mode, folder, selection, pauseFile, continueFile, items, line => {
            if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(() => UpdateProgress(line)));
        }));
        if (IsDisposed) return;
        busy = false;
        AppendLog("Operação " + mode + " terminou com código " + result + ".");
        if (!string.IsNullOrEmpty(pauseFile) && File.Exists(pauseFile)) File.Delete(pauseFile);
        if (!string.IsNullOrEmpty(continueFile) && File.Exists(continueFile)) File.Delete(continueFile);
        pause.Enabled = false;
        finishCatalog.Visible = false;
        foreach (Control control in new Control[] {prepare, install, verify, choose, directory, catalogButton, tools}.Concat(options)) control.Enabled = true;
        if (result != 0 && !failed) status.Text = "A operação terminou com erro. Consulte os detalhes.";
        if (result != 0) { progress.Style = ProgressBarStyle.Continuous; activity.SelectedTab = logs; }
        if (closeAfterPause) Close();
    }

    private void UpdateProgress(string line)
    {
        AppendLog(line.TrimStart('\uFEFF'));
        if (line == "PICK") { finishCatalog.Visible = true; return; }
        string[] fields = line.Split(new[] {'|'}, 3);
        int percent;
        if (fields.Length == 3 && fields[0] == "PROGRESS" && int.TryParse(fields[1], out percent)) {
            status.Text = percent >= 0 && percent < 100 ? percent + "%  ·  " + fields[2] : fields[2];
            progress.Style = percent < 0 ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            if (percent >= 0) progress.Value = Math.Min(100, percent);
            return;
        }
        if (line.StartsWith("ERROR|", StringComparison.Ordinal)) {
            failed = true;
            status.Text = line.Substring(6);
        }
        if (detail.TextLength > 150000) detail.Clear();
        detail.AppendText(line.TrimStart('\uFEFF') + Environment.NewLine);
    }
}

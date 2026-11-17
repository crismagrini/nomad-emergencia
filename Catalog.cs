using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;

public sealed class CatalogItem
{
    public string title { get; set; }
    public string file { get; set; }
    public string url { get; set; }
    public long size { get; set; }
    public string languages { get; set; }
    public string edition { get; set; }
    public string date { get; set; }

    internal string EditionLabel {
        get {
            switch (edition) {
                case "mini": return "Resumida (mini)";
                case "nopic": return "Sem imagens (nopic)";
                case "maxi": return "Completa (maxi)";
                default: return string.IsNullOrWhiteSpace(edition) ? "Padrão" : edition;
            }
        }
    }
}

internal static class KiwixCatalog
{
    private const string Base = "https://opds.library.kiwix.org/catalog/v2/entries";
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly Regex Zim = new Regex(@"^[a-zA-Z0-9._-]+\.zim$");
    internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
    internal static Dictionary<string,string> LanguageNames = new Dictionary<string,string>();

    internal static CatalogItem[] Parse(string xml, out int total, out int count)
    {
        XDocument document;
        using (var text = new StringReader(xml))
        using (var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
            document = XDocument.Load(reader);
        var feed = document.Root;
        var totalNode = feed.Elements().FirstOrDefault(e => e.Name.LocalName == "totalResults");
        if (totalNode == null || !int.TryParse(totalNode.Value, out total) || total < 0)
            throw new InvalidDataException("O Kiwix retornou um catálogo inválido.");
        var entries = feed.Elements(Atom + "entry").ToArray();
        count = entries.Length;
        var items = new List<CatalogItem>();
        foreach (var entry in entries) {
            var link = entry.Elements(Atom + "link").FirstOrDefault(x => (string)x.Attribute("type") == "application/x-zim");
            if (link == null) continue;
            string href = (string)link.Attribute("href") ?? "";
            Uri url;
            long size;
            if (!Uri.TryCreate(Regex.Replace(href, @"\.meta4$", ""), UriKind.Absolute, out url) ||
                url.Scheme != Uri.UriSchemeHttps || !(url.Host == "download.kiwix.org" || url.Host == "lb.download.kiwix.org" || url.Host == "lbo.download.kiwix.org") ||
                !long.TryParse((string)link.Attribute("length"), NumberStyles.None, CultureInfo.InvariantCulture, out size)) continue;
            var file = Path.GetFileName(url.AbsolutePath);
            if (!Zim.IsMatch(file)) continue;
            var date = (string)entry.Element(Atom + "updated") ?? "";
            items.Add(new CatalogItem {
                title = (string)entry.Element(Atom + "title") ?? file, file = file,
                url = url.AbsoluteUri, size = size, languages = (string)entry.Element(Atom + "language") ?? "",
                edition = (string)entry.Element(Atom + "flavour") ?? "", date = date.Length >= 10 ? date.Substring(0,10) : date
            });
        }
        return items.ToArray();
    }

    internal static CatalogItem[] Load()
    {
        ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
        var result = new List<CatalogItem>();
        int offset = 0, total, count;
        using (var client = new CatalogClient()) {
            client.Encoding = Encoding.UTF8;
            do {
                var xml = client.DownloadString(Base + "?count=1000&start=" + offset);
                result.AddRange(Parse(xml, out total, out count));
                if (count == 0 && offset < total) throw new IOException("O Kiwix retornou uma página incompleta.");
                offset += count;
            } while (offset < total);
            try {
                var xml = client.DownloadString("https://opds.library.kiwix.org/catalog/v2/languages");
                XNamespace dc = "http://purl.org/dc/terms/";
                var document = XDocument.Parse(xml);
                LanguageNames = document.Root.Elements(Atom + "entry")
                    .Where(entry => entry.Element(dc + "language") != null && entry.Element(Atom + "title") != null)
                    .GroupBy(entry => (string)entry.Element(dc + "language"))
                    .ToDictionary(group => group.Key, group => (string)group.First().Element(Atom + "title"));
            } catch (WebException) { } catch (XmlException) { }
        }
        return result.GroupBy(item => item.file, StringComparer.OrdinalIgnoreCase).Select(group => group.First())
            .OrderBy(item => item.title, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.file).ToArray();
    }

    private sealed class CatalogClient : WebClient
    {
        protected override WebRequest GetWebRequest(Uri address)
        {
            WebRequest request = base.GetWebRequest(address);
            request.Timeout = 45000;
            return request;
        }
    }
}

internal sealed class CatalogWindow : Form
{
    private readonly TextBox search = new TextBox();
    private readonly ComboBox languages = new ComboBox();
    private readonly ComboBox editions = new ComboBox();
    private readonly ListView results = new ListView();
    private readonly Label summary = new Label();
    private readonly CheckBox showSelected = new CheckBox();
    private readonly Dictionary<string, CatalogItem> selected;
    private CatalogItem[] catalog;
    private bool refreshing;
    internal CatalogItem[] Selection { get { return selected.Values.OrderBy(item => item.file).ToArray(); } }
    internal CatalogItem[] CachedCatalog { get { return catalog; } }

    private sealed class Filter
    {
        internal string Value, Label;
        public override string ToString() { return Label; }
    }

    internal CatalogWindow(CatalogItem[] current, CatalogItem[] cached)
    {
        selected = current.ToDictionary(item => item.file, StringComparer.OrdinalIgnoreCase);
        catalog = cached;
        Text = "Catálogo de conteúdo · Kiwix";
        Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(960, 610);
        MinimumSize = new Size(820, 500);
        BackColor = Color.FromArgb(244,248,247);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(18,10,18,12) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
        Controls.Add(layout);
        var filters = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 3 };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,27));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,23));
        var fields = new Control[] {search, languages, editions};
        var labels = new[] {"Buscar título ou arquivo", "Idioma", "Edição"};
        for (int i = 0; i < fields.Length; i++) {
            filters.Controls.Add(new Label { Text = labels[i], AutoSize = true },i,0);
            fields[i].Dock = DockStyle.Fill;
            fields[i].AccessibleName = labels[i];
            filters.Controls.Add(fields[i],i,1);
        }
        languages.DropDownStyle = editions.DropDownStyle = ComboBoxStyle.DropDownList;
        layout.Controls.Add(filters,0,0);
        results.Dock = DockStyle.Fill;
        results.View = View.Details;
        results.CheckBoxes = true;
        results.FullRowSelect = true;
        results.HideSelection = false;
        results.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        results.Columns.Add("Título / arquivo", 405);
        results.Columns.Add("Idioma", 138);
        results.Columns.Add("Edição", 143);
        results.Columns.Add("Data", 92);
        results.Columns.Add("Tamanho", 95);
        layout.Controls.Add(results,0,1);
        summary.Dock = DockStyle.Fill;
        summary.TextAlign = ContentAlignment.MiddleLeft;
        summary.Text = "Consultando o catálogo do Kiwix…";
        layout.Controls.Add(summary,0,2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        showSelected.Text = "Só selecionados";
        showSelected.AutoSize = true;
        showSelected.Margin = new Padding(8,9,8,0);
        var mark = new Button { Text = "Marcar resultados", AutoSize = true, Height = 35 };
        var clear = new Button { Text = "Desmarcar resultados", AutoSize = true, Height = 35 };
        var apply = new Button { Text = "Usar seleção", AutoSize = true, Height = 35, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Voltar", AutoSize = true, Height = 35, DialogResult = DialogResult.Cancel };
        actions.Controls.AddRange(new Control[] {apply,cancel,showSelected,mark,clear});
        layout.Controls.Add(actions,0,3);
        CancelButton = cancel;
        search.TextChanged += (s,e) => RefreshResults();
        languages.SelectedIndexChanged += (s,e) => RefreshResults();
        editions.SelectedIndexChanged += (s,e) => RefreshResults();
        showSelected.CheckedChanged += (s,e) => RefreshResults();
        mark.Click += (s,e) => SetVisible(true);
        clear.Click += (s,e) => SetVisible(false);
        results.ItemChecked += (s,e) => {
            if (refreshing) return;
            var item = (CatalogItem)e.Item.Tag;
            if (e.Item.Checked) selected[item.file] = item;
            else selected.Remove(item.file);
            UpdateSummary();
        };
        Shown += async (s,e) => {
            try {
                if (catalog == null) catalog = await Task.Run(() => KiwixCatalog.Load());
                if (!IsDisposed) LoadFilters();
            } catch (Exception error) {
                if (!IsDisposed) summary.Text = "Catálogo indisponível. Feche e tente novamente. " + error.Message;
            }
        };
    }

    private void LoadFilters()
    {
        languages.Items.Add(new Filter {Value="",Label="Todos os idiomas"});
        var codes = catalog.SelectMany(item => item.languages.Split(',')).Where(code => code.Length > 0).Distinct()
            .Select(code => new Filter {Value=code,Label=LanguageName(code)}).OrderBy(filter => filter.Label).ToArray();
        languages.Items.AddRange(codes);
        editions.Items.Add(new Filter {Value="",Label="Todas as edições"});
        editions.Items.AddRange(catalog.GroupBy(item => item.edition).Where(group => group.Key.Length != 0)
            .Select(group => new Filter {Value=group.Key,Label=group.First().EditionLabel}).OrderBy(filter => filter.Label).ToArray());
        languages.SelectedIndex = 0;
        editions.SelectedIndex = 0;
        RefreshResults();
    }

    internal static string LanguageName(string code)
    {
        switch (code) {
            case "por": return "Português";
            case "eng": return "Inglês";
            case "spa": return "Espanhol";
            case "fra": case "fre": return "Francês";
            case "deu": case "ger": return "Alemão";
            case "ita": return "Italiano";
            case "mul": return "Multilíngue";
            default:
                string label;
                return KiwixCatalog.LanguageNames.TryGetValue(code,out label) ? label + " (" + code + ")" : code;
        }
    }

    private void RefreshResults()
    {
        if (catalog == null || languages.SelectedItem == null || editions.SelectedItem == null) return;
        string q = search.Text.Trim();
        string lang = ((Filter)languages.SelectedItem).Value;
        string flavour = ((Filter)editions.SelectedItem).Value;
        var matching = showSelected.Checked ? Selection.AsEnumerable() : catalog.Where(item =>
            (lang.Length == 0 || item.languages.Split(',').Contains(lang)) &&
            (flavour.Length == 0 || item.edition == flavour) &&
            (q.Length == 0 || CultureInfo.CurrentCulture.CompareInfo.IndexOf(item.title + " " + item.file, q, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0));
        refreshing = true;
        results.BeginUpdate();
        try {
            results.Items.Clear();
            foreach (var item in matching) {
                var row = new ListViewItem(new[] {
                    item.title + "  ·  " + item.file,
                    string.Join(", ",item.languages.Split(',').Select(LanguageName)),
                    item.EditionLabel,item.date,FormatSize(item.size)
                }) { Tag = item, Checked = selected.ContainsKey(item.file) };
                results.Items.Add(row);
            }
        } finally { results.EndUpdate(); refreshing = false; }
        UpdateSummary();
    }

    private void SetVisible(bool value)
    {
        foreach (ListViewItem row in results.Items) {
            var item = (CatalogItem)row.Tag;
            if (value) selected[item.file] = item;
            else selected.Remove(item.file);
        }
        RefreshResults();
    }

    private void UpdateSummary()
    {
        summary.Text = string.Format("{0:N0} resultados  ·  {1:N0} selecionados  ·  {2}",
            results.Items.Count, selected.Count, FormatSize(selected.Values.Sum(item => item.size)));
    }

    private static string FormatSize(long bytes)
    {
        return bytes >= 1073741824 ? string.Format("{0:N1} GiB", bytes / 1073741824.0) : string.Format("{0:N1} MiB", bytes / 1048576.0);
    }
}


using System.Text;
using System.Text.RegularExpressions;
using System.ComponentModel.DataAnnotations;

namespace Converters;

public sealed class AspxControl
{
    public string TagName { get; init; } = "";
    public Dictionary<string,string> Attributes { get; } = new();
    public string Inner { get; init; } = "";
}

public static class AspxParser
{
    private static readonly Regex OpenOrSelf = new(
        @"<asp:(?<tag>[A-Za-z]+)\s+(?<attrs>[^>]*?)(/>|>)",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AttrRegex = new(
        @"(?<name>[A-Za-z_:.-]+)\s*=\s*""(?<val>[^""]*)""",
        RegexOptions.Compiled);

    public static IEnumerable<AspxControl> ParseControls(string aspx)
    {
        foreach (Match m in OpenOrSelf.Matches(aspx))
        {
            var tag = m.Groups["tag"].Value;
            if (tag.Equals("Content", StringComparison.OrdinalIgnoreCase)) continue;
            var control = new AspxControl { TagName = tag };
            foreach (Match a in AttrRegex.Matches(m.Groups["attrs"].Value))
                control.Attributes[a.Groups["name"].Value] = a.Groups["val"].Value;
            yield return control;
        }
    }
}

public sealed class GridColumn
{
    public string Kind { get; init; } = ""; // BoundField, ButtonField
    public Dictionary<string,string> Attributes { get; } = new();
}

public static class GridParser
{
    private static readonly Regex ColumnsBlock = new(
        @"<asp:GridView(?<gvAttrs>[^>]*)>(?<inner>.*?)</asp:GridView>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ColumnTag = new(
        @"<asp:(?<kind>BoundField|ButtonField)\s+(?<attrs>[^>]*)/>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AttrRegex = new(
        @"(?<name>[A-Za-z_:.-]+)\s*=\s*""(?<val>[^""]*)""",
        RegexOptions.Compiled);

    public static (Dictionary<string,string> gridAttrs, List<GridColumn> columns) Parse(string aspx)
    {
        var m = ColumnsBlock.Match(aspx);
        var attrs = new Dictionary<string,string>();
        var cols = new List<GridColumn>();
        if (!m.Success) return (attrs, cols);

        foreach (Match a in AttrRegex.Matches(m.Groups["gvAttrs"].Value))
            attrs[a.Groups["name"].Value] = a.Groups["val"].Value;

        foreach (Match c in ColumnTag.Matches(m.Groups["inner"].Value))
        {
            var col = new GridColumn { Kind = c.Groups["kind"].Value };
            foreach (Match a in AttrRegex.Matches(c.Groups["attrs"].Value))
                col.Attributes[a.Groups["name"].Value] = a.Groups["val"].Value;
            cols.Add(col);
        }
        return (attrs, cols);
    }
}

public static class Mapping
{
    public static string San(string s) => Regex.Replace(s, @"[^A-Za-z0-9_]", "");
    public static string BindName(string? id, string fallback) => San(string.IsNullOrWhiteSpace(id) ? fallback : id!);

    public static string MapControl(AspxControl c)
    {
        c.Attributes.TryGetValue("ID", out var id);
        c.Attributes.TryGetValue("Text", out var text);
        switch (c.TagName.ToLowerInvariant())
        {
            case "label":
                return $"<dot:HtmlTag TagName=\"label\">{text}</dot:HtmlTag>";
            case "textbox":
                return $"<dot:TextBox ID=\"{id}\" Text=\"{{value: {BindName(id, "Text")} }}\" />";
            case "hiddenfield":
                return $"<!-- HiddenField {id} mapped to property -->";
            case "button":
                c.Attributes.TryGetValue("OnClick", out var handler);
                c.Attributes.TryGetValue("Visible", out var vis);
                var visible = string.IsNullOrWhiteSpace(vis) ? "" : $" Visible=\"{{value: {vis.ToLowerInvariant()} }}\"";
                return $"<dot:Button ID=\"{id}\" Text=\"{text}\" Click=\"{{command: {San(handler ?? "Unknown")}()}}\"{visible} />";
            case "validationsummary":
                return "<dot:ValidationSummary />";
            case "requiredfieldvalidator":
                c.Attributes.TryGetValue("ControlToValidate", out var ctl);
                var prop = BindName(ctl, "Field");
                return $"<dot:ValidationMessage For=\"{{value: {prop} }}\" />";
            default:
                return $"<!-- TODO: Unmapped control: {c.TagName} ID={id} -->";
        }
    }
}

public sealed class Conversion
{
    public string View { get; set; } = "";
    public string ViewModel { get; set; } = "";
}

public static class DotvvmGenerator
{
    public static Conversion Generate(string aspx, string ns, string vmClass)
    {
        var controls = AspxParser.ParseControls(aspx).ToList();

        var requiredProps = new Dictionary<string, string>();
        foreach (var v in controls.Where(c => c.TagName.Equals("RequiredFieldValidator", StringComparison.OrdinalIgnoreCase)))
        {
            if (!v.Attributes.TryGetValue("ControlToValidate", out var ctl)) continue;
            var prop = Mapping.BindName(ctl, "Field");
            var msg = v.Attributes.TryGetValue("ErrorMessage", out var em) ? em : $"{prop} is required.";
            requiredProps[prop] = msg;
        }

        var (gridAttrs, columns) = GridParser.Parse(aspx);
        var gridId = gridAttrs.TryGetValue("ID", out var gid) ? gid : "GridView1";
        gridAttrs.TryGetValue("DataKeyNames", out var dataKey);
        var key = string.IsNullOrWhiteSpace(dataKey) ? "ID" : dataKey.Split(',')[0].Trim();

        var sbv = new StringBuilder();
        sbv.AppendLine("@viewModel " + ns + "." + vmClass);
        sbv.AppendLine("<div>");
        foreach (var c in controls)
        {
            if (c.TagName.Equals("GridView", StringComparison.OrdinalIgnoreCase)) continue;
            sbv.AppendLine(Mapping.MapControl(c));
        }

        if (columns.Count > 0)
        {
            sbv.AppendLine($"<dot:GridView DataSource=\"{{value: {Mapping.BindName(gridId, "Grid")}Data}}\">");
            sbv.AppendLine("  <Columns>");
            foreach (var col in columns)
            {
                if (col.Kind.Equals("BoundField", StringComparison.OrdinalIgnoreCase))
                {
                    if (col.Attributes.TryGetValue("DataField", out var df) && col.Attributes.TryGetValue("HeaderText", out var ht))
                    {
                        sbv.AppendLine($"    <dot:GridViewTextColumn ValueBinding=\"{{value: {df}}}\" HeaderText=\"{ht}\" />");
                    }
                }
                else if (col.Kind.Equals("ButtonField", StringComparison.OrdinalIgnoreCase))
                {
                    col.Attributes.TryGetValue("CommandName", out var cmdName);
                    col.Attributes.TryGetValue("Text", out var btxt);
                    var handler = cmdName?.ToLowerInvariant() == "editrow" ? "GridEdit" :
                                  cmdName?.ToLowerInvariant() == "deleterow" ? "GridDelete" : "GridCommand";
                    sbv.AppendLine("    <dot:GridViewTemplateColumn>");
                    sbv.AppendLine("      <ContentTemplate>");
                    sbv.AppendLine($"        <dot:Button Text=\"{btxt}\" Click=\"{{command: {handler}({key})}}\" />");
                    sbv.AppendLine("      </ContentTemplate>");
                    sbv.AppendLine("    </dot:GridViewTemplateColumn>");
                }
            }
            sbv.AppendLine("  </Columns>");
            sbv.AppendLine("</dot:GridView>");
        }
        sbv.AppendLine("</div>");
        var view = sbv.ToString();

        var sbm = new StringBuilder();
        sbm.AppendLine("using DotVVM.Framework.ViewModel;");
        sbm.AppendLine("using System.ComponentModel.DataAnnotations;");
        sbm.AppendLine();
        sbm.AppendLine($"namespace {ns};");
        sbm.AppendLine();
        sbm.AppendLine($"public class {vmClass} : DotvvmViewModelBase");
        sbm.AppendLine("{");
        if (controls.Any(c => c.TagName.Equals("HiddenField", StringComparison.OrdinalIgnoreCase)))
            sbm.AppendLine("    public int? hfEditId { get; set; }");
        foreach (var c in controls.Where(c => c.TagName.Equals("TextBox", StringComparison.OrdinalIgnoreCase)))
        {
            var prop = Mapping.BindName(c.Attributes.TryGetValue("ID", out var id) ? id : "Text", "Text");
            if (requiredProps.TryGetValue(prop, out var msg))
                sbm.AppendLine($"    [Required(ErrorMessage = \"{msg}\")]");
            sbm.AppendLine($"    public string {prop} {{ get; set; }} = string.Empty;");
            sbm.AppendLine();
        }
        sbm.AppendLine($"    public List<{vmClass}Row> GridView1Data {{ get; set; }} = new();");
        sbm.AppendLine();
        sbm.AppendLine("    public void btnSave_Click() { /* TODO */ }");
        sbm.AppendLine("    public void btnCancel_Click() { /* TODO */ }");
        sbm.AppendLine($"    public void GridEdit(int {key}) {{ /* TODO */ }}");
        sbm.AppendLine($"    public void GridDelete(int {key}) {{ /* TODO */ }}");
        sbm.AppendLine();
        sbm.AppendLine($"    public class {vmClass}Row");
        sbm.AppendLine("    {");
        sbm.AppendLine("        public int ID { get; set; }");
        sbm.AppendLine("        public string Name { get; set; } = string.Empty;");
        sbm.AppendLine("        public string Position { get; set; } = string.Empty;");
        sbm.AppendLine("        public string Department { get; set; } = string.Empty;");
        sbm.AppendLine("    }");
        sbm.AppendLine("}");
        var vm = sbm.ToString();

        return new Conversion { View = view, ViewModel = vm };
    }
}
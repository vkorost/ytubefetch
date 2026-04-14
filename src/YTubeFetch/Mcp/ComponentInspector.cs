#if ENABLE_MCP
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace YTubeFetch.Mcp;

/// <summary>
/// Extracts detailed per-type state for a single FrameworkElement.
/// Must be called on the UI thread.
/// </summary>
internal static class ComponentInspector
{
    public static ComponentDetail Inspect(FrameworkElement fe)
    {
        var bounds = TreeWalker.GetBoundsRelativeToWindow(fe);
        string? fg = GetColorHex(fe, Control.ForegroundProperty);
        string? bg = GetColorHex(fe, Control.BackgroundProperty);
        string? tooltip = fe.ToolTip?.ToString();
        string? fontFamily = null;
        double? fontSize = null;

        if (fe is Control ctrl)
        {
            fontFamily = ctrl.FontFamily?.ToString();
            fontSize = ctrl.FontSize;
        }
        else if (fe is TextBlock tb)
        {
            fontFamily = tb.FontFamily?.ToString();
            fontSize = tb.FontSize;
        }

        var extra = ExtractExtra(fe);

        return new ComponentDetail(
            Name: fe.Name ?? string.Empty,
            Type: fe.GetType().Name,
            Bounds: bounds,
            Visible: fe.IsVisible,
            Enabled: fe.IsEnabled,
            Focused: fe.IsFocused,
            Foreground: fg,
            Background: bg,
            Tooltip: tooltip,
            FontFamily: fontFamily,
            FontSize: fontSize,
            Extra: extra.Count > 0 ? extra : null);
    }

    private static Dictionary<string, object?> ExtractExtra(FrameworkElement fe)
    {
        var d = new Dictionary<string, object?>();

        switch (fe)
        {
            case ListView lv:
                ExtractListViewExtra(lv, d);
                break;
            case DataGrid dg:
                ExtractDataGridExtra(dg, d);
                break;
            case TreeView tv:
                ExtractTreeViewExtra(tv, d);
                break;
            case ComboBox cb:
                ExtractComboBoxExtra(cb, d);
                break;
            case TextBox tx:
                ExtractTextBoxExtra(tx, d);
                break;
            case CheckBox chk:
                d["isChecked"] = chk.IsChecked;
                break;
            case RadioButton rb:
                d["isChecked"] = rb.IsChecked;
                break;
            case Slider sl:
                d["value"] = sl.Value;
                d["minimum"] = sl.Minimum;
                d["maximum"] = sl.Maximum;
                d["tickFrequency"] = sl.TickFrequency;
                break;
            case TabControl tc:
                ExtractTabControlExtra(tc, d);
                break;
            case ProgressBar pb:
                d["value"] = pb.Value;
                d["minimum"] = pb.Minimum;
                d["maximum"] = pb.Maximum;
                d["isIndeterminate"] = pb.IsIndeterminate;
                break;
            case ListBox lb when fe is not ComboBox:
                d["selectedIndex"] = lb.SelectedIndex;
                d["itemCount"] = lb.Items.Count;
                break;
            case Menu menu:
                d["items"] = ExtractMenuItems(menu.Items);
                break;
            case MenuItem mi:
                d["header"] = mi.Header?.ToString();
                d["isChecked"] = mi.IsChecked;
                d["isCheckable"] = mi.IsCheckable;
                d["items"] = ExtractMenuItems(mi.Items);
                break;
        }

        return d;
    }

    private static void ExtractListViewExtra(ListView lv, Dictionary<string, object?> d)
    {
        var columns = new List<string>();
        if (lv.View is GridView gv)
        {
            foreach (GridViewColumn col in gv.Columns)
                columns.Add(col.Header?.ToString() ?? string.Empty);
        }
        d["columns"] = columns.ToArray();
        d["selectedIndex"] = lv.SelectedIndex;
        d["itemCount"] = lv.Items.Count;

        var rows = new List<string[]>();
        int maxRows = Math.Min(lv.Items.Count, 50);
        for (int r = 0; r < maxRows; r++)
        {
            var item = lv.Items[r];
            if (item == null) { rows.Add([]); continue; }
            var cells = columns.Count > 0
                ? columns.Select(c => GetPropertyValue(item, c)).ToArray()
                : [item.ToString() ?? string.Empty];
            rows.Add(cells);
        }
        d["items"] = rows.ToArray();
    }

    private static void ExtractDataGridExtra(DataGrid dg, Dictionary<string, object?> d)
    {
        var columns = dg.Columns.Select(c => c.Header?.ToString() ?? string.Empty).ToArray();
        d["columns"] = columns;
        d["selectedIndex"] = dg.SelectedIndex;
        d["itemCount"] = dg.Items.Count;

        var rows = new List<string[]>();
        int maxRows = Math.Min(dg.Items.Count, 50);
        for (int r = 0; r < maxRows; r++)
        {
            var item = dg.Items[r];
            if (item == null) { rows.Add([]); continue; }
            var cells = columns.Select(c => GetPropertyValue(item, c)).ToArray();
            rows.Add(cells);
        }
        d["items"] = rows.ToArray();
    }

    private static void ExtractTreeViewExtra(TreeView tv, Dictionary<string, object?> d)
    {
        var nodes = tv.Items.Cast<object>()
            .Select((item, _) => BuildTreeNode(item as TreeViewItem))
            .ToArray();
        d["nodes"] = nodes;

        string? selectedPath = null;
        if (tv.SelectedItem is TreeViewItem sel)
            selectedPath = sel.Header?.ToString();
        d["selectedPath"] = selectedPath;
    }

    private static Dictionary<string, object?> BuildTreeNode(TreeViewItem? item)
    {
        if (item == null) return new Dictionary<string, object?> { ["header"] = "(null)" };
        var d = new Dictionary<string, object?>
        {
            ["header"] = item.Header?.ToString(),
            ["isExpanded"] = item.IsExpanded,
            ["isSelected"] = item.IsSelected
        };
        if (item.Items.Count > 0)
            d["children"] = item.Items.Cast<object>()
                .Select(c => BuildTreeNode(c as TreeViewItem))
                .ToArray();
        return d;
    }

    private static void ExtractComboBoxExtra(ComboBox cb, Dictionary<string, object?> d)
    {
        var items = cb.Items.Cast<object>()
            .Select(i => i?.ToString() ?? string.Empty)
            .ToArray();
        d["items"] = items;
        d["selectedItem"] = cb.SelectedItem?.ToString();
        d["selectedIndex"] = cb.SelectedIndex;
        d["isEditable"] = cb.IsEditable;
        d["isDropDownOpen"] = cb.IsDropDownOpen;
    }

    private static void ExtractTextBoxExtra(TextBox tx, Dictionary<string, object?> d)
    {
        string text = tx.Text;
        if (text.Length > 500) text = text[..500];
        d["text"] = text;
        d["isReadOnly"] = tx.IsReadOnly;
        d["caretIndex"] = tx.CaretIndex;
        d["selectionStart"] = tx.SelectionStart;
        d["selectionLength"] = tx.SelectionLength;
    }

    private static void ExtractTabControlExtra(TabControl tc, Dictionary<string, object?> d)
    {
        var headers = tc.Items.Cast<object>()
            .Select(i => i is TabItem ti ? ti.Header?.ToString() ?? string.Empty : i?.ToString() ?? string.Empty)
            .ToArray();
        d["tabHeaders"] = headers;
        d["selectedIndex"] = tc.SelectedIndex;
    }

    private static object[] ExtractMenuItems(ItemCollection items)
    {
        var result = new List<object>();
        foreach (var item in items)
        {
            if (item is MenuItem mi)
            {
                var d = new Dictionary<string, object?>
                {
                    ["header"] = mi.Header?.ToString(),
                    ["isChecked"] = mi.IsChecked,
                    ["isCheckable"] = mi.IsCheckable,
                    ["isEnabled"] = mi.IsEnabled
                };
                if (mi.Items.Count > 0)
                    d["items"] = ExtractMenuItems(mi.Items);
                result.Add(d);
            }
            else if (item is Separator)
            {
                result.Add(new Dictionary<string, object?> { ["type"] = "separator" });
            }
        }
        return result.ToArray();
    }

    private static string GetPropertyValue(object item, string propName)
    {
        if (item == null) return string.Empty;
        var prop = item.GetType().GetProperty(propName);
        return prop?.GetValue(item)?.ToString() ?? item.ToString() ?? string.Empty;
    }

    private static string? GetColorHex(FrameworkElement fe, DependencyProperty prop)
    {
        try
        {
            var val = fe.GetValue(prop);
            if (val is SolidColorBrush scb)
                return $"#{scb.Color.R:X2}{scb.Color.G:X2}{scb.Color.B:X2}";
            return null;
        }
        catch { return null; }
    }
}
#endif

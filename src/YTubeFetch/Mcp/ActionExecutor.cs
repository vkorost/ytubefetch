#if ENABLE_MCP
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace YTubeFetch.Mcp;

/// <summary>
/// Executes UI actions on the WPF UI thread and returns the resulting component state.
/// All public methods must be called from the UI thread (inside Dispatcher.Invoke).
/// </summary>
internal static class ActionExecutor
{
    /// <summary>
    /// Executes the requested action synchronously on the UI thread.
    /// Caller is responsible for the 100ms settle delay after this returns.
    /// </summary>
    public static void Execute(ActionRequest req, Window mainWindow)
    {
        var element = ResolveTarget(req.Target, mainWindow)
            ?? throw new InvalidOperationException($"Target not found: {req.Target}");

        switch (req.Action.ToLowerInvariant())
        {
            case "click":
                PerformClick(element);
                break;

            case "type":
                if (element is not TextBox typeBox)
                    throw new InvalidOperationException("'type' action requires a TextBox target");
                typeBox.Text = req.Text ?? string.Empty;
                typeBox.CaretIndex = typeBox.Text.Length;
                typeBox.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent, UndoAction.None));
                break;

            case "clear":
                if (element is not TextBox clearBox)
                    throw new InvalidOperationException("'clear' action requires a TextBox target");
                clearBox.Clear();
                clearBox.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent, UndoAction.None));
                break;

            case "select_combo":
                if (element is not ComboBox combo)
                    throw new InvalidOperationException("'select_combo' action requires a ComboBox target");
                if (req.Index.HasValue)
                {
                    combo.SelectedIndex = req.Index.Value;
                }
                else if (req.Value != null)
                {
                    var match = combo.Items.Cast<object>()
                        .FirstOrDefault(i => string.Equals(i?.ToString(), req.Value, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                        combo.SelectedItem = match;
                    else
                        throw new InvalidOperationException($"ComboBox item not found: {req.Value}");
                }
                break;

            case "select_tab":
                if (element is not TabControl tabCtrl)
                    throw new InvalidOperationException("'select_tab' action requires a TabControl target");
                if (!req.Index.HasValue)
                    throw new InvalidOperationException("'select_tab' requires 'index'");
                tabCtrl.SelectedIndex = req.Index.Value;
                break;

            case "check":
                if (element is not ToggleButton chk)
                    throw new InvalidOperationException("'check' action requires a ToggleButton/CheckBox target");
                chk.IsChecked = true;
                break;

            case "uncheck":
                if (element is not ToggleButton unchk)
                    throw new InvalidOperationException("'uncheck' action requires a ToggleButton/CheckBox target");
                unchk.IsChecked = false;
                break;

            case "select_listitem":
                if (element is not Selector sel)
                    throw new InvalidOperationException("'select_listitem' action requires a ListBox/ListView target");
                if (!req.Index.HasValue)
                    throw new InvalidOperationException("'select_listitem' requires 'index'");
                sel.SelectedIndex = req.Index.Value;
                break;

            case "menu":
                if (req.Path == null)
                    throw new InvalidOperationException("'menu' action requires 'path' (e.g. \"File > Preferences\")");
                PerformMenuAction(req.Path, mainWindow);
                break;

            case "focus":
                element.Focus();
                break;

            case "paste_url":
                PerformPasteUrl(req.Target, req.Url ?? string.Empty, mainWindow);
                break;

            default:
                throw new InvalidOperationException($"Unknown action: {req.Action}");
        }
    }

    private static void PerformClick(FrameworkElement element)
    {
        if (element is ButtonBase btn)
        {
            btn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            return;
        }

        // Try automation peer IInvokeProvider for other elements
        var peer = UIElementAutomationPeer.CreatePeerForElement(element);
        if (peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoker)
        {
            invoker.Invoke();
            return;
        }

        // Fallback: just focus the element
        element.Focus();
    }

    private static void PerformMenuAction(string path, Window mainWindow)
    {
        // path format: "File > Preferences" or "Actions > Open Video Folder"
        var parts = path.Split('>', StringSplitOptions.TrimEntries);
        if (parts.Length == 0) throw new InvalidOperationException("Empty menu path");

        // Find the Menu in the visual tree
        var menu = FindDescendant<Menu>(mainWindow);
        if (menu == null) throw new InvalidOperationException("No Menu found in window");

        ItemCollection items = menu.Items;
        MenuItem? target = null;

        foreach (var part in parts)
        {
            target = null;
            foreach (var item in items)
            {
                if (item is MenuItem mi)
                {
                    string header = mi.Header?.ToString() ?? string.Empty;
                    // Strip WPF access key underscore prefix
                    header = header.Replace("_", string.Empty);
                    if (string.Equals(header, part, StringComparison.OrdinalIgnoreCase))
                    {
                        target = mi;
                        items = mi.Items;
                        break;
                    }
                }
            }
            if (target == null)
                throw new InvalidOperationException($"Menu item not found: {part}");
        }

        target!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    private static void PerformPasteUrl(string targetName, string url, Window mainWindow)
    {
        // YTubeFetcher-specific: set the UrlBar text and trigger Fetch (same as pressing Enter).
        // targetName is used to locate a TextBox; if not found, fall back to "UrlBar".
        var textBox = FindDescendant<TextBox>(mainWindow, targetName)
                      ?? FindDescendant<TextBox>(mainWindow, "UrlBar");

        if (textBox == null)
            throw new InvalidOperationException("UrlBar TextBox not found");

        textBox.Text = url;
        textBox.CaretIndex = url.Length;

        // Simulate pressing Enter: raise KeyDown with Key.Enter, same as UrlBar_KeyDown handler
        var keyArgs = new KeyEventArgs(
            Keyboard.PrimaryDevice,
            PresentationSource.FromVisual(mainWindow)!,
            0,
            Key.Enter)
        {
            RoutedEvent = UIElement.KeyDownEvent
        };
        textBox.RaiseEvent(keyArgs);
    }

    private static FrameworkElement? ResolveTarget(string target, Window mainWindow)
    {
        // Try by integer ID first
        if (int.TryParse(target, out int id))
            return TreeWalker.FindById(mainWindow, id);

        // Try by Name
        return TreeWalker.FindByName(mainWindow, target);
    }

    private static T? FindDescendant<T>(DependencyObject parent, string? name = null)
        where T : FrameworkElement
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T t && (name == null || t.Name == name))
                return t;
            var found = FindDescendant<T>(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
#endif

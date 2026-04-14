#if ENABLE_MCP
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace YTubeFetch.Mcp;

/// <summary>
/// Walks the WPF visual tree from MainWindow and builds a flat array of ComponentNode.
/// Assigns stable integer IDs via element Tag (falls back to GetHashCode if Tag is occupied).
/// </summary>
internal static class TreeWalker
{
    // Global ID counter — monotonically increasing, never reset while process runs.
    private static int _nextId = 1;

    // Map from element identity to stable ID.
    // We use a ConditionalWeakTable so GC'd elements are automatically removed.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, StableId>
        _idMap = new();

    private sealed class StableId { public int Id; }

    // Types considered "interactable" for the default /tree?interactable=true filter.
    private static readonly HashSet<string> InteractableTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Button", "TextBox", "ComboBox", "ListBox", "ListView", "TreeView",
        "CheckBox", "RadioButton", "Slider", "TabControl", "Menu", "MenuItem",
        "ToggleButton", "ProgressBar", "RichTextBox", "PasswordBox"
    };

    // Layout-only container types that are skipped when building the node list
    // (their children are still traversed, inheriting the parent's node ID).
    private static readonly HashSet<Type> SkipTypes = new()
    {
        typeof(Grid), typeof(StackPanel), typeof(DockPanel), typeof(WrapPanel),
        typeof(UniformGrid), typeof(Canvas), typeof(ScrollViewer),
        typeof(ScrollContentPresenter), typeof(ItemsPresenter), typeof(ContentPresenter),
        typeof(AdornerDecorator), typeof(Decorator)
    };

    /// <summary>
    /// Returns a flat list of nodes from the visual tree of <paramref name="root"/>.
    /// Must be called on the UI thread.
    /// </summary>
    /// <param name="interactableOnly">When true, skip elements that are not interactable.</param>
    /// <param name="typeFilter">If non-empty, include only these type names.</param>
    /// <param name="maxCount">Maximum nodes to return.</param>
    public static (List<ComponentNode> Nodes, int TotalFound) BuildTree(
        Window root,
        bool interactableOnly,
        IReadOnlySet<string>? typeFilter,
        int maxCount = 200)
    {
        var nodes = new List<ComponentNode>(64);
        int totalFound = 0;

        Traverse(root, null, nodes, interactableOnly, typeFilter, maxCount, ref totalFound);

        return (nodes, totalFound);
    }

    private static void Traverse(
        DependencyObject element,
        int? parentId,
        List<ComponentNode> nodes,
        bool interactableOnly,
        IReadOnlySet<string>? typeFilter,
        int maxCount,
        ref int totalFound)
    {
        if (element is not FrameworkElement fe)
        {
            // Still traverse children of non-FrameworkElement DOs
            TraverseChildren(element, parentId, nodes, interactableOnly, typeFilter, maxCount, ref totalFound);
            return;
        }

        bool isSkipContainer = IsSkipContainer(fe);
        string typeName = fe.GetType().Name;
        bool matchesType = typeFilter == null || typeFilter.Count == 0 || typeFilter.Contains(typeName);
        bool isInteractable = IsInteractable(fe);
        bool include = !isSkipContainer && matchesType && (!interactableOnly || isInteractable);

        int? myId = null;
        if (include)
        {
            totalFound++;
            if (nodes.Count < maxCount)
            {
                myId = GetOrAssignId(fe);
                var node = BuildNode(fe, typeName, myId.Value, parentId);
                nodes.Add(node);
            }
        }

        // Pass my ID down to children (so children of skipped containers get the right parent).
        int? childParentId = myId ?? parentId;
        TraverseChildren(element, childParentId, nodes, interactableOnly, typeFilter, maxCount, ref totalFound);
    }

    private static void TraverseChildren(
        DependencyObject element,
        int? parentId,
        List<ComponentNode> nodes,
        bool interactableOnly,
        IReadOnlySet<string>? typeFilter,
        int maxCount,
        ref int totalFound)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            Traverse(child, parentId, nodes, interactableOnly, typeFilter, maxCount, ref totalFound);
        }
    }

    private static bool IsSkipContainer(FrameworkElement fe)
    {
        // Named borders are kept (they act as semantic containers).
        if (fe is Border && !string.IsNullOrEmpty(fe.Name))
            return false;

        return SkipTypes.Contains(fe.GetType());
    }

    private static bool IsInteractable(FrameworkElement fe)
    {
        return InteractableTypes.Contains(fe.GetType().Name);
    }

    private static int GetOrAssignId(FrameworkElement fe)
    {
        if (!_idMap.TryGetValue(fe, out var stableId))
        {
            stableId = new StableId { Id = _nextId++ };
            _idMap.AddOrUpdate(fe, stableId);
        }
        return stableId.Id;
    }

    public static FrameworkElement? FindById(Window root, int id)
    {
        FrameworkElement? result = null;
        FindByIdInTree(root, id, ref result);
        return result;
    }

    private static void FindByIdInTree(DependencyObject element, int id, ref FrameworkElement? result)
    {
        if (result != null) return;

        if (element is FrameworkElement fe && _idMap.TryGetValue(fe, out var sid) && sid.Id == id)
        {
            result = fe;
            return;
        }

        int childCount = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < childCount; i++)
            FindByIdInTree(VisualTreeHelper.GetChild(element, i), id, ref result);
    }

    public static FrameworkElement? FindByName(Window root, string name)
    {
        // LogicalTreeHelper is reliable for named elements.
        return root.FindName(name) as FrameworkElement
               ?? FindByNameInVisualTree(root, name);
    }

    private static FrameworkElement? FindByNameInVisualTree(DependencyObject element, string name)
    {
        if (element is FrameworkElement fe && fe.Name == name)
            return fe;

        int childCount = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < childCount; i++)
        {
            var found = FindByNameInVisualTree(VisualTreeHelper.GetChild(element, i), name);
            if (found != null) return found;
        }
        return null;
    }

    private static ComponentNode BuildNode(FrameworkElement fe, string typeName, int id, int? parentId)
    {
        var bounds = GetBoundsRelativeToWindow(fe);
        bool visible = fe.IsVisible;
        bool enabled = fe.IsEnabled;
        bool focused = fe.IsFocused;
        string? text = ExtractText(fe);
        int childCount = VisualTreeHelper.GetChildrenCount(fe);

        return new ComponentNode(
            Id: id,
            Type: typeName,
            Name: fe.Name ?? string.Empty,
            Parent: parentId,
            Bounds: bounds,
            Visible: visible,
            Enabled: enabled,
            Focused: focused,
            Text: text,
            ChildCount: childCount);
    }

    internal static BoundsRect GetBoundsRelativeToWindow(FrameworkElement fe)
    {
        try
        {
            var window = Window.GetWindow(fe);
            if (window == null)
                return new BoundsRect(0, 0, fe.ActualWidth, fe.ActualHeight);

            var transform = fe.TransformToAncestor(window);
            var topLeft = transform.Transform(new Point(0, 0));
            return new BoundsRect(
                Math.Round(topLeft.X, 1),
                Math.Round(topLeft.Y, 1),
                Math.Round(fe.ActualWidth, 1),
                Math.Round(fe.ActualHeight, 1));
        }
        catch
        {
            return new BoundsRect(0, 0, fe.ActualWidth, fe.ActualHeight);
        }
    }

    internal static string? ExtractText(FrameworkElement fe)
    {
        const int maxLen = 200;

        // Use explicit type checks in priority order to avoid unreachable-pattern errors.
        // (ToggleButton, TabItem etc. are subclasses of ContentControl so they must be checked first.)
        string? text = null;

        if (fe is MenuItem mi)
            text = mi.Header?.ToString();
        else if (fe is TabItem ti)
            text = ti.Header?.ToString();
        else if (fe is TextBox tx)
            text = tx.Text;
        else if (fe is TextBlock tb)
            text = tb.Text;
        else if (fe is System.Windows.Controls.ContentControl cc)
            text = cc.Content?.ToString();

        if (text == null)
            text = fe.ToolTip?.ToString();

        if (text != null && text.Length > maxLen)
            text = text[..maxLen];

        return text;
    }
}
#endif

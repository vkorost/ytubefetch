using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace YTubeFetch.Services;

public static class ThemeService
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static bool IsDarkMode { get; private set; }

    // Theme colors as dynamic resources
    public static Color WindowBackground { get; private set; }
    public static Color PanelBackground { get; private set; }
    public static Color PanelBorder { get; private set; }
    public static Color PanelHighlight { get; private set; }
    public static Color DropHighlight { get; private set; }
    public static Color TextPrimary { get; private set; }
    public static Color TextSecondary { get; private set; }
    public static Color MenuBackground { get; private set; }
    public static Color MenuForeground { get; private set; }
    public static Color StatusBarBackground { get; private set; }
    public static Color ListBackground { get; private set; }
    public static Color ListBorder { get; private set; }
    public static Color AccentColor { get; private set; }
    public static Color MenuHover { get; private set; }
    public static Color MenuPressed { get; private set; }
    public static Color MenuBorder { get; private set; }
    public static Color MenuSeparator { get; private set; }
    public static Color ScrollBarTrack { get; private set; }
    public static Color ScrollBarThumb { get; private set; }
    public static Color ScrollBarThumbHover { get; private set; }
    public static Color ScrollBarThumbPressed { get; private set; }
    public static Color ButtonBackground { get; private set; }
    public static Color ButtonBorder { get; private set; }
    public static Color ButtonHover { get; private set; }
    public static Color ButtonPressed { get; private set; }
    public static Color ControlBorder { get; private set; }
    public static Color ControlFocusBorder { get; private set; }
    public static Color ControlBackground { get; private set; }

    public static void Initialize()
    {
        IsDarkMode = DetectDarkMode();
        LogService.Log($"Theme: IsDarkMode={IsDarkMode}");

        if (IsDarkMode)
        {
            WindowBackground = Color.FromRgb(0x1E, 0x1E, 0x1E);
            PanelBackground = Color.FromRgb(0x2D, 0x2D, 0x2D);
            PanelBorder = Color.FromRgb(0x44, 0x44, 0x44);
            PanelHighlight = Color.FromRgb(0x00, 0x78, 0xD4);
            DropHighlight = Color.FromRgb(0x1A, 0x3A, 0x5C);
            TextPrimary = Color.FromRgb(0xE0, 0xE0, 0xE0);
            TextSecondary = Color.FromRgb(0x99, 0x99, 0x99);
            MenuBackground = Color.FromRgb(0x2C, 0x2C, 0x2C);
            MenuForeground = Color.FromRgb(0xE0, 0xE0, 0xE0);
            MenuHover = Color.FromRgb(0x3D, 0x3D, 0x3D);
            MenuPressed = Color.FromRgb(0x33, 0x33, 0x33);
            MenuBorder = Color.FromRgb(0x48, 0x48, 0x48);
            MenuSeparator = Color.FromRgb(0x48, 0x48, 0x48);
            StatusBarBackground = Color.FromRgb(0x25, 0x25, 0x25);
            ListBackground = Color.FromRgb(0x25, 0x25, 0x25);
            ListBorder = Color.FromRgb(0x44, 0x44, 0x44);
            AccentColor = Color.FromRgb(0x00, 0x78, 0xD4);
            ScrollBarTrack = Color.FromRgb(0x25, 0x25, 0x25);
            ScrollBarThumb = Color.FromRgb(0x5A, 0x5A, 0x5A);
            ScrollBarThumbHover = Color.FromRgb(0x7A, 0x7A, 0x7A);
            ScrollBarThumbPressed = Color.FromRgb(0x9A, 0x9A, 0x9A);
            ButtonBackground = Color.FromRgb(0x3D, 0x3D, 0x3D);
            ButtonBorder = Color.FromRgb(0x55, 0x55, 0x55);
            ButtonHover = Color.FromRgb(0x4A, 0x4A, 0x4A);
            ButtonPressed = Color.FromRgb(0x55, 0x55, 0x55);
            ControlBorder = Color.FromRgb(0x55, 0x55, 0x55);
            ControlFocusBorder = Color.FromRgb(0x00, 0x78, 0xD4);
            ControlBackground = Color.FromRgb(0x25, 0x25, 0x25);
        }
        else
        {
            WindowBackground = Color.FromRgb(0xF3, 0xF3, 0xF3);
            PanelBackground = Color.FromRgb(0xF0, 0xF0, 0xF0);
            PanelBorder = Color.FromRgb(0xCC, 0xCC, 0xCC);
            PanelHighlight = Color.FromRgb(0x00, 0x78, 0xD4);
            DropHighlight = Color.FromRgb(0xD0, 0xE8, 0xFF);
            TextPrimary = Color.FromRgb(0x1E, 0x1E, 0x1E);
            TextSecondary = Color.FromRgb(0x88, 0x88, 0x88);
            MenuBackground = Color.FromRgb(0xF9, 0xF9, 0xF9);
            MenuForeground = Color.FromRgb(0x1E, 0x1E, 0x1E);
            MenuHover = Color.FromRgb(0xE9, 0xE9, 0xE9);
            MenuPressed = Color.FromRgb(0xDA, 0xDA, 0xDA);
            MenuBorder = Color.FromRgb(0xE0, 0xE0, 0xE0);
            MenuSeparator = Color.FromRgb(0xD6, 0xD6, 0xD6);
            StatusBarBackground = Color.FromRgb(0xE8, 0xE8, 0xE8);
            ListBackground = Colors.White;
            ListBorder = Color.FromRgb(0xCC, 0xCC, 0xCC);
            AccentColor = Color.FromRgb(0x00, 0x78, 0xD4);
            ScrollBarTrack = Color.FromRgb(0xF5, 0xF5, 0xF5);
            ScrollBarThumb = Color.FromRgb(0xC1, 0xC1, 0xC1);
            ScrollBarThumbHover = Color.FromRgb(0xA8, 0xA8, 0xA8);
            ScrollBarThumbPressed = Color.FromRgb(0x8A, 0x8A, 0x8A);
            ButtonBackground = Color.FromRgb(0xFD, 0xFD, 0xFD);
            ButtonBorder = Color.FromRgb(0xD0, 0xD0, 0xD0);
            ButtonHover = Color.FromRgb(0xF5, 0xF5, 0xF5);
            ButtonPressed = Color.FromRgb(0xE8, 0xE8, 0xE8);
            ControlBorder = Color.FromRgb(0xCC, 0xCC, 0xCC);
            ControlFocusBorder = Color.FromRgb(0x00, 0x78, 0xD4);
            ControlBackground = Colors.White;
        }

        ApplyToAppResources();
    }

    public static void ApplyDarkTitleBar(Window window)
    {
        if (!IsDarkMode) return;

        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
            {
                int value = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
            }
        }
        catch (Exception ex)
        {
            LogService.Log($"Failed to apply dark title bar: {ex.Message}");
        }
    }

    private static void ApplyToAppResources()
    {
        var res = Application.Current.Resources;

        res["WindowBackgroundBrush"] = new SolidColorBrush(WindowBackground);
        res["PanelBackgroundBrush"] = new SolidColorBrush(PanelBackground);
        res["PanelBorderBrush"] = new SolidColorBrush(PanelBorder);
        res["PanelHighlightBrush"] = new SolidColorBrush(PanelHighlight);
        res["DropHighlightBrush"] = new SolidColorBrush(DropHighlight);
        res["TextPrimaryBrush"] = new SolidColorBrush(TextPrimary);
        res["TextSecondaryBrush"] = new SolidColorBrush(TextSecondary);
        res["MenuBackgroundBrush"] = new SolidColorBrush(MenuBackground);
        res["MenuForegroundBrush"] = new SolidColorBrush(MenuForeground);
        res["MenuHoverBrush"] = new SolidColorBrush(MenuHover);
        res["MenuPressedBrush"] = new SolidColorBrush(MenuPressed);
        res["MenuBorderBrush"] = new SolidColorBrush(MenuBorder);
        res["MenuSeparatorBrush"] = new SolidColorBrush(MenuSeparator);
        res["StatusBarBackgroundBrush"] = new SolidColorBrush(StatusBarBackground);
        res["ListBackgroundBrush"] = new SolidColorBrush(ListBackground);
        res["ListBorderBrush"] = new SolidColorBrush(ListBorder);
        res["AccentBrush"] = new SolidColorBrush(AccentColor);
        res["ScrollBarTrackBrush"] = new SolidColorBrush(ScrollBarTrack);
        res["ScrollBarThumbBrush"] = new SolidColorBrush(ScrollBarThumb);
        res["ScrollBarThumbHoverBrush"] = new SolidColorBrush(ScrollBarThumbHover);
        res["ScrollBarThumbPressedBrush"] = new SolidColorBrush(ScrollBarThumbPressed);
        res["ButtonBackgroundBrush"] = new SolidColorBrush(ButtonBackground);
        res["ButtonBorderBrush"] = new SolidColorBrush(ButtonBorder);
        res["ButtonHoverBrush"] = new SolidColorBrush(ButtonHover);
        res["ButtonPressedBrush"] = new SolidColorBrush(ButtonPressed);
        res["ControlBorderBrush"] = new SolidColorBrush(ControlBorder);
        res["ControlFocusBorderBrush"] = new SolidColorBrush(ControlFocusBorder);
        res["ControlBackgroundBrush"] = new SolidColorBrush(ControlBackground);

        res["WindowBackgroundColor"] = WindowBackground;
        res["PanelBackgroundColor"] = PanelBackground;
    }

    private static bool DetectDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var val = key?.GetValue("AppsUseLightTheme");
            if (val is int intVal)
                return intVal == 0;
        }
        catch { }
        return false;
    }
}

using System.Windows;
using YTubeFetch.Services;

namespace YTubeFetch.Views;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
        ThemeService.ApplyDarkTitleBar(this);
    }
}

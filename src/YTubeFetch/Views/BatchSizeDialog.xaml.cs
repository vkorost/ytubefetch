using System.Windows;
using System.Windows.Controls;
using YTubeFetch.Services;

namespace YTubeFetch.Views;

public partial class BatchSizeDialog : Window
{
    private readonly int _totalCount;

    /// <summary>
    /// The selected limit. 0 means "All", null means cancelled.
    /// </summary>
    public int? SelectedLimit { get; private set; }

    public BatchSizeDialog(int totalCount)
    {
        InitializeComponent();
        ThemeService.ApplyDarkTitleBar(this);
        _totalCount = totalCount;
        MessageText.Text = $"This playlist/channel has {totalCount} videos.\nHow many do you want to download?";
        AllWarning.Text = $"Downloading all {totalCount} videos may take a long time.";
    }

    private void LimitCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AllWarning == null) return;
        var item = LimitCombo.SelectedItem as ComboBoxItem;
        AllWarning.Visibility = item?.Content?.ToString() == "All"
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var item = LimitCombo.SelectedItem as ComboBoxItem;
        var text = item?.Content?.ToString() ?? "50";

        SelectedLimit = text == "All" ? 0 : int.TryParse(text, out int n) ? n : 50;
        DialogResult = true;
        Close();
    }
}

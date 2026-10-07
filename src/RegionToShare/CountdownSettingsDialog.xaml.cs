using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TomsToolbox.Wpf.Styles;

namespace RegionToShare;

public partial class CountdownSettingsDialog : Window
{
    public string CountdownTitle { get; private set; } = string.Empty;
    public string CountdownStartTime { get; private set; } = "12:00";
    public string CountdownTextColor { get; private set; } = "#FFFFFF";
    public string CountdownFontFamily { get; private set; } = "Consolas";

    public CountdownSettingsDialog(string initialTitle, string initialStartTime, string initialTextColor, string initialFontFamily)
    {
        Resources.RegisterDefaultStyles();
        InitializeComponent();

        for (var h = 0; h < 24; h++)
        {
            HoursComboBox.Items.Add(h.ToString("D2", CultureInfo.InvariantCulture));
        }

        for (var m = 0; m < 60; m++)
        {
            MinutesComboBox.Items.Add(m.ToString("D2", CultureInfo.InvariantCulture));
        }

        TitleTextBox.Text = string.IsNullOrEmpty(initialTitle) ? "Title of session" : initialTitle;

        var parts = (initialStartTime ?? "12:00").Split(':');
        var hour = parts.Length > 0 ? parts[0].PadLeft(2, '0') : "12";
        var minute = parts.Length > 1 ? parts[1].PadLeft(2, '0') : "00";

        HoursComboBox.SelectedItem = HoursComboBox.Items.Cast<string>().FirstOrDefault(i => i == hour) ?? "12";
        MinutesComboBox.SelectedItem = MinutesComboBox.Items.Cast<string>().FirstOrDefault(i => i == minute) ?? "00";

        ColorTextBox.Text = string.IsNullOrWhiteSpace(initialTextColor) ? "#FFFFFF" : initialTextColor;
        UpdateColorPreview(ColorTextBox.Text);

        var popularFonts = new[]
        {
            "Consolas",
            "Segoe UI",
            "Arial",
            "Calibri",
            "Courier New",
            "Lucida Console",
            "Tahoma",
            "Trebuchet MS",
            "Verdana"
        };

        foreach (var font in popularFonts)
        {
            FontFamilyComboBox.Items.Add(font);
        }

        var selectedFont = string.IsNullOrWhiteSpace(initialFontFamily) ? "Consolas" : initialFontFamily;
        FontFamilyComboBox.SelectedItem = FontFamilyComboBox.Items.Cast<string>().FirstOrDefault(f => string.Equals(f, selectedFont, StringComparison.OrdinalIgnoreCase)) ?? "Consolas";
    }

    private void ColorTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateColorPreview(ColorTextBox.Text);
    }

    private void UpdateColorPreview(string hex)
    {
        try
        {
            var cleaned = hex?.Trim() ?? "";
            if (!cleaned.StartsWith("#"))
                cleaned = "#" + cleaned;

            if (cleaned.Length == 7)
            {
                var color = (Color)ColorConverter.ConvertFromString(cleaned);
                ColorPreviewBorder.Background = new SolidColorBrush(color);
                return;
            }
        }
        catch
        {
            // Invalid color
        }

        ColorPreviewBorder.Background = Brushes.Transparent;
    }

    private void ColorPickerButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.ColorDialog();
        dialog.AllowFullOpen = true;

        try
        {
            var cleaned = ColorTextBox.Text?.Trim() ?? "";
            if (!cleaned.StartsWith("#"))
                cleaned = "#" + cleaned;
            var wpfColor = (Color)ColorConverter.ConvertFromString(cleaned);
            dialog.Color = System.Drawing.Color.FromArgb(wpfColor.R, wpfColor.G, wpfColor.B);
        }
        catch
        {
            dialog.Color = System.Drawing.Color.White;
        }

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var c = dialog.Color;
            ColorTextBox.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        CountdownTitle = TitleTextBox.Text.Trim();
        var hour = HoursComboBox.SelectedItem as string ?? "12";
        var minute = MinutesComboBox.SelectedItem as string ?? "00";
        CountdownStartTime = $"{hour}:{minute}";

        var color = ColorTextBox.Text.Trim();
        if (!color.StartsWith("#"))
            color = "#" + color;

        try
        {
            ColorConverter.ConvertFromString(color);
            CountdownTextColor = color.ToUpperInvariant();
        }
        catch
        {
            CountdownTextColor = "#FFFFFF";
        }

        CountdownFontFamily = FontFamilyComboBox.SelectedItem as string ?? "Consolas";

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

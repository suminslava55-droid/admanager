using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AdManager.Common;
using AdManager.Models;

namespace AdManager.Dialogs;

public partial class ExpirationWindow : Window
{
    public ExpirationChoice Choice { get; } = new() { Canceled = true };

    public ExpirationWindow(DateTime? current)
    {
        InitializeComponent();
        WindowChromeHelper.EnableDarkTitleBar(this);
        CurrentText.Text = current.HasValue
            ? "Сейчас: " + AdValue.ToLocalText(current)
            : "Сейчас: срок не ограничен";

        if (current.HasValue)
        {
            DateRadio.IsChecked = true;
            DateBox.Text = current.Value.ToLocalTime().ToString("dd.MM.yyyy", CultureInfo.CurrentCulture);
        }
        else
        {
            NeverRadio.IsChecked = true;
        }

        Loaded += (_, _) => DateBox.IsEnabled = DateRadio.IsChecked == true;
    }

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (DateBox is null) return;
        DateBox.IsEnabled = DateRadio.IsChecked == true;
    }

    private void Quick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        int days = int.TryParse(button.Tag as string, out int d) ? d : 30;
        DateRadio.IsChecked = true;
        DateBox.Text = DateTime.Today.AddDays(days).ToString("dd.MM.yyyy", CultureInfo.CurrentCulture);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Choice.Canceled = false;

        if (NeverRadio.IsChecked == true)
        {
            Choice.Expiration = null;
            DialogResult = true;
            return;
        }

        string text = DateBox.Text.Trim();
        if (!DateTime.TryParseExact(text, "dd.MM.yyyy", CultureInfo.CurrentCulture,
                DateTimeStyles.None, out DateTime date) &&
            !DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out date))
        {
            ErrorText.Text = "Не удалось разобрать дату. Укажите в формате дд.ММ.гггг, например 31.12.2026.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        Choice.Expiration = date.Date.AddHours(23).AddMinutes(59);
        DialogResult = true;
    }
}

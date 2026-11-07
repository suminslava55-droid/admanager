using System.Windows;
using System.Windows.Media;
using AdManager.Common;

namespace AdManager.Dialogs;

public partial class MessageWindow : Window
{
    public bool Confirmed { get; private set; }

    public MessageWindow(string title, string message, bool confirm, bool isError)
    {
        InitializeComponent();
        WindowChromeHelper.EnableDarkTitleBar(this);
        Title = title;
        HeaderText.Text = title;
        MessageText.Text = message;
        CancelButton.Visibility = confirm ? Visibility.Visible : Visibility.Collapsed;
        OkButton.Content = confirm ? "Да" : "ОК";
        if (confirm)
        {
            CancelButton.Content = "Нет";
        }

        if (isError)
        {
            IconBorder.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x21, 0x1F));
            IconText.Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0x87, 0x71));
            IconText.Text = "!";
        }
        else if (confirm)
        {
            IconBorder.Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x2E, 0x18));
            IconText.Foreground = new SolidColorBrush(Color.FromRgb(0xDB, 0xCD, 0x78));
            IconText.Text = "?";
        }

        Loaded += (_, _) => OkButton.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
    }
}

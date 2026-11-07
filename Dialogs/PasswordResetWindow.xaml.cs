using System.Windows;
using AdManager.Common;
using AdManager.Models;
using AdManager.Services;

namespace AdManager.Dialogs;

public partial class PasswordResetWindow : Window
{
    private readonly AdUser _user;
    private readonly int _passwordLength;

    public PasswordResetOptions? Options { get; private set; }

    public PasswordResetWindow(AdUser user, int passwordLength)
    {
        InitializeComponent();
        WindowChromeHelper.EnableDarkTitleBar(this);
        _user = user;
        _passwordLength = passwordLength;
        TargetText.Text = $"{user.DisplayLabel} · {user.SamAccountName} · {user.StatusText}";
        UnlockCheck.IsChecked = user.Locked;
        UnlockCheck.Visibility = user.Locked ? Visibility.Visible : Visibility.Collapsed;
        PasswordBox.Text = PasswordGenerator.Generate(passwordLength);
        Loaded += (_, _) =>
        {
            PasswordBox.Focus();
            PasswordBox.SelectAll();
        };
    }

    public bool UnlockRequested => UnlockCheck.IsChecked == true;

    private void Generate_Click(object sender, RoutedEventArgs e)
    {
        PasswordBox.Text = PasswordGenerator.Generate(_passwordLength);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string password = PasswordBox.Text;
        if (password.Length < 7)
        {
            ErrorText.Text = "Пароль должен содержать не менее 7 символов (обычно требуется 8+ и сложность по политике домена).";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        Options = new PasswordResetOptions
        {
            Password = password,
            MustChange = MustChangeCheck.IsChecked == true
        };
        DialogResult = true;
    }
}

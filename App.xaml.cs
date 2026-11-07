using System.Windows;
using System.Windows.Threading;
using System.Diagnostics;
using AdManager.Common;
using AdManager.Services;
using AdManager.ViewModels;

namespace AdManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var settings = new SettingsService();
        var audit = new AuditLogService();
        var dialogs = new DialogService();
        var viewModel = new MainViewModel(settings, dialogs, audit);

        var window = new MainWindow
        {
            DataContext = viewModel
        };

        double width = settings.Current.WindowWidth;
        double height = settings.Current.WindowHeight;
        if (width >= 1000) window.Width = width;
        if (height >= 650) window.Height = height;

        window.Closing += (_, _) => viewModel.SaveWindowSize(window.ActualWidth, window.ActualHeight);

        dialogs.SetOwner(window);
        MainWindow = window;
        window.Show();

        if (!ElevationHelper.IsAdministrator &&
            !settings.Current.UseDemo &&
            !Debugger.IsAttached &&
            (!string.IsNullOrWhiteSpace(settings.Current.Domain) || ElevationHelper.IsDomainEnvironment))
        {
            bool elevate = dialogs.Confirm(
                "Права администратора",
                "AdManager запущен без прав администратора.\n\n" +
                "В этом режиме Active Directory может не отдавать часть атрибутов: статус УЗ, членство в группах, сроки паролей.\n\n" +
                "Перезапустить приложение с повышением прав?");

            if (elevate)
            {
                if (ElevationHelper.TryRestartElevated(out bool canceled))
                {
                    window.Close();
                    return;
                }

                if (!canceled)
                {
                    dialogs.Error("Права администратора",
                        "Не удалось перезапустить приложение с повышением прав. Продолжаем в обычном режиме.");
                }
            }
        }

        _ = viewModel.InitializeAsync();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            "Непредвиденная ошибка:\n\n" + e.Exception.Message +
            "\n\nПодробности можно найти в журнале Windows.",
            "AdManager",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}

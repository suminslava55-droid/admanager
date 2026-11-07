using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Security.Principal;

namespace AdManager.Common;

/// <summary>Проверка прав Windows и перезапуск приложения с повышением прав (UAC).</summary>
public static class ElevationHelper
{
    public static bool IsAdministrator { get; } = CheckAdministrator();

    public static bool IsDomainEnvironment
    {
        get
        {
            try
            {
                return !string.IsNullOrWhiteSpace(IPGlobalProperties.GetIPGlobalProperties().DomainName);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Перезапускает текущий исполняемый файл через UAC.
    /// Возвращает true, если новый процесс запущен.
    /// </summary>
    public static bool TryRestartElevated(out bool canceledByUser)
    {
        canceledByUser = false;
        try
        {
            string? executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
            {
                executable = Process.GetCurrentProcess().MainModule?.FileName;
            }

            if (string.IsNullOrWhiteSpace(executable))
            {
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            };

            string[] args = Environment.GetCommandLineArgs().Skip(1).ToArray();
            if (args.Length > 0)
            {
                startInfo.Arguments = string.Join(" ", args.Select(Quote));
            }

            Process.Start(startInfo);
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            canceledByUser = true;
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool CheckAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static string Quote(string value)
    {
        if (value.Length == 0) return "\"\"";
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}

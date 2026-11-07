using System.Net.NetworkInformation;
using AdManager.Models;

namespace AdManager.Services;

public static class AdServiceFactory
{
    public static IAdDirectoryService Create(AppSettings settings)
    {
        if (settings.UseDemo || !CanUseLiveDirectory(settings))
        {
            return new DemoAdDirectoryService();
        }

        return new LiveAdDirectoryService(settings.PageSize);
    }

    /// <summary>
    /// Быстрая проверка: домен задан вручную или компьютер введён в DNS-домен.
    /// Не использует Domain.GetCurrentDomain() — он может блокировать поток на машине вне домена.
    /// </summary>
    public static bool CanUseLiveDirectory(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Domain) || !string.IsNullOrWhiteSpace(settings.Server))
        {
            return true;
        }

        try
        {
            string dnsDomain = IPGlobalProperties.GetIPGlobalProperties().DomainName;
            return !string.IsNullOrWhiteSpace(dnsDomain);
        }
        catch
        {
            return false;
        }
    }
}

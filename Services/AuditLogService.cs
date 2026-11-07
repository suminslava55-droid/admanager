using System.IO;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using AdManager.Models;

namespace AdManager.Services;

public sealed class AuditLogService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private readonly object _sync = new();
    private readonly string _directory;

    public string Directory => _directory;

    public AuditLogService()
    {
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AdManager",
            "audit");
        System.IO.Directory.CreateDirectory(_directory);
    }

    public void Write(string action, string target, string details, bool success = true, string? error = null)
    {
        var entry = new AuditEntry
        {
            Timestamp = DateTime.Now,
            Operator = CurrentOperator(),
            Action = action,
            Target = target,
            Details = details,
            Result = success ? "OK" : "ERROR",
            Error = error
        };

        try
        {
            string file = Path.Combine(_directory, DateTime.Now.ToString("yyyy-MM") + ".jsonl");
            string line = JsonSerializer.Serialize(entry, JsonOptions);
            lock (_sync)
            {
                File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Аудит не должен ломать операцию.
        }
    }

    private static string CurrentOperator()
    {
        try
        {
            return WindowsIdentity.GetCurrent().Name;
        }
        catch
        {
            return Environment.UserName;
        }
    }
}

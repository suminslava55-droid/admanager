using System.IO;
using System.Net;
using System.Text;
using AdManager.Models;

namespace AdManager.Services;

public static class ExportService
{
    public static void ExportUsersCsv(string path, IEnumerable<AdUser> users)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Имя;Логин;UPN;Статус;Привилегированная;Привилегированные группы;Риски;Отдел;Должность;Руководитель;Почта;Последний вход;Пароль установлен;Пароль истекает;Срок УЗ;Групп;OU;DN");
        foreach (var u in users)
        {
            sb.Append(Csv(u.DisplayLabel)).Append(';')
              .Append(Csv(u.SamAccountName)).Append(';')
              .Append(Csv(u.UserPrincipalName)).Append(';')
              .Append(Csv(u.StatusText)).Append(';')
              .Append(Csv(u.IsPrivileged ? "да" : "нет")).Append(';')
              .Append(Csv(string.Join(", ", u.PrivilegedGroups))).Append(';')
              .Append(Csv(string.Join(", ", u.RiskFlags.Select(f => f.Name)))).Append(';')
              .Append(Csv(u.Department)).Append(';')
              .Append(Csv(u.Title)).Append(';')
              .Append(Csv(u.Manager)).Append(';')
              .Append(Csv(u.Mail)).Append(';')
              .Append(Csv(u.LastLogonText)).Append(';')
              .Append(Csv(u.PasswordLastSetText)).Append(';')
              .Append(Csv(u.PasswordExpiresText)).Append(';')
              .Append(Csv(u.AccountExpiresText)).Append(';')
              .Append(Csv(u.GroupsCountText)).Append(';')
              .Append(Csv(u.OuDisplay)).Append(';')
              .Append(Csv(u.DistinguishedName))
              .AppendLine();
        }

        WriteUtf8(path, sb.ToString());
    }

    public static void ExportGroupsCsv(string path, IEnumerable<AdGroup> groups)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Группа;Логин;Тип;Область;Привилегированная;Участников;Владелец;Риски;Описание;OU;DN");
        foreach (var g in groups)
        {
            sb.Append(Csv(g.Name)).Append(';')
              .Append(Csv(g.SamAccountName)).Append(';')
              .Append(Csv(g.IsSecurity ? "Безопасность" : "Рассылка")).Append(';')
              .Append(Csv(g.GroupScopeText)).Append(';')
              .Append(Csv(g.IsPrivileged ? "да" : "нет")).Append(';')
              .Append(Csv(g.MemberCountText)).Append(';')
              .Append(Csv(g.ManagedBy)).Append(';')
              .Append(Csv(string.Join(", ", g.RiskFlags.Select(f => f.Name)))).Append(';')
              .Append(Csv(g.Description)).Append(';')
              .Append(Csv(g.OuDisplay)).Append(';')
              .Append(Csv(g.DistinguishedName))
              .AppendLine();
        }

        WriteUtf8(path, sb.ToString());
    }

    public static void ExportIssuesCsv(string path, IEnumerable<ReviewIssue> issues)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Важность;Категория;Тип объекта;Объект;Логин;Детали;DN");
        foreach (var issue in issues)
        {
            sb.Append(Csv(issue.SeverityText)).Append(';')
              .Append(Csv(issue.Category)).Append(';')
              .Append(Csv(issue.ObjectType)).Append(';')
              .Append(Csv(issue.ObjectName)).Append(';')
              .Append(Csv(issue.SamAccountName)).Append(';')
              .Append(Csv(issue.Details)).Append(';')
              .Append(Csv(issue.ObjectDn))
              .AppendLine();
        }

        WriteUtf8(path, sb.ToString());
    }

    public static void ExportHtml(
        string path,
        string domain,
        ReviewResult review,
        IReadOnlyCollection<AdUser> users,
        IReadOnlyCollection<AdGroup> groups)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html lang=\"ru\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<title>Аудит доступов — " + H(domain) + "</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;margin:28px;color:#222;background:#fff}");
        sb.AppendLine("h1{font-size:24px;margin:0 0 4px}h2{font-size:18px;margin:28px 0 10px;border-bottom:1px solid #ddd;padding-bottom:6px}");
        sb.AppendLine(".muted{color:#666;font-size:12px}.cards{display:flex;flex-wrap:wrap;gap:10px;margin:18px 0}");
        sb.AppendLine(".card{border:1px solid #ddd;border-radius:8px;padding:10px 14px;min-width:120px;background:#fafafa}");
        sb.AppendLine(".card b{display:block;font-size:22px;line-height:1.1}.card span{font-size:12px;color:#666}");
        sb.AppendLine("table{border-collapse:collapse;width:100%;font-size:12px}th,td{border:1px solid #ddd;padding:5px 7px;text-align:left;vertical-align:top}");
        sb.AppendLine("th{background:#f3f3f3;position:sticky;top:0}tr:nth-child(even){background:#fafafa}");
        sb.AppendLine(".critical{color:#b00020;font-weight:600}.high{color:#b35c00;font-weight:600}.medium{color:#7a6400}.low{color:#24608c}");
        sb.AppendLine("code{font-family:Consolas,monospace;font-size:11px}");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine("<h1>Аудит доступов Active Directory</h1>");
        sb.AppendLine($"<div class=\"muted\">Домен: {H(domain)} · сформировано: {DateTime.Now:dd.MM.yyyy HH:mm}</div>");

        var s = review.Summary;
        sb.AppendLine("<div class=\"cards\">");
        Card(sb, s.TotalUsers, "Пользователей");
        Card(sb, s.PrivilegedUsers, "Привилегированных");
        Card(sb, s.StaleUsers, "Неактивных");
        Card(sb, s.NeverLoggedIn, "Никогда не входили");
        Card(sb, s.DisabledWithAccess, "Отключены, но есть доступ");
        Card(sb, s.SpnUsers, "С SPN");
        Card(sb, s.AsRepUsers, "AS-REP");
        Card(sb, s.DelegationUsers, "Делегирование");
        Card(sb, s.TotalGroups, "Групп");
        Card(sb, s.EmptyGroups, "Пустых групп");
        sb.AppendLine("</div>");

        sb.AppendLine("<h2>Проблемы</h2>");
        sb.AppendLine("<table><tr><th>Важность</th><th>Категория</th><th>Объект</th><th>Логин</th><th>Детали</th></tr>");
        foreach (var issue in review.Issues)
        {
            string cls = issue.Severity switch
            {
                RiskSeverity.Critical => "critical",
                RiskSeverity.High => "high",
                RiskSeverity.Medium => "medium",
                _ => "low"
            };
            sb.AppendLine($"<tr><td class=\"{cls}\">{H(issue.SeverityText)}</td><td>{H(issue.Category)}</td><td>{H(issue.ObjectName)}</td><td>{H(issue.SamAccountName)}</td><td>{H(issue.Details)}</td></tr>");
        }
        sb.AppendLine("</table>");

        sb.AppendLine("<h2>Привилегированные пользователи</h2>");
        sb.AppendLine("<table><tr><th>Имя</th><th>Логин</th><th>Группы</th><th>Последний вход</th><th>Статус</th></tr>");
        foreach (var u in users.Where(u => u.IsPrivileged).OrderBy(u => u.DisplayLabel))
        {
            sb.AppendLine($"<tr><td>{H(u.DisplayLabel)}</td><td>{H(u.SamAccountName)}</td><td>{H(string.Join(", ", u.PrivilegedGroups))}</td><td>{H(u.LastLogonText)}</td><td>{H(u.StatusText)}</td></tr>");
        }
        sb.AppendLine("</table>");

        sb.AppendLine("<h2>Пользователи</h2>");
        sb.AppendLine("<table><tr><th>Имя</th><th>Логин</th><th>Статус</th><th>Отдел</th><th>Должность</th><th>Последний вход</th><th>Пароль</th><th>Риски</th><th>OU</th><th>DN</th></tr>");
        foreach (var u in users.OrderBy(u => u.DisplayLabel))
        {
            sb.AppendLine($"<tr><td>{H(u.DisplayLabel)}</td><td>{H(u.SamAccountName)}</td><td>{H(u.StatusText)}</td><td>{H(u.Department)}</td><td>{H(u.Title)}</td><td>{H(u.LastLogonText)}</td><td>{H(u.PasswordLastSetText)}</td><td>{H(string.Join(", ", u.RiskFlags.Select(f => f.Name)))}</td><td>{H(u.OuDisplay)}</td><td><code>{H(u.DistinguishedName)}</code></td></tr>");
        }
        sb.AppendLine("</table>");

        sb.AppendLine("<h2>Группы</h2>");
        sb.AppendLine("<table><tr><th>Группа</th><th>Тип</th><th>Участников</th><th>Владелец</th><th>Привилегированная</th><th>Риски</th><th>DN</th></tr>");
        foreach (var g in groups.OrderBy(g => g.Name))
        {
            sb.AppendLine($"<tr><td>{H(g.Name)}</td><td>{H(g.GroupTypeText)}</td><td>{g.MemberCount}</td><td>{H(g.ManagedBy)}</td><td>{(g.IsPrivileged ? "да" : "—")}</td><td>{H(string.Join(", ", g.RiskFlags.Select(f => f.Name)))}</td><td><code>{H(g.DistinguishedName)}</code></td></tr>");
        }
        sb.AppendLine("</table>");
        sb.AppendLine("</body></html>");

        WriteUtf8(path, sb.ToString());
    }

    private static void Card(StringBuilder sb, int value, string label)
        => sb.AppendLine($"<div class=\"card\"><b>{value}</b><span>{H(label)}</span></div>");

    private static void WriteUtf8(string path, string content)
        => File.WriteAllText(path, content, new UTF8Encoding(true));

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

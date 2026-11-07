namespace AdManager.Models;

/// <summary>Каталог проблем access review, которые можно включать/выключать в настройках.</summary>
public static class ProblemCatalog
{
    public static IReadOnlyList<string> UserProblems { get; } = new[]
    {
        "Привилегированная УЗ",
        "Привилегированная и неактивная",
        "adminCount=1",
        "SPN (Kerberoastable)",
        "AS-REP (без preauth)",
        "Unconstrained delegation",
        "Constrained delegation",
        "Protocol transition",
        "SID History",
        "Пароль не истекает",
        "Пароль не требуется",
        "DES only",
        "No auth data required",
        "Неактивная УЗ",
        "Отключена, но в группах",
        "Срок действия истёк",
        "Пароль истёк",
        "Требуется смена пароля",
        "Пароль скоро истекает"
    };

    public static IReadOnlyList<string> GroupProblems { get; } = new[]
    {
        "Привилегированная группа",
        "Вложена в привилегированную",
        "Привилегированная без владельца",
        "Пустая группа",
        "Без владельца"
    };

    public static IEnumerable<string> All => UserProblems.Concat(GroupProblems);
}

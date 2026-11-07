using System.DirectoryServices;
using System.Runtime.InteropServices;

namespace AdManager.Services;

public sealed class AdOperationException : Exception
{
    public AdOperationException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }

    public static AdOperationException Wrap(Exception ex, string action)
    {
        if (ex is AdOperationException adEx)
        {
            return adEx;
        }

        string message = ex switch
        {
            DirectoryServicesCOMException ds => Friendly(ds),
            COMException com => Friendly(com),
            UnauthorizedAccessException => "Недостаточно прав для этой операции. Проверьте делегирование в AD.",
            _ => ex.Message
        };

        return new AdOperationException($"{action}: {message}", ex);
    }

    private static string Friendly(DirectoryServicesCOMException ex)
    {
        int code = ex.ErrorCode;
        string extended = ex.ExtendedErrorMessage?.Trim() ?? string.Empty;
        string baseMessage = code switch
        {
            unchecked((int)0x80070005) => "Доступ запрещён (нет прав на объект).",
            unchecked((int)0x800708C5) => "Пароль не соответствует политике домена.",
            unchecked((int)0x80071392) => "Объект с таким именем уже существует.",
            unchecked((int)0x8007052E) => "Неверное имя пользователя или пароль.",
            unchecked((int)0x80072030) => "Объект не найден (возможно, перемещён или удалён).",
            unchecked((int)0x80072035) => "Контроллер домена отклонил изменение. Повторите позже.",
            _ => ex.Message
        };

        return string.IsNullOrWhiteSpace(extended) ? baseMessage : $"{baseMessage} {extended}";
    }

    private static string Friendly(COMException ex)
    {
        return ex.HResult switch
        {
            unchecked((int)0x80070005) => "Доступ запрещён (нет прав на объект).",
            unchecked((int)0x800708C5) => "Пароль не соответствует политике домена.",
            unchecked((int)0x80071392) => "Объект с таким именем уже существует.",
            unchecked((int)0x80072030) => "Объект не найден.",
            _ => ex.Message
        };
    }
}

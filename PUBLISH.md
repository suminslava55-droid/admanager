# Как выложить AdManager на GitHub

## Что готово

- `AdManager-upload\` — очищенная копия проекта для загрузки в репозиторий.
  Внутри только исходники, ресурсы, документация и `.gitignore`.
  Нет `bin`, `obj`, `publish`, exe и пользовательских настроек.
- `AdManager-release\AdManager-1.0.0-win64.zip` — архив для раздела **Releases**
  (внутри `AdManager.exe`, `README.md`, `LICENSE`, `CHANGELOG.md`).
- Готовый exe: `publish\AdManager.exe` (self-contained, .NET 8 не требуется).

В репозиторий **не должны** попасть:

- `bin\`, `obj\`, `publish\`;
- `*.exe`, `*.pdb`;
- пользовательские `settings.json` и журналы (они лежат в `%APPDATA%`, не в проекте).

Это уже исключено в `.gitignore`.

---

## Вариант А. Через браузер, без установки git

1. Откройте https://github.com/new
   - Repository name: `admanager`
   - Public
   - **Ничего не отмечайте** (README/.gitignore/license — выключены)
   - Create repository
2. На странице пустого репозитория: **Add file → Upload files**.
3. Откройте папку
   `D:\prog\1CUpdate\AdManager-upload`,
   выделите всё (**Ctrl+A**) и перетащите в окно браузера.
   Папки `Assets`, `Common`, `Converters`, `Dialogs`, `Models`, `Services`,
   `ViewModels`, `Views`, `Themes`, `docs` перенесутся со своей структурой.
4. `.gitignore` в проводнике скрыт. Включите показ скрытых файлов и перетащите
   его вместе с остальными.
   Если браузер не принимает папки — перетаскивайте по одной.
5. Внизу страницы: сообщение коммита `AdManager 1.0.0` → **Commit changes**.
6. Проверьте, что НЕ загрузились `bin`, `obj`, `publish`, exe и `.zip`.

---

## Вариант Б. Через git (удобнее для дальнейших правок)

```powershell
# 1. Установить Git for Windows: https://git-scm.com/download/win
#    или: winget install --id Git.Git -e

git config --global user.name "mr_sumin"
git config --global user.email "ваш@email"

cd D:\prog\1CUpdate\AdManager-upload
git init -b main
git add .
git status                  # убедиться, что нет bin/obj/publish/exe
git commit -m "AdManager 1.0.0 - аудит доступов Active Directory"
git remote add origin https://github.com/<логин>/admanager.git
git push -u origin main
```

`git status` перед коммитом должен показать только исходники и документацию.

---

## Релиз с готовой программой

1. GitHub → **Releases** → **Draft a new release**.
2. Tag: `v1.0.0`, Title: `AdManager 1.0.0`.
3. Прикрепите `D:\prog\1CUpdate\AdManager-release\AdManager-1.0.0-win64.zip`
   (Attach binaries).
4. Опишите изменения — скопируйте раздел `[1.0.0]` из `CHANGELOG.md`.
5. **Publish release**.

Если нужен только exe без архива — прикрепите `AdManager.exe` отдельным ассетом.
В репозиторий сам exe не коммитьте: он большой, для этого есть Releases.

---

## SmartScreen и UAC

Exe не подписан, поэтому Windows может показать «Система Windows защитила ваш
компьютер». Снять метку «скачано из интернета»:

```powershell
Unblock-File .\AdManager.exe
```

Приложение собрано с манифестом `requireAdministrator`: при запуске Windows
показывает UAC. Если UAC отключён:

- под администратором приложение запустится сразу с полными правами;
- под обычным пользователем приложение не сможет запуститься — нужен вход
  администратором или включение UAC.

Если планируете подпись, добавьте сертификат и подпишите exe через `signtool`
перед упаковкой в zip.

---

## Оформление репозитория

About (описание):

```
WPF-приложение для аудита доступов Active Directory: пользователи и группы,
привилегированные УЗ, риски, эффективные группы, управление учётными записями,
копирование УЗ, CSV/HTML-отчёты. .NET 8, self-contained, тёмная тема.
```

Topics:

```
active-directory, ad, access-review, audit, identity, security, wpf, dotnet,
csharp, windows, sysadmin, russian
```

---

## Если нужен exe прямо в репозитории

Уберите строки `*.exe` и `publish/` из `.gitignore` и добавьте файл коммитом.
Для публичного проекта обычно лучше держать бинарник в Releases, а в репозитории —
исходники: exe на 155 МБ сильно раздувает историю git.

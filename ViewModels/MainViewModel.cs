using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Data;
using AdManager.Common;
using AdManager.Models;
using AdManager.Services;

namespace AdManager.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly AuditLogService _audit;
    private readonly IDialogService _dialogs;

    private IAdDirectoryService _service;
    private AccessReviewService _review;
    private AppSettings _settings;
    private ReviewResult _reviewResult = new();
    private List<AdUser> _allUsers = new();
    private List<AdGroup> _allGroups = new();
    private List<AdGroup> _referenceGroups = new();
    private List<AdUser> _selectedUsers = new();
    private List<AdGroup> _selectedGroups = new();

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _detailsCts;
    private bool _isBusy;
    private bool _isConnected;
    private bool _isDemo;
    private string _statusText = "Готово";
    private string _busyText = string.Empty;
    private string _connectionError = string.Empty;
    private string _searchText = string.Empty;
    private string _activeFilter = "All";
    private string _groupFilter = "All";
    private bool _onlyProblems;
    private bool _includeDisabled;
    private bool _recursive;
    private int _tabIndex;
    private AdOu? _selectedOu;
    private AdUser? _selectedUser;
    private AdGroup? _selectedGroup;
    private ReviewIssue? _selectedIssue;
    private AdMember? _selectedMember;
    private object? _selectedObject;
    private string _lastUpdatedText = "—";
    private bool _isLoadingDetails;
    private int _staleDays;

    public MainViewModel(SettingsService settingsService, IDialogService dialogs, AuditLogService audit)
    {
        _settingsService = settingsService;
        _settings = settingsService.Current;
        _dialogs = dialogs;
        _audit = audit;
        _service = AdServiceFactory.Create(_settings);
        _review = new AccessReviewService(_settings);
        _includeDisabled = _settings.IncludeDisabled;
        _recursive = _settings.Recursive;
        _staleDays = _settings.StaleDays;

        Users = new ObservableCollection<AdUser>();
        Groups = new ObservableCollection<AdGroup>();
        Issues = new ObservableCollection<ReviewIssue>();
        OuRoots = new ObservableCollection<AdOu>();
        KpiItems = new ObservableCollection<KpiItem>();
        BuildKpiItems();

        UsersView = CollectionViewSource.GetDefaultView(Users);
        UsersView.Filter = FilterUser;
        GroupsView = CollectionViewSource.GetDefaultView(Groups);
        GroupsView.Filter = FilterGroup;
        IssuesView = CollectionViewSource.GetDefaultView(Issues);
        IssuesView.Filter = FilterIssue;

        ConnectCommand = new AsyncRelayCommand(ConnectAsync);
        RefreshCommand = new AsyncRelayCommand(ReloadDataAsync);
        SearchCommand = new AsyncRelayCommand(ReloadDataAsync);
        ClearSearchCommand = new AsyncRelayCommand(async () =>
        {
            SearchText = string.Empty;
            await ReloadDataAsync();
        });

        SelectOuCommand = new AsyncRelayCommand(async parameter =>
        {
            if (parameter is AdOu ou)
            {
                SelectedOu = ou;
                _settings.LastOuDn = ou.DistinguishedName;
                _settingsService.Save();
                await ReloadDataAsync();
            }
        });

        KpiFilterCommand = new RelayCommand(ApplyKpiFilter);

        ToggleEnabledCommand = new AsyncRelayCommand(ToggleEnabledAsync);
        UnlockCommand = new AsyncRelayCommand(UnlockAsync);
        ResetPasswordCommand = new AsyncRelayCommand(ResetPasswordAsync);
        TogglePasswordNeverExpiresCommand = new AsyncRelayCommand(TogglePasswordNeverExpiresAsync);
        MustChangePasswordCommand = new AsyncRelayCommand(MustChangePasswordAsync);
        ResetPasswordAgeCommand = new AsyncRelayCommand(ResetPasswordAgeAsync);
        SetExpirationCommand = new AsyncRelayCommand(SetExpirationAsync);
        MoveOuCommand = new AsyncRelayCommand(MoveOuAsync);
        AddToGroupCommand = new AsyncRelayCommand(AddToGroupAsync);
        RemoveFromGroupCommand = new AsyncRelayCommand(RemoveFromGroupAsync);
        EditUserCommand = new AsyncRelayCommand(EditUserAsync);
        DeleteUserCommand = new AsyncRelayCommand(DeleteUserAsync);
        CopyUserCommand = new AsyncRelayCommand(CopyUserAsync);
        CreateUserCommand = new AsyncRelayCommand(CreateUserAsync);
        CreateGroupCommand = new AsyncRelayCommand(CreateGroupAsync);
        DeleteGroupCommand = new AsyncRelayCommand(DeleteGroupAsync);
        AddGroupMemberCommand = new AsyncRelayCommand(AddGroupMemberAsync);
        RemoveGroupMemberCommand = new AsyncRelayCommand(RemoveGroupMemberAsync);

        ExportUsersCsvCommand = new RelayCommand(ExportUsersCsv);
        ExportGroupsCsvCommand = new RelayCommand(ExportGroupsCsv);
        ExportIssuesCsvCommand = new RelayCommand(ExportIssuesCsv);
        ExportHtmlCommand = new RelayCommand(ExportHtml);
        OpenSettingsCommand = new AsyncRelayCommand(OpenSettingsAsync);
        OpenAuditFolderCommand = new RelayCommand(OpenAuditFolder);
        DiagnosticsCommand = new AsyncRelayCommand(RunDiagnosticsAsync);
    }

    public ObservableCollection<AdUser> Users { get; }
    public ObservableCollection<AdGroup> Groups { get; }
    public ObservableCollection<ReviewIssue> Issues { get; }
    public ObservableCollection<AdOu> OuRoots { get; }
    public ObservableCollection<KpiItem> KpiItems { get; }
    public ICollectionView UsersView { get; }
    public ICollectionView GroupsView { get; }
    public ICollectionView IssuesView { get; }

    public AsyncRelayCommand ConnectCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SearchCommand { get; }
    public AsyncRelayCommand ClearSearchCommand { get; }
    public AsyncRelayCommand SelectOuCommand { get; }
    public RelayCommand KpiFilterCommand { get; }
    public AsyncRelayCommand ToggleEnabledCommand { get; }
    public AsyncRelayCommand UnlockCommand { get; }
    public AsyncRelayCommand ResetPasswordCommand { get; }
    public AsyncRelayCommand TogglePasswordNeverExpiresCommand { get; }
    public AsyncRelayCommand MustChangePasswordCommand { get; }
    public AsyncRelayCommand ResetPasswordAgeCommand { get; }
    public AsyncRelayCommand SetExpirationCommand { get; }
    public AsyncRelayCommand MoveOuCommand { get; }
    public AsyncRelayCommand AddToGroupCommand { get; }
    public AsyncRelayCommand RemoveFromGroupCommand { get; }
    public AsyncRelayCommand EditUserCommand { get; }
    public AsyncRelayCommand DeleteUserCommand { get; }
    public AsyncRelayCommand CopyUserCommand { get; }
    public AsyncRelayCommand CreateUserCommand { get; }
    public AsyncRelayCommand CreateGroupCommand { get; }
    public AsyncRelayCommand DeleteGroupCommand { get; }
    public AsyncRelayCommand AddGroupMemberCommand { get; }
    public AsyncRelayCommand RemoveGroupMemberCommand { get; }
    public RelayCommand ExportUsersCsvCommand { get; }
    public RelayCommand ExportGroupsCsvCommand { get; }
    public RelayCommand ExportIssuesCsvCommand { get; }
    public RelayCommand ExportHtmlCommand { get; }
    public AsyncRelayCommand OpenSettingsCommand { get; }
    public RelayCommand OpenAuditFolderCommand { get; }
    public AsyncRelayCommand DiagnosticsCommand { get; }

    public bool IsConnected { get => _isConnected; private set { if (Set(ref _isConnected, value)) { OnPropertyChanged(nameof(IsNotConnected)); OnPropertyChanged(nameof(ConnectionStatus)); } } }
    public bool IsNotConnected => !IsConnected;
    public bool IsElevated => ElevationHelper.IsAdministrator;
    public bool IsNotElevated => !IsElevated;
    public bool ShowElevationWarning => IsNotElevated && !IsDemo;
    public bool IsDemo { get => _isDemo; private set { if (Set(ref _isDemo, value)) { OnPropertyChanged(nameof(ConnectionStatus)); OnPropertyChanged(nameof(ShowElevationWarning)); } } }
    public string DomainName => _service.DomainName;
    public string ServerName => _service.ServerName;
    public string CurrentUser => _service.CurrentUser;
    public string ConnectionError { get => _connectionError; private set => Set(ref _connectionError, value); }
    public string ConnectionStatus => IsDemo ? "Демо-режим" : IsConnected ? "Подключено" : "Нет подключения";
    public string ConnectionDetails => IsConnected
        ? $"{DomainName} · {ServerName}"
        : "Проверьте домен и права текущей учётной записи";

    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) OnPropertyChanged(nameof(IsNotBusy)); } }
    public bool IsNotBusy => !IsBusy;
    public string BusyText { get => _busyText; private set => Set(ref _busyText, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public string LastUpdatedText { get => _lastUpdatedText; private set => Set(ref _lastUpdatedText, value); }
    public bool IsLoadingDetails { get => _isLoadingDetails; private set => Set(ref _isLoadingDetails, value); }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value))
            {
                RefreshViews();
            }
        }
    }

    public string ActiveFilter
    {
        get => _activeFilter;
        private set
        {
            if (Set(ref _activeFilter, value)) RefreshViews();
        }
    }

    public string GroupFilter
    {
        get => _groupFilter;
        private set
        {
            if (Set(ref _groupFilter, value)) RefreshViews();
        }
    }

    public bool OnlyProblems
    {
        get => _onlyProblems;
        set
        {
            if (Set(ref _onlyProblems, value))
            {
                if (value)
                {
                    ActiveFilter = "All";
                    GroupFilter = "All";
                }

                RefreshViews();
            }
        }
    }

    public bool IncludeDisabled
    {
        get => _includeDisabled;
        set
        {
            if (Set(ref _includeDisabled, value))
            {
                _settings.IncludeDisabled = value;
                _settingsService.Save();
                RefreshViews();
            }
        }
    }

    public bool Recursive
    {
        get => _recursive;
        set
        {
            if (Set(ref _recursive, value))
            {
                _settings.Recursive = value;
                _settingsService.Save();
                _ = ReloadDataAsync();
            }
        }
    }

    public int StaleDays
    {
        get => _staleDays;
        set
        {
            int normalized = Math.Clamp(value, 1, 3650);
            if (Set(ref _staleDays, normalized))
            {
                _settings.StaleDays = normalized;
                _settingsService.Save();
                RebuildReview();
            }
        }
    }

    public int TabIndex
    {
        get => _tabIndex;
        set
        {
            if (Set(ref _tabIndex, value))
            {
                SelectedObject = value switch
                {
                    0 => SelectedUser,
                    1 => SelectedGroup,
                    _ => SelectedIssue?.User ?? (object?)SelectedIssue?.Group
                };
                OnPropertyChanged(nameof(IsUsersTab));
                OnPropertyChanged(nameof(IsGroupsTab));
                OnPropertyChanged(nameof(IsReviewTab));
                UpdateKpiActiveState();
            }
        }
    }

    public bool IsUsersTab => TabIndex == 0;
    public bool IsGroupsTab => TabIndex == 1;
    public bool IsReviewTab => TabIndex == 2;

    public AdOu? SelectedOu
    {
        get => _selectedOu;
        set
        {
            if (Set(ref _selectedOu, value))
            {
                OnPropertyChanged(nameof(SelectedOuPath));
            }
        }
    }

    public string SelectedOuPath => SelectedOu?.DistinguishedName ?? "—";

    public AdUser? SelectedUser
    {
        get => _selectedUser;
        set
        {
            if (Set(ref _selectedUser, value))
            {
                if (TabIndex == 0) SelectedObject = value;
                OnPropertyChanged(nameof(HasUserSelection));
                OnPropertyChanged(nameof(SelectionText));
                LoadUserDetails(value);
            }
        }
    }

    public AdGroup? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (Set(ref _selectedGroup, value))
            {
                if (TabIndex == 1) SelectedObject = value;
                OnPropertyChanged(nameof(HasGroupSelection));
                OnPropertyChanged(nameof(SelectionText));
                LoadGroupDetails(value);
            }
        }
    }

    public ReviewIssue? SelectedIssue
    {
        get => _selectedIssue;
        set
        {
            if (Set(ref _selectedIssue, value))
            {
                if (value?.User is not null)
                {
                    TabIndex = 0;
                    SelectedUser = value.User;
                }
                else if (value?.Group is not null)
                {
                    TabIndex = 1;
                    SelectedGroup = value.Group;
                }
            }
        }
    }

    public AdMember? SelectedMember
    {
        get => _selectedMember;
        set
        {
            if (Set(ref _selectedMember, value))
            {
                OnPropertyChanged(nameof(HasSelectedMember));
            }
        }
    }

    public object? SelectedObject
    {
        get => _selectedObject;
        private set => Set(ref _selectedObject, value);
    }

    public bool HasUserSelection => _selectedUsers.Count > 0 || SelectedUser is not null;
    public bool HasGroupSelection => SelectedGroup is not null;
    public bool HasSelectedMember => SelectedMember is not null;

    public string SelectionText
    {
        get
        {
            if (_selectedUsers.Count > 1) return $"Выбрано пользователей: {_selectedUsers.Count}";
            if (_selectedGroups.Count > 1) return $"Выбрано групп: {_selectedGroups.Count}";
            if (SelectedUser is not null) return "Выбран: " + SelectedUser.DisplayLabel;
            if (SelectedGroup is not null) return "Выбрана группа: " + SelectedGroup.Name;
            return "Ничего не выбрано";
        }
    }

    public IReadOnlyList<AdOu> AllOus => FlattenOus(OuRoots).ToList();

    public async Task InitializeAsync()
    {
        await ConnectAsync();
    }

    public void SetSelectedUsers(IEnumerable<AdUser> users)
    {
        _selectedUsers = users.ToList();
        OnPropertyChanged(nameof(HasUserSelection));
        OnPropertyChanged(nameof(SelectionText));
    }

    public void SetSelectedGroups(IEnumerable<AdGroup> groups)
    {
        _selectedGroups = groups.ToList();
        OnPropertyChanged(nameof(HasGroupSelection));
        OnPropertyChanged(nameof(SelectionText));
    }

    private async Task ConnectAsync()
    {
        try
        {
            _loadCts?.Cancel();
            _service.Dispose();
            _settings = _settingsService.Current;
            _service = AdServiceFactory.Create(_settings);
            _review = new AccessReviewService(_settings);

            SetBusy(true, "Подключение к домену…");
            await _service.ConnectAsync(_settings.Domain, _settings.Server, CancellationToken.None);

            IsDemo = _service.IsDemo;
            ConnectionError = _service.ConnectionError;
            OnPropertyChanged(nameof(DomainName));
            OnPropertyChanged(nameof(ServerName));
            OnPropertyChanged(nameof(CurrentUser));
            OnPropertyChanged(nameof(ConnectionDetails));

            if (!_service.IsConnected)
            {
                IsConnected = false;
                StatusText = "Не удалось подключиться: " + ConnectionError;
                SetBusy(false);
                return;
            }

            IsConnected = true;
            StatusText = _service.IsDemo
                ? "Демо-режим: интерфейс работает на тестовых данных."
                : $"Подключено к {DomainName}. Загрузка данных…";

            await LoadOuTreeAsync();
            await ReloadDataAsync();
        }
        catch (Exception ex)
        {
            IsConnected = false;
            ConnectionError = ex.Message;
            StatusText = "Ошибка подключения: " + ex.Message;
            SetBusy(false);
        }
    }

    private async Task LoadOuTreeAsync()
    {
        try
        {
            var ous = await _service.LoadOusAsync(CancellationToken.None);
            var root = new AdOu
            {
                Name = string.IsNullOrWhiteSpace(_service.DomainName) ? "Домен" : _service.DomainName,
                DistinguishedName = _service.RootDn,
                ParentDn = string.Empty,
                IsExpanded = true
            };

            var map = new Dictionary<string, AdOu>(StringComparer.OrdinalIgnoreCase)
            {
                [root.DistinguishedName] = root
            };

            foreach (var ou in ous)
            {
                if (string.IsNullOrWhiteSpace(ou.DistinguishedName)) continue;
                // В выборке может встретиться сам корень домена — не подменяем корневой узел.
                if (ou.DistinguishedName.Equals(root.DistinguishedName, StringComparison.OrdinalIgnoreCase)) continue;
                map[ou.DistinguishedName] = ou;
            }

            foreach (var ou in ous)
            {
                if (string.IsNullOrWhiteSpace(ou.DistinguishedName)) continue;
                if (ou.DistinguishedName.Equals(root.DistinguishedName, StringComparison.OrdinalIgnoreCase)) continue;
                if (map.TryGetValue(ou.ParentDn, out var parent) && !ReferenceEquals(parent, ou))
                {
                    if (!parent.Children.Contains(ou))
                    {
                        parent.Children.Add(ou);
                    }
                }
                else
                {
                    root.Children.Add(ou);
                }
            }

            SortOus(root);
            OuRoots.Clear();
            OuRoots.Add(root);

            AdOu selected = root;
            if (!string.IsNullOrWhiteSpace(_settings.LastOuDn) && map.TryGetValue(_settings.LastOuDn, out var saved))
            {
                selected = saved;
            }

            SelectedOu = selected;
        }
        catch (Exception ex)
        {
            StatusText = "Не удалось прочитать структуру OU: " + ex.Message;
        }
    }

    private static void SortOus(AdOu node)
    {
        var children = node.Children.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        node.Children.Clear();
        foreach (var child in children)
        {
            node.Children.Add(child);
            SortOus(child);
        }
    }

    private static IEnumerable<AdOu> FlattenOus(IEnumerable<AdOu> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in FlattenOus(node.Children))
            {
                yield return child;
            }
        }
    }

    private async Task ReloadDataAsync()
    {
        if (!IsConnected) return;

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        string? selectedUserDn = SelectedUser?.DistinguishedName;
        string? selectedGroupDn = SelectedGroup?.DistinguishedName;

        try
        {
            SetBusy(true, "Загрузка пользователей и групп…");
            string baseDn = SelectedOu?.DistinguishedName ?? _service.RootDn;

            var usersTask = _service.LoadUsersAsync(
                baseDn, Recursive, SearchText, IncludeDisabled, _settings.MaxObjects, ct);

            // Группы — это объекты домена, а не OU: показываем все группы домена,
            // иначе при фильтре по подразделению теряются Domain Admins/Administrators
            // и группы доступа, в которые входят пользователи из выбранного OU.
            var groupsTask = _service.LoadGroupsAsync(
                _service.RootDn, true, SearchText, _settings.MaxObjects, ct);

            await Task.WhenAll(usersTask, groupsTask);
            ct.ThrowIfCancellationRequested();

            _allUsers = await usersTask;
            _allGroups = await groupsTask;
            _referenceGroups = _allGroups;

            RebuildReview();

            SelectedUser = selectedUserDn is null
                ? null
                : _allUsers.FirstOrDefault(u => u.DistinguishedName.Equals(selectedUserDn, StringComparison.OrdinalIgnoreCase));
            SelectedGroup = selectedGroupDn is null
                ? null
                : _allGroups.FirstOrDefault(g => g.DistinguishedName.Equals(selectedGroupDn, StringComparison.OrdinalIgnoreCase));

            LastUpdatedText = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
            StatusText = $"Пользователей: {_allUsers.Count} · групп: {_allGroups.Count} · проблем: {_reviewResult.Issues.Count}";
        }
        catch (OperationCanceledException)
        {
            // Предыдущая загрузка отменена.
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка загрузки: " + ex.Message;
            _dialogs.Error("Загрузка данных", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RebuildReview()
    {
        _reviewResult = _review.Build(_allUsers, _allGroups, _referenceGroups);

        Users.Clear();
        foreach (var user in _allUsers) Users.Add(user);
        Groups.Clear();
        foreach (var group in _allGroups) Groups.Add(group);
        Issues.Clear();
        foreach (var issue in _reviewResult.Issues) Issues.Add(issue);

        UpdateKpis();
        RefreshViews();
    }

    private void BuildKpiItems()
    {
        AddKpi("Всего пользователей", "Всего пользователей", "All", "#007ACC");
        AddKpi("Привилегированные", "Привилегированные", "Privileged", "#F0A35E");
        AddKpi("Неактивные", "Неактивные", "Stale", "#DBCD78");
        AddKpi("Никогда не входили", "Никогда не входили", "NeverLoggedIn", "#9696A0");
        AddKpi("Отключены, но в группах", "Отключены, но сохраняют членство в группах", "DisabledWithAccess", "#DBCD78");
        AddKpi("SPN", "SPN (Kerberoastable)", "Spn", "#F0A35E");
        AddKpi("AS-REP", "AS-REP", "AsRep", "#F48771");
        AddKpi("Делегирование", "Делегирование", "Delegation", "#F0A35E");
        AddKpi("SID History", "SID History", "SidHistory", "#F48771");
        AddKpi("Пароль не истекает", "Пароль не истекает", "PasswordNeverExpires", "#DBCD78");
        AddKpi("Пароль истекает", "Пароль истекает", "PasswordExpiring", "#DBCD78");
        AddKpi("Группы", "Группы", "Groups:All", "#78B4F0");
        AddKpi("Пустые группы", "Пустые группы", "Groups:Empty", "#9696A0");
        AddKpi("Критичные", "Критичные проблемы", "Critical", "#F48771");
    }

    private void AddKpi(string label, string tooltip, string filter, string accent)
    {
        KpiItems.Add(new KpiItem { Label = label, Filter = filter, Accent = accent, Value = 0 });
    }

    private void UpdateKpis()
    {
        var s = _reviewResult.Summary;
        SetKpi("All", s.TotalUsers);
        SetKpi("Privileged", s.PrivilegedUsers);
        SetKpi("Stale", s.StaleUsers);
        SetKpi("NeverLoggedIn", s.NeverLoggedIn);
        SetKpi("DisabledWithAccess", s.DisabledWithAccess);
        SetKpi("Spn", s.SpnUsers);
        SetKpi("AsRep", s.AsRepUsers);
        SetKpi("Delegation", s.DelegationUsers);
        SetKpi("SidHistory", s.SidHistoryUsers);
        SetKpi("PasswordNeverExpires", s.PasswordNeverExpires);
        SetKpi("PasswordExpiring", s.PasswordExpiring);
        SetKpi("Groups:All", s.TotalGroups);
        SetKpi("Groups:Empty", s.EmptyGroups);
        SetKpi("Critical", s.CriticalIssues);
    }

    private void SetKpi(string filter, int value)
    {
        var item = KpiItems.FirstOrDefault(k => k.Filter == filter);
        if (item is not null) item.Value = value;
    }

    private void ApplyKpiFilter(object? parameter)
    {
        if (parameter is not string filter) return;
        if (filter.StartsWith("Groups:", StringComparison.Ordinal))
        {
            TabIndex = 1;
            GroupFilter = filter["Groups:".Length..];
            ActiveFilter = "All";
        }
        else
        {
            TabIndex = 0;
            ActiveFilter = filter;
            GroupFilter = "All";
        }

        RefreshViews();
    }

    private bool FilterUser(object item)
    {
        if (item is not AdUser user) return false;
        if (!IncludeDisabled && !user.Enabled) return false;
        if (OnlyProblems && user.RiskFlags.Count == 0) return false;
        if (!string.IsNullOrWhiteSpace(SearchText) &&
            !user.SearchBlob.Contains(SearchText.Trim().ToLowerInvariant(), StringComparison.Ordinal))
        {
            return false;
        }

        return ActiveFilter switch
        {
            "Privileged" => user.IsPrivileged,
            "Stale" => user.IsStale,
            "NeverLoggedIn" => user.IsNeverLoggedIn,
            "DisabledWithAccess" => user.IsDisabledWithAccess,
            "Spn" => user.HasSpn,
            "AsRep" => user.DontRequirePreAuth,
            "Delegation" => user.TrustedForDelegation || user.TrustedToAuthForDelegation || user.HasConstrainedDelegation,
            "SidHistory" => user.HasSidHistory,
            "PasswordNeverExpires" => user.PasswordNeverExpires,
            "MissingManager" => user.HasNoManager,
            "PasswordExpiring" => !user.PasswordNeverExpires &&
                                  (user.IsPasswordExpired ||
                                   (user.PasswordExpires.HasValue && user.PasswordExpires.Value > DateTime.UtcNow &&
                                    (user.PasswordExpires.Value - DateTime.UtcNow).TotalDays <= _settings.PasswordExpiringDays)),
            "Critical" => user.RiskFlags.Any(f => f.Severity == RiskSeverity.Critical),
            _ => true
        };
    }

    private bool FilterGroup(object item)
    {
        if (item is not AdGroup group) return false;
        if (OnlyProblems && group.RiskFlags.Count == 0) return false;
        if (!string.IsNullOrWhiteSpace(SearchText) &&
            !group.SearchBlob.Contains(SearchText.Trim().ToLowerInvariant(), StringComparison.Ordinal))
        {
            return false;
        }

        return GroupFilter switch
        {
            "Privileged" => group.IsPrivileged,
            "Empty" => group.IsEmpty,
            "NoOwner" => group.HasNoOwner,
            _ => true
        };
    }

    private bool FilterIssue(object item)
    {
        if (item is not ReviewIssue issue) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        string s = SearchText.Trim().ToLowerInvariant();
        return issue.ObjectName.Contains(s, StringComparison.OrdinalIgnoreCase)
               || issue.SamAccountName.Contains(s, StringComparison.OrdinalIgnoreCase)
               || issue.Category.Contains(s, StringComparison.OrdinalIgnoreCase)
               || issue.Details.Contains(s, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshViews()
    {
        UsersView.Refresh();
        GroupsView.Refresh();
        IssuesView.Refresh();
        UpdateKpiActiveState();
    }

    private void UpdateKpiActiveState()
    {
        string current = TabIndex switch
        {
            0 => ActiveFilter,
            1 => "Groups:" + GroupFilter,
            _ => string.Empty
        };

        foreach (var item in KpiItems)
        {
            item.IsActive = !string.IsNullOrEmpty(current) &&
                            string.Equals(item.Filter, current, StringComparison.Ordinal);
        }
    }

    private async void LoadUserDetails(AdUser? user)
    {
        _detailsCts?.Cancel();
        if (user is null) return;

        _detailsCts = new CancellationTokenSource();
        var ct = _detailsCts.Token;
        try
        {
            IsLoadingDetails = true;
            await _service.LoadUserDetailsAsync(user, ct);

            try
            {
                var effective = await _service.LoadUserEffectiveGroupDnsAsync(user, ct);
                user.EffectiveGroups = effective.Select(AdValue.DnToName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            }
            catch
            {
                // Эффективные группы не критичны для карточки.
            }

            user.RaiseAll();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusText = "Не удалось загрузить карточку: " + ex.Message;
        }
        finally
        {
            IsLoadingDetails = false;
        }
    }

    private async void LoadGroupDetails(AdGroup? group)
    {
        _detailsCts?.Cancel();
        if (group is null) return;

        _detailsCts = new CancellationTokenSource();
        var ct = _detailsCts.Token;
        try
        {
            IsLoadingDetails = true;
            await _service.LoadGroupDetailsAsync(group, ct);
            await _service.LoadGroupMembersAsync(group, ct);
            group.RaiseAll();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusText = "Не удалось загрузить группу: " + ex.Message;
        }
        finally
        {
            IsLoadingDetails = false;
        }
    }

    private List<AdUser> TargetUsers()
    {
        if (_selectedUsers.Count > 0) return _selectedUsers.ToList();
        if (SelectedUser is not null) return new List<AdUser> { SelectedUser };
        return new List<AdUser>();
    }

    private async Task RunUsersAsync(string action, Func<AdUser, Task> operation, string details)
    {
        var targets = TargetUsers();
        if (targets.Count == 0)
        {
            _dialogs.Info(action, "Сначала выберите пользователя или несколько пользователей.");
            return;
        }

        string names = string.Join("\n", targets.Take(8).Select(u => "• " + u.DisplayLabel + " (" + u.SamAccountName + ")"));
        if (targets.Count > 8) names += $"\n… и ещё {targets.Count - 8}";
        string what = targets.Count == 1 ? "1 объекта" : $"{targets.Count} объектов";
        if (!_dialogs.Confirm(action, $"{action} для {what}?\n\n{names}"))
        {
            return;
        }

        SetBusy(true, action + "…");
        var errors = new List<string>();
        int ok = 0;
        foreach (var user in targets)
        {
            try
            {
                await operation(user);
                ok++;
                _audit.Write(action, user.DistinguishedName, details);
            }
            catch (Exception ex)
            {
                errors.Add(user.DisplayLabel + ": " + ex.Message);
                _audit.Write(action, user.DistinguishedName, details, false, ex.Message);
            }
        }

        SetBusy(false);
        await ReloadDataAsync();

        if (errors.Count > 0)
        {
            _dialogs.Error(action, $"Успешно: {ok}. Ошибок: {errors.Count}.\n\n" + string.Join("\n", errors.Take(20)));
        }
        else
        {
            _dialogs.Info(action, $"Готово. Успешно: {ok}.");
        }
    }

    private async Task ToggleEnabledAsync()
    {
        var targets = TargetUsers();
        if (targets.Count == 0)
        {
            _dialogs.Info("Включение/отключение", "Сначала выберите пользователя.");
            return;
        }

        bool enable = !targets.All(u => u.Enabled);
        await RunUsersAsync(enable ? "Включение УЗ" : "Отключение УЗ",
            u => _service.SetUserEnabledAsync(u, enable, CancellationToken.None),
            enable ? "Enabled=true" : "Enabled=false");
    }

    private async Task UnlockAsync()
    {
        var targets = TargetUsers().Where(u => u.Locked).ToList();
        if (targets.Count == 0)
        {
            _dialogs.Info("Разблокировка", "Среди выбранных нет заблокированных учётных записей.");
            return;
        }

        _selectedUsers = targets;
        await RunUsersAsync("Разблокировка УЗ",
            u => _service.UnlockUserAsync(u, CancellationToken.None),
            "lockoutTime=0");
    }

    private async Task ResetPasswordAsync()
    {
        var target = SelectedUser ?? TargetUsers().FirstOrDefault();
        if (target is null)
        {
            _dialogs.Info("Сброс пароля", "Выберите одну учётную запись.");
            return;
        }

        var options = _dialogs.ResetPassword(target, _settings.PasswordLength);
        if (options is null) return;

        try
        {
            SetBusy(true, "Сброс пароля…");
            await _service.ResetPasswordAsync(target, options.Password, options.MustChange, CancellationToken.None);
            _audit.Write("Сброс пароля", target.DistinguishedName,
                options.MustChange ? "MustChange=true" : "MustChange=false");

            if (options.Unlock && target.Locked)
            {
                await _service.UnlockUserAsync(target, CancellationToken.None);
                _audit.Write("Разблокировка УЗ", target.DistinguishedName, "вместе со сбросом пароля");
            }

            await ReloadDataAsync();
            _dialogs.Info("Сброс пароля", $"Пароль для {target.DisplayLabel} изменён.");
        }
        catch (Exception ex)
        {
            _audit.Write("Сброс пароля", target.DistinguishedName, "", false, ex.Message);
            _dialogs.Error("Сброс пароля", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task TogglePasswordNeverExpiresAsync()
    {
        var targets = TargetUsers();
        if (targets.Count == 0)
        {
            _dialogs.Info("Срок действия пароля", "Выберите пользователя.");
            return;
        }

        bool value = !targets.All(u => u.PasswordNeverExpires);
        await RunUsersAsync(value ? "Пароль не истекает: включить" : "Пароль не истекает: выключить",
            u => _service.SetPasswordNeverExpiresAsync(u, value, CancellationToken.None),
            "DONT_EXPIRE_PASSWORD=" + value);
    }

    private async Task MustChangePasswordAsync()
    {
        var targets = TargetUsers();
        if (targets.Count == 0)
        {
            _dialogs.Info("Смена пароля", "Выберите пользователя.");
            return;
        }

        await RunUsersAsync("Требовать смену пароля",
            u => _service.SetMustChangePasswordAsync(u, CancellationToken.None),
            "pwdLastSet=0");
    }

    private async Task ResetPasswordAgeAsync()
    {
        var targets = TargetUsers();
        if (targets.Count == 0)
        {
            _dialogs.Info("Возраст пароля", "Выберите пользователя.");
            return;
        }

        await RunUsersAsync("Сбросить возраст пароля",
            u => _service.ResetPasswordAgeAsync(u, CancellationToken.None),
            "pwdLastSet=-1");
    }

    private async Task SetExpirationAsync()
    {
        var target = SelectedUser ?? TargetUsers().FirstOrDefault();
        if (target is null)
        {
            _dialogs.Info("Срок действия УЗ", "Выберите пользователя.");
            return;
        }

        var choice = _dialogs.PickExpiration(target.AccountExpires);
        if (choice.Canceled) return;

        try
        {
            SetBusy(true, "Изменение срока действия…");
            await _service.SetAccountExpirationAsync(target, choice.Expiration, CancellationToken.None);
            _audit.Write("Срок действия УЗ", target.DistinguishedName,
                choice.Expiration?.ToString("yyyy-MM-dd") ?? "never");
            await ReloadDataAsync();
            _dialogs.Info("Срок действия УЗ", "Срок действия изменён.");
        }
        catch (Exception ex)
        {
            _audit.Write("Срок действия УЗ", target.DistinguishedName, "", false, ex.Message);
            _dialogs.Error("Срок действия УЗ", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task MoveOuAsync()
    {
        var targets = TargetUsers();
        if (targets.Count == 0)
        {
            _dialogs.Info("Перемещение", "Выберите пользователя.");
            return;
        }

        var ou = _dialogs.PickOu(AllOus, "Переместить в OU", SelectedOu?.DistinguishedName);
        if (ou is null) return;

        string what = targets.Count == 1 ? "1 объект" : $"{targets.Count} объектов";
        if (!_dialogs.Confirm("Перемещение", $"Переместить {what} в:\n{ou.DistinguishedName}?"))
        {
            return;
        }

        SetBusy(true, "Перемещение…");
        var errors = new List<string>();
        int ok = 0;
        foreach (var user in targets)
        {
            try
            {
                await _service.MoveObjectAsync(user.DistinguishedName, ou.DistinguishedName, CancellationToken.None);
                ok++;
                _audit.Write("Перемещение", user.DistinguishedName, "-> " + ou.DistinguishedName);
            }
            catch (Exception ex)
            {
                errors.Add(user.DisplayLabel + ": " + ex.Message);
                _audit.Write("Перемещение", user.DistinguishedName, "-> " + ou.DistinguishedName, false, ex.Message);
            }
        }

        SetBusy(false);
        await ReloadDataAsync();
        if (errors.Count > 0)
        {
            _dialogs.Error("Перемещение", $"Успешно: {ok}. Ошибок: {errors.Count}.\n\n" + string.Join("\n", errors.Take(20)));
        }
        else
        {
            _dialogs.Info("Перемещение", $"Готово. Перемещено: {ok}.");
        }
    }

    private async Task AddToGroupAsync()
    {
        var targets = TargetUsers();
        if (targets.Count == 0)
        {
            _dialogs.Info("Добавление в группы", "Выберите пользователя.");
            return;
        }

        IReadOnlyList<AdGroup> available = _allGroups;
        if (available.Count == 0)
        {
            try
            {
                SetBusy(true, "Загрузка групп…");
                available = await _service.LoadGroupsAsync(_service.RootDn, true, null, _settings.MaxObjects, CancellationToken.None);
            }
            finally
            {
                SetBusy(false);
            }
        }

        var groups = _dialogs.PickGroups(available, "Добавить в группы");
        if (groups.Count == 0) return;

        SetBusy(true, "Добавление в группы…");
        var errors = new List<string>();
        int ok = 0;
        foreach (var user in targets)
        {
            foreach (var group in groups)
            {
                try
                {
                    await _service.AddToGroupAsync(group.DistinguishedName, user.DistinguishedName, CancellationToken.None);
                    ok++;
                    _audit.Write("Добавление в группу", user.DistinguishedName, group.Name);
                }
                catch (Exception ex)
                {
                    errors.Add($"{user.DisplayLabel} → {group.Name}: {ex.Message}");
                    _audit.Write("Добавление в группу", user.DistinguishedName, group.Name, false, ex.Message);
                }
            }
        }

        SetBusy(false);
        await ReloadDataAsync();
        if (errors.Count > 0)
        {
            _dialogs.Error("Добавление в группы", $"Успешно: {ok}. Ошибок: {errors.Count}.\n\n" + string.Join("\n", errors.Take(20)));
        }
        else
        {
            _dialogs.Info("Добавление в группы", $"Готово. Добавлений: {ok}.");
        }
    }

    private async Task RemoveFromGroupAsync()
    {
        var targets = TargetUsers();
        if (targets.Count == 0)
        {
            _dialogs.Info("Удаление из групп", "Выберите пользователя.");
            return;
        }

        var map = new Dictionary<string, AdGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var user in targets)
        {
            foreach (string dn in user.MemberOf)
            {
                if (!map.ContainsKey(dn))
                {
                    map[dn] = new AdGroup
                    {
                        DistinguishedName = dn,
                        Name = AdValue.DnToName(dn),
                        SamAccountName = AdValue.DnToName(dn)
                    };
                }
            }
        }

        var groups = _dialogs.PickGroups(map.Values.OrderBy(g => g.Name).ToList(), "Удалить из групп");
        if (groups.Count == 0) return;

        SetBusy(true, "Удаление из групп…");
        var errors = new List<string>();
        int ok = 0;
        foreach (var user in targets)
        {
            foreach (var group in groups)
            {
                try
                {
                    await _service.RemoveFromGroupAsync(group.DistinguishedName, user.DistinguishedName, CancellationToken.None);
                    ok++;
                    _audit.Write("Удаление из группы", user.DistinguishedName, group.Name);
                }
                catch (Exception ex)
                {
                    errors.Add($"{user.DisplayLabel} ← {group.Name}: {ex.Message}");
                    _audit.Write("Удаление из группы", user.DistinguishedName, group.Name, false, ex.Message);
                }
            }
        }

        SetBusy(false);
        await ReloadDataAsync();
        if (errors.Count > 0)
        {
            _dialogs.Error("Удаление из групп", $"Успешно: {ok}. Ошибок: {errors.Count}.\n\n" + string.Join("\n", errors.Take(20)));
        }
        else
        {
            _dialogs.Info("Удаление из групп", $"Готово. Удалений: {ok}.");
        }
    }

    private async Task EditUserAsync()
    {
        var target = SelectedUser ?? TargetUsers().FirstOrDefault();
        if (target is null)
        {
            _dialogs.Info("Редактирование", "Выберите пользователя.");
            return;
        }

        var attributes = _dialogs.EditUser(target);
        if (attributes is null) return;

        try
        {
            SetBusy(true, "Сохранение атрибутов…");
            await _service.UpdateUserAttributesAsync(target, attributes, CancellationToken.None);
            _audit.Write("Изменение атрибутов", target.DistinguishedName,
                string.Join("; ", attributes.Select(a => a.Key + "=" + (a.Value ?? ""))));
            target.DetailsLoaded = false;
            await ReloadDataAsync();
            _dialogs.Info("Редактирование", "Атрибуты сохранены.");
        }
        catch (Exception ex)
        {
            _audit.Write("Изменение атрибутов", target.DistinguishedName, "", false, ex.Message);
            _dialogs.Error("Редактирование", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task DeleteUserAsync()
    {
        var targets = TargetUsers();
        if (targets.Count == 0)
        {
            _dialogs.Info("Удаление", "Выберите пользователя.");
            return;
        }

        string names = string.Join("\n", targets.Take(8).Select(u => "• " + u.DisplayLabel + " (" + u.SamAccountName + ")"));
        string question = targets.Count == 1 ? "Удалить 1 учётную запись?" : $"Удалить {targets.Count} учётных записей?";
        if (!_dialogs.Confirm("Удаление УЗ",
                $"{question}\n\n{names}\n\nЭто необратимая операция. Рекомендуется сначала отключить УЗ."))
        {
            return;
        }

        SetBusy(true, "Удаление…");
        var errors = new List<string>();
        int ok = 0;
        foreach (var user in targets)
        {
            try
            {
                await _service.DeleteUserAsync(user, CancellationToken.None);
                ok++;
                _audit.Write("Удаление УЗ", user.DistinguishedName, user.SamAccountName);
            }
            catch (Exception ex)
            {
                errors.Add(user.DisplayLabel + ": " + ex.Message);
                _audit.Write("Удаление УЗ", user.DistinguishedName, user.SamAccountName, false, ex.Message);
            }
        }

        SetBusy(false);
        await ReloadDataAsync();
        if (errors.Count > 0)
        {
            _dialogs.Error("Удаление", $"Удалено: {ok}. Ошибок: {errors.Count}.\n\n" + string.Join("\n", errors.Take(20)));
        }
        else
        {
            _dialogs.Info("Удаление", $"Удалено: {ok}.");
        }
    }

    private async Task CreateUserAsync()
    {
        string parent = SelectedOu?.DistinguishedName ?? _service.RootDn;
        var request = _dialogs.CreateUser(parent, AllOus);
        if (request is null) return;

        try
        {
            SetBusy(true, "Создание пользователя…");
            await _service.CreateUserAsync(request, CancellationToken.None);
            _audit.Write("Создание УЗ", request.SamAccountName, request.ParentDn);
            await ReloadDataAsync();
            _dialogs.Info("Создание пользователя", $"Учётная запись {request.SamAccountName} создана.");
        }
        catch (Exception ex)
        {
            _audit.Write("Создание УЗ", request.SamAccountName, request.ParentDn, false, ex.Message);
            _dialogs.Error("Создание пользователя", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task CreateGroupAsync()
    {
        string parent = SelectedOu?.DistinguishedName ?? _service.RootDn;
        var request = _dialogs.CreateGroup(parent, AllOus);
        if (request is null) return;

        try
        {
            SetBusy(true, "Создание группы…");
            await _service.CreateGroupAsync(request, CancellationToken.None);
            _audit.Write("Создание группы", request.SamAccountName, request.ParentDn);
            await ReloadDataAsync();
            _dialogs.Info("Создание группы", $"Группа {request.Name} создана.");
        }
        catch (Exception ex)
        {
            _audit.Write("Создание группы", request.SamAccountName, request.ParentDn, false, ex.Message);
            _dialogs.Error("Создание группы", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task DeleteGroupAsync()
    {
        var group = SelectedGroup;
        if (group is null)
        {
            _dialogs.Info("Удаление группы", "Выберите группу.");
            return;
        }

        if (!_dialogs.Confirm("Удаление группы",
                $"Удалить группу «{group.Name}»?\n\nУчастников: {group.MemberCount}. Это необратимая операция."))
        {
            return;
        }

        try
        {
            SetBusy(true, "Удаление группы…");
            await _service.DeleteGroupAsync(group, CancellationToken.None);
            _audit.Write("Удаление группы", group.DistinguishedName, group.Name);
            await ReloadDataAsync();
            _dialogs.Info("Удаление группы", "Группа удалена.");
        }
        catch (Exception ex)
        {
            _audit.Write("Удаление группы", group.DistinguishedName, group.Name, false, ex.Message);
            _dialogs.Error("Удаление группы", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task AddGroupMemberAsync()
    {
        var group = SelectedGroup;
        if (group is null)
        {
            _dialogs.Info("Добавление участника", "Выберите группу.");
            return;
        }

        var available = _allUsers
            .Where(u => !group.MemberDns.Contains(u.DistinguishedName, StringComparer.OrdinalIgnoreCase))
            .ToList();
        var users = _dialogs.PickUsers(available, "Добавить участников в «" + group.Name + "»");
        if (users.Count == 0) return;

        SetBusy(true, "Добавление участников…");
        var errors = new List<string>();
        int ok = 0;
        foreach (var user in users)
        {
            try
            {
                await _service.AddGroupMemberAsync(group.DistinguishedName, user.DistinguishedName, CancellationToken.None);
                ok++;
                _audit.Write("Добавление в группу", user.DistinguishedName, group.Name);
            }
            catch (Exception ex)
            {
                errors.Add(user.DisplayLabel + ": " + ex.Message);
                _audit.Write("Добавление в группу", user.DistinguishedName, group.Name, false, ex.Message);
            }
        }

        SetBusy(false);
        group.DetailsLoaded = false;
        await ReloadDataAsync();
        if (errors.Count > 0)
        {
            _dialogs.Error("Добавление участников", $"Успешно: {ok}. Ошибок: {errors.Count}.\n\n" + string.Join("\n", errors.Take(20)));
        }
        else
        {
            _dialogs.Info("Добавление участников", $"Добавлено: {ok}.");
        }
    }

    private async Task RemoveGroupMemberAsync()
    {
        var group = SelectedGroup;
        var member = SelectedMember;
        if (group is null || member is null)
        {
            _dialogs.Info("Удаление участника", "Выберите группу и участника.");
            return;
        }

        if (!_dialogs.Confirm("Удаление участника", $"Удалить «{member.Name}» из группы «{group.Name}»?"))
        {
            return;
        }

        try
        {
            SetBusy(true, "Удаление участника…");
            await _service.RemoveGroupMemberAsync(group.DistinguishedName, member.DistinguishedName, CancellationToken.None);
            _audit.Write("Удаление из группы", member.DistinguishedName, group.Name);
            group.DetailsLoaded = false;
            await ReloadDataAsync();
            _dialogs.Info("Удаление участника", "Участник удалён из группы.");
        }
        catch (Exception ex)
        {
            _audit.Write("Удаление из группы", member.DistinguishedName, group.Name, false, ex.Message);
            _dialogs.Error("Удаление участника", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ExportUsersCsv()
    {
        var users = _selectedUsers.Count > 0
            ? _selectedUsers
            : UsersView.Cast<AdUser>().ToList();
        if (users.Count == 0)
        {
            _dialogs.Info("Экспорт", "Нет данных для экспорта.");
            return;
        }

        string? path = _dialogs.SaveFile("Экспорт пользователей", "CSV (*.csv)|*.csv|Все файлы (*.*)|*.*", "users.csv");
        if (path is null) return;
        ExportService.ExportUsersCsv(path, users);
        _dialogs.Info("Экспорт", "Сохранено: " + path);
    }

    private void ExportGroupsCsv()
    {
        var groups = _selectedGroups.Count > 0
            ? _selectedGroups
            : GroupsView.Cast<AdGroup>().ToList();
        if (groups.Count == 0)
        {
            _dialogs.Info("Экспорт", "Нет данных для экспорта.");
            return;
        }

        string? path = _dialogs.SaveFile("Экспорт групп", "CSV (*.csv)|*.csv|Все файлы (*.*)|*.*", "groups.csv");
        if (path is null) return;
        ExportService.ExportGroupsCsv(path, groups);
        _dialogs.Info("Экспорт", "Сохранено: " + path);
    }

    private void ExportIssuesCsv()
    {
        var issues = IssuesView.Cast<ReviewIssue>().ToList();
        if (issues.Count == 0)
        {
            _dialogs.Info("Экспорт", "Нет проблем для экспорта.");
            return;
        }

        string? path = _dialogs.SaveFile("Экспорт проблем", "CSV (*.csv)|*.csv|Все файлы (*.*)|*.*", "access-review.csv");
        if (path is null) return;
        ExportService.ExportIssuesCsv(path, issues);
        _dialogs.Info("Экспорт", "Сохранено: " + path);
    }

    private void ExportHtml()
    {
        string? path = _dialogs.SaveFile("HTML-отчёт", "HTML (*.html)|*.html|Все файлы (*.*)|*.*", "access-review.html");
        if (path is null) return;
        ExportService.ExportHtml(path, DomainName, _reviewResult, _allUsers, _allGroups);
        _dialogs.Info("Экспорт", "Сохранено: " + path);
    }

    private async Task OpenSettingsAsync()
    {
        var updated = _dialogs.EditSettings(_settings.Clone());
        if (updated is null) return;

        _settingsService.Apply(updated);
        _settings = _settingsService.Current;
        _review = new AccessReviewService(_settings);
        IncludeDisabled = _settings.IncludeDisabled;
        Recursive = _settings.Recursive;
        StaleDays = _settings.StaleDays;
        await ConnectAsync();
    }

    private void CopyUserAccess()
    {
        var user = SelectedUser ?? TargetUsers().FirstOrDefault();
        if (user is null)
        {
            _dialogs.Info("Копирование доступов", "Выберите пользователя.");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("AdManager — доступы учётной записи");
        sb.AppendLine($"Пользователь: {user.DisplayLabel} ({user.SamAccountName})");
        sb.AppendLine($"UPN: {user.UpnDisplay}");
        sb.AppendLine($"DN: {user.DistinguishedName}");
        sb.AppendLine($"Подразделение: {user.OuDisplay}");
        sb.AppendLine($"Статус: {user.StatusText}");
        sb.AppendLine($"Отдел: {user.DepartmentDisplay}");
        sb.AppendLine($"Должность: {user.TitleDisplay}");
        sb.AppendLine($"Руководитель: {user.ManagerDisplay}");
        sb.AppendLine($"Почта: {user.MailDisplay}");
        sb.AppendLine($"Последний вход: {user.LastLogonText}");
        sb.AppendLine($"Пароль установлен: {user.PasswordLastSetText}");
        sb.AppendLine($"Пароль истекает: {user.PasswordExpiresText}");
        sb.AppendLine($"Срок действия УЗ: {user.AccountExpiresText}");
        sb.AppendLine($"Привилегированная УЗ: {(user.IsPrivileged ? "да" : "нет")}");
        sb.AppendLine();

        sb.AppendLine($"Привилегированные группы ({user.PrivilegedGroups.Count}):");
        AppendCopyList(sb, user.PrivilegedGroups);
        sb.AppendLine();

        var directGroups = user.MemberOf
            .Select(AdValue.DnToName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        sb.AppendLine($"Прямое членство в группах ({directGroups.Count}):");
        AppendCopyList(sb, directGroups);
        sb.AppendLine();

        sb.AppendLine($"Эффективные группы, включая вложенность ({user.EffectiveGroups.Count}):");
        AppendCopyList(sb, user.EffectiveGroups);
        sb.AppendLine();

        var flags = new List<string>();
        if (user.Locked) flags.Add("УЗ заблокирована");
        if (user.AdminCount) flags.Add("adminCount=1");
        if (user.HasSpn) flags.Add("SPN: " + string.Join(", ", user.Spn));
        if (user.DontRequirePreAuth) flags.Add("AS-REP: не требуется preauth");
        if (user.TrustedForDelegation) flags.Add("Unconstrained delegation");
        if (user.HasConstrainedDelegation) flags.Add("Constrained delegation: " + string.Join(", ", user.AllowedToDelegateTo));
        if (user.TrustedToAuthForDelegation) flags.Add("Protocol transition");
        if (user.HasSidHistory) flags.Add("SID History: " + string.Join(", ", user.SidHistory));
        if (user.PasswordNeverExpires) flags.Add("Пароль не истекает");
        if (user.PasswordNotRequired) flags.Add("Пароль не требуется");
        if (user.UseDesKeyOnly) flags.Add("DES only");
        if (user.NoAuthDataRequired) flags.Add("No auth data required");

        sb.AppendLine($"Признаки УЗ ({flags.Count}):");
        if (flags.Count == 0)
        {
            sb.AppendLine("  —");
        }
        else
        {
            foreach (string flag in flags)
            {
                sb.AppendLine("  • " + flag);
            }
        }

        sb.AppendLine();
        sb.AppendLine($"Проблемы ({user.RiskFlags.Count}):");
        if (user.RiskFlags.Count == 0)
        {
            sb.AppendLine("  —");
        }
        else
        {
            foreach (var issue in user.RiskFlags.OrderBy(f => f.Severity).ThenBy(f => f.Name))
            {
                sb.AppendLine($"  • [{issue.SeverityText}] {issue.Name} — {issue.Details}");
            }
        }

        try
        {
            Clipboard.SetDataObject(sb.ToString(), true);
            StatusText = "Доступы скопированы в буфер обмена: " + user.DisplayLabel;
        }
        catch (Exception ex)
        {
            _dialogs.Error("Копирование доступов",
                "Не удалось скопировать в буфер обмена: " + ex.Message);
        }
    }

    private static void AppendCopyList(StringBuilder sb, IEnumerable<string> values)
    {
        var items = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (items.Count == 0)
        {
            sb.AppendLine("  —");
            return;
        }

        foreach (string item in items)
        {
            sb.AppendLine("  • " + item);
        }
    }

    private async Task CopyUserAsync()
    {
        var source = SelectedUser ?? TargetUsers().FirstOrDefault();
        if (source is null)
        {
            _dialogs.Info("Копирование УЗ", "Выберите пользователя.");
            return;
        }

        try
        {
            SetBusy(true, "Чтение групп пользователя…");
            try
            {
                await _service.LoadUserDetailsAsync(source, CancellationToken.None);
            }
            catch
            {
                // Если не удалось перечитать детали, используем уже загруженный memberOf.
            }

            var groups = source.MemberOf
                .Where(dn => !string.IsNullOrWhiteSpace(dn))
                .Where(dn => !dn.Equals(source.PrimaryGroupDn, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string parent = AdValue.DnToOu(source.DistinguishedName);
            if (string.IsNullOrWhiteSpace(parent))
            {
                parent = _service.RootDn;
            }

            var request = _dialogs.CopyUser(source, parent, AllOus, groups.Count);
            if (request is null) return;

            SetBusy(true, "Создание копии УЗ…");
            string newDn = await _service.CreateUserAsync(request, CancellationToken.None);
            _audit.Write("Копирование УЗ", newDn,
                source.DistinguishedName + " -> " + request.SamAccountName);

            var errors = new List<string>();
            int copied = 0;
            if (request.CopyGroups)
            {
                foreach (string groupDn in groups)
                {
                    try
                    {
                        await _service.AddGroupMemberAsync(groupDn, newDn, CancellationToken.None);
                        copied++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add(AdValue.DnToName(groupDn) + ": " + ex.Message);
                    }
                }
            }

            await ReloadDataAsync();

            var created = _allUsers.FirstOrDefault(u =>
                              u.DistinguishedName.Equals(newDn, StringComparison.OrdinalIgnoreCase))
                          ?? _allUsers.FirstOrDefault(u =>
                              u.SamAccountName.Equals(request.SamAccountName, StringComparison.OrdinalIgnoreCase));
            if (created is not null)
            {
                TabIndex = 0;
                SelectedUser = created;
            }

            string message = $"Учётная запись {request.SamAccountName} создана.";
            if (request.CopyGroups)
            {
                message += $"\nСкопировано групп: {copied}.";
            }

            if (errors.Count > 0)
            {
                message += "\n\nОшибки копирования групп:\n" + string.Join("\n", errors.Take(20));
                _dialogs.Error("Копирование УЗ", message);
            }
            else
            {
                _dialogs.Info("Копирование УЗ", message);
            }
        }
        catch (Exception ex)
        {
            _audit.Write("Копирование УЗ", source.DistinguishedName, source.SamAccountName, false, ex.Message);
            _dialogs.Error("Копирование УЗ", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OpenAuditFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "\"" + _audit.Directory + "\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _dialogs.Error("Журнал", ex.Message);
        }
    }

    private async Task RunDiagnosticsAsync()
    {
        try
        {
            SetBusy(true, "Сбор диагностики…");
            var sb = new StringBuilder();
            sb.AppendLine("AdManager diagnostics");
            sb.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            sb.AppendLine("Version: " + typeof(MainViewModel).Assembly.GetName().Version);
            sb.AppendLine("OS: " + Environment.OSVersion);
            sb.AppendLine("Runtime: " + Environment.Version);
            sb.AppendLine("64-bit process: " + Environment.Is64BitProcess);
            sb.AppendLine();

            sb.AppendLine("=== SETTINGS ===");
            sb.AppendLine($"Domain={_settings.Domain}; Server={_settings.Server}; UseDemo={_settings.UseDemo}");
            sb.AppendLine($"StaleDays={_settings.StaleDays}; PasswordExpiringDays={_settings.PasswordExpiringDays}; " +
                          $"PageSize={_settings.PageSize}; MaxObjects={_settings.MaxObjects}; IncludeDisabled={_settings.IncludeDisabled}; " +
                          $"Recursive={_settings.Recursive}");
            sb.AppendLine("PrivilegedGroups=" + string.Join("; ", _settings.PrivilegedGroups));
            sb.AppendLine();

            sb.AppendLine("=== VIEW ===");
            sb.AppendLine($"SelectedOu={SelectedOu?.DistinguishedName ?? "(null)"}; SelectedOuPath={SelectedOuPath}");
            sb.AppendLine($"Recursive={Recursive}; SearchText={SearchText}; IncludeDisabled={IncludeDisabled}; " +
                          $"OnlyProblems={OnlyProblems}; ActiveFilter={ActiveFilter}; GroupFilter={GroupFilter}; TabIndex={TabIndex}");
            sb.AppendLine($"Users={_allUsers.Count}; Groups={_allGroups.Count}; ReferenceGroups={_referenceGroups.Count}; Issues={_reviewResult.Issues.Count}");
            sb.AppendLine("KPI=" + string.Join("; ", KpiItems.Select(k => $"{k.Label}={k.Value}")));
            sb.AppendLine();

            sb.AppendLine("=== LOADED GROUPS ===");
            foreach (var group in _allGroups.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                sb.AppendLine($"{group.Name} | sam={group.SamAccountName} | sid={group.ObjectSid} | " +
                              $"type={group.GroupTypeText} | members={group.MemberCount} | " +
                              $"privileged={group.IsPrivileged} | nested={group.IsNestedPrivileged} | " +
                              $"managedBy={group.ManagedByDn} | dn={group.DistinguishedName}");
            }

            sb.AppendLine();
            int emptyMemberOf = _allUsers.Count(u => u.MemberOf.Count == 0);
            sb.AppendLine($"=== USERS WITH EMPTY memberOf: {emptyMemberOf} of {_allUsers.Count} ===");
            foreach (var user in _allUsers.Where(u => u.MemberOf.Count == 0).Take(100))
            {
                sb.AppendLine($"{user.DisplayLabel} | sam={user.SamAccountName} | enabled={user.Enabled} | " +
                              $"uac=0x{user.UserAccountControl:X} | dn={user.DistinguishedName}");
            }

            if (SelectedUser is not null)
            {
                sb.AppendLine();
                sb.AppendLine("=== SELECTED USER GROUP CROSS-CHECK ===");
                sb.AppendLine($"User={SelectedUser.DisplayLabel} | sam={SelectedUser.SamAccountName} | " +
                              $"enabled={SelectedUser.Enabled} | locked={SelectedUser.Locked} | " +
                              $"status={SelectedUser.StatusText} | " +
                              $"uac=0x{SelectedUser.UserAccountControl:X} | memberOf={SelectedUser.MemberOf.Count}");
                foreach (string dn in SelectedUser.MemberOf)
                {
                    AdGroup? shown = _allGroups.FirstOrDefault(g =>
                        g.DistinguishedName.Equals(dn, StringComparison.OrdinalIgnoreCase));
                    AdGroup? reference = _referenceGroups.FirstOrDefault(g =>
                        g.DistinguishedName.Equals(dn, StringComparison.OrdinalIgnoreCase));
                    sb.AppendLine($"{dn} | inUI={shown is not null} | inReference={reference is not null} | " +
                                  $"name={reference?.Name ?? shown?.Name ?? "?"} | privileged={reference?.IsPrivileged ?? shown?.IsPrivileged}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("=== RAW SERVICE DIAGNOSTICS ===");
            sb.AppendLine(await _service.BuildDiagnosticsAsync(
                SelectedUser,
                SelectedGroup,
                SelectedOu?.DistinguishedName,
                Recursive,
                SearchText,
                CancellationToken.None));

            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AdManager", "diagnostics");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"AdManager-diag-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(true));

            _dialogs.Info("Диагностика",
                "Файл сохранён:\n\n" + path + "\n\nОтправьте этот файл разработчику — в нём данные о подключении, группах и выбранной УЗ.");

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "/select,\"" + path + "\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _dialogs.Error("Диагностика", ex.ToString());
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy, string text = "")
    {
        IsBusy = busy;
        BusyText = text;
    }

    public void Dispose()
    {
        _loadCts?.Cancel();
        _detailsCts?.Cancel();
        _service.Dispose();
    }

    public void SaveWindowSize(double width, double height)
    {
        if (width < 1000 || height < 650 || width > 10000 || height > 10000)
        {
            return;
        }

        _settings.WindowWidth = width;
        _settings.WindowHeight = height;
        _settingsService.Save();
    }
}

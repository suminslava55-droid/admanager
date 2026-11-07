using AdManager.Models;

namespace AdManager.Services;

/// <summary>Единый контракт для живого AD и демо-режима.</summary>
public interface IAdDirectoryService : IDisposable
{
    bool IsConnected { get; }
    bool IsDemo { get; }
    string DomainName { get; }
    string ServerName { get; }
    string RootDn { get; }
    string CurrentUser { get; }
    string ConnectionError { get; }

    Task ConnectAsync(string? domain, string? server, CancellationToken ct);
    Task<List<AdOu>> LoadOusAsync(CancellationToken ct);
    Task<List<AdUser>> LoadUsersAsync(string? baseDn, bool recursive, string? search, bool includeDisabled, int max, CancellationToken ct);
    Task<List<AdGroup>> LoadGroupsAsync(string? baseDn, bool recursive, string? search, int max, CancellationToken ct);
    Task<List<AdGroup>> SearchGroupsAsync(string? search, int max, CancellationToken ct);
    Task LoadUserDetailsAsync(AdUser user, CancellationToken ct);
    Task LoadGroupDetailsAsync(AdGroup group, CancellationToken ct);
    Task<List<AdMember>> LoadGroupMembersAsync(AdGroup group, CancellationToken ct);
    Task<List<string>> LoadUserEffectiveGroupDnsAsync(AdUser user, CancellationToken ct);
    Task<string> BuildDiagnosticsAsync(AdUser? user, AdGroup? group, string? baseDn, bool recursive, string? search, CancellationToken ct);

    Task SetUserEnabledAsync(AdUser user, bool enabled, CancellationToken ct);
    Task UnlockUserAsync(AdUser user, CancellationToken ct);
    Task ResetPasswordAsync(AdUser user, string password, bool mustChange, CancellationToken ct);
    Task SetPasswordNeverExpiresAsync(AdUser user, bool value, CancellationToken ct);
    Task SetMustChangePasswordAsync(AdUser user, CancellationToken ct);
    Task ResetPasswordAgeAsync(AdUser user, CancellationToken ct);
    Task SetAccountExpirationAsync(AdUser user, DateTime? expiration, CancellationToken ct);
    Task MoveObjectAsync(string dn, string newParentDn, CancellationToken ct);
    Task AddToGroupAsync(string groupDn, string memberDn, CancellationToken ct);
    Task RemoveFromGroupAsync(string groupDn, string memberDn, CancellationToken ct);
    Task UpdateUserAttributesAsync(AdUser user, IReadOnlyDictionary<string, string?> attributes, CancellationToken ct);
    Task<string> CreateUserAsync(NewUserRequest request, CancellationToken ct);
    Task DeleteUserAsync(AdUser user, CancellationToken ct);
    Task CreateGroupAsync(NewGroupRequest request, CancellationToken ct);
    Task DeleteGroupAsync(AdGroup group, CancellationToken ct);
    Task AddGroupMemberAsync(string groupDn, string memberDn, CancellationToken ct);
    Task RemoveGroupMemberAsync(string groupDn, string memberDn, CancellationToken ct);
}

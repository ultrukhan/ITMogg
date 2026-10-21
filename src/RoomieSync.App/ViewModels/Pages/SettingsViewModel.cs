using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomieSync.App.Localization;
using RoomieSync.App.Services;
using RoomieSync.App.ViewModels.Dialogs;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Models;

namespace RoomieSync.App.ViewModels.Pages;

public sealed class MemberSettingsItem(Member member, bool canDelete, string? email, bool isMe)
{
    public Member Member { get; } = member;
    public bool CanDelete { get; } = canDelete;
    public bool ShowDebtNote => !CanDelete && !Member.IsAdmin;
    public string? Email { get; } = email;
    public bool HasLogin => !string.IsNullOrEmpty(Email);
    public bool IsMe { get; } = isMe;
}


public sealed partial class SettingsViewModel(
    ISessionContext session,
    IHouseholdService households,
    IAuthService auth,
    IDialogService dialogs,
    IToastService toast) : PageViewModel
{
    public override string TitleKey => "S.Settings.Title";
    public override string CaptionKey => "S.Settings.Caption";
    public override NavSection Section => NavSection.Settings;

    public ObservableCollection<MemberSettingsItem> Members { get; } = new();

    [ObservableProperty] private string _householdName = string.Empty;
    [ObservableProperty] private string _newMemberName = string.Empty;
    [ObservableProperty] private bool _canAddMore = true;

    public bool IsAdmin => session.CurrentMember?.IsAdmin == true;
    public string MemberLimitText => Loc.Format("S.Settings.MemberLimit", Members.Count, Household.MaxMembers);

    public override Task LoadAsync(object? parameter) => RunAsync(async () =>
    {
        if (session.CurrentHousehold is not { } h) return;
        HouseholdName = h.Name;
        var list = await households.GetMembersAsync(h.Id);
        Members.Clear();
        foreach (var m in list)
            Members.Add(new MemberSettingsItem(m, await households.CanDeleteMemberAsync(m.Id), await auth.GetEmailAsync(m.Id), m.Id == session.CurrentMember?.Id));
        CanAddMore = IsAdmin && Members.Count < Household.MaxMembers;
        OnPropertyChanged(nameof(MemberLimitText));
        OnPropertyChanged(nameof(IsAdmin));
    }, toast);

    [RelayCommand]
    private Task SaveHousehold() => RunAsync(async () =>
    {
        if (session.CurrentHousehold is not { } h) return;
        var updated = await households.RenameAsync(h.Id, HouseholdName);
        session.UpdateHousehold(updated);
        toast.Show(Loc.Get("S.Settings.Saved"));
    }, toast);

    [RelayCommand]
    private Task AddMember() => RunAsync(async () =>
    {
        if (session.CurrentHousehold is not { } h) return;
        var m = await households.AddMemberAsync(h.Id, NewMemberName);
        NewMemberName = string.Empty;
        toast.Show(Loc.Format("S.Settings.MemberAdded", m.DisplayName));
        await LoadAsync(null);
        session.UpdateHousehold(h); 
    }, toast);

    [RelayCommand]
    private async Task EditMember(MemberSettingsItem item)
    {
        var dialog = new MemberEditDialogViewModel(households, auth, item.Member, item.Email);
        if (!await dialogs.ShowAsync(dialog)) return;
        if (item.IsMe && dialog.UpdatedMember is { } updated) session.UpdateMember(updated);
        toast.Show(Loc.Get("S.Settings.MemberUpdated"));
        await LoadAsync(null);
    }

    [RelayCommand]
    private async Task DeleteMember(MemberSettingsItem item)
    {
        if (!await dialogs.ConfirmAsync(Loc.Get("S.Settings.DeleteTitle"), Loc.Format("S.Settings.DeleteMessage", item.Member.DisplayName), Loc.Get("S.Common.Delete"), danger: true))
            return;
        await RunAsync(async () =>
        {
            await households.DeleteMemberAsync(item.Member.Id);
            toast.Show(Loc.Format("S.Settings.MemberDeleted", item.Member.DisplayName));
            await LoadAsync(null);
            if (session.CurrentHousehold is { } h) session.UpdateHousehold(h);
        }, toast);
    }
}

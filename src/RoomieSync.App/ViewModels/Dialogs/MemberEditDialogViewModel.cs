using CommunityToolkit.Mvvm.ComponentModel;
using RoomieSync.App.Localization;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Common;
using RoomieSync.Core.Models;

namespace RoomieSync.App.ViewModels.Dialogs;


public sealed partial class MemberEditDialogViewModel(IHouseholdService households, IAuthService auth, Member member, string? email) : DialogViewModel
{
    public override string Title => Loc.Get("S.Settings.EditMember");
    public Member Member { get; } = member;
    public Member? UpdatedMember { get; private set; }

    [ObservableProperty] private string _displayName = member.DisplayName;
    [ObservableProperty] private string _email = email ?? string.Empty;
    [ObservableProperty] private string _password = string.Empty;

    public bool HasLogin => !string.IsNullOrEmpty(email);

    protected override async Task<bool> OnConfirmAsync()
    {
        var emailChanged = !string.Equals(Email.Trim(), email ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        if (emailChanged || Password.Length > 0)
        {
            if (string.IsNullOrWhiteSpace(Email)) throw new DomainException(Loc.Get("S.Settings.EmailRequired"));
            if (Password.Length == 0) throw new DomainException(Loc.Get("S.Settings.PasswordRequired"));
        }

        if (DisplayName.Trim() != Member.DisplayName)
            UpdatedMember = await households.RenameMemberAsync(Member.Id, DisplayName);
        if (Password.Length > 0)
            await auth.SetCredentialsAsync(Member.Id, Email, Password);
        Password = string.Empty;
        return true;
    }
}

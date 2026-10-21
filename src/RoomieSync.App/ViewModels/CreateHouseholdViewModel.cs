using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomieSync.App.Localization;
using RoomieSync.App.Services;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Models;

namespace RoomieSync.App.ViewModels;

public sealed partial class MemberNameEntry : ObservableObject
{
	[ObservableProperty]
	private string _name = string.Empty;
}


public sealed partial class CreateHouseholdViewModel : ViewModelBase
{
	private readonly IHouseholdService _households;
	private readonly IAuthService _auth;
	private readonly ISessionContext _session;
	private readonly IAppNavigator _navigator;

	public CreateHouseholdViewModel(IHouseholdService households, IAuthService auth, ISessionContext session, IAppNavigator navigator)
	{
		_households = households;
		_auth = auth;
		_session = session;
		_navigator = navigator;
		for (var i = 0; i < 3; i++) Members.Add(new MemberNameEntry());
		_ = CheckExistingAsync();
	}

	[ObservableProperty] private string _householdName = string.Empty;
	[ObservableProperty] private string _adminName = string.Empty;
	[ObservableProperty] private string _adminEmail = string.Empty;
	[ObservableProperty] private string _adminPassword = string.Empty;
	[ObservableProperty] private string _confirmPassword = string.Empty;
	[ObservableProperty] private string? _errorMessage;
	[ObservableProperty] private bool _canGoBack;

	public ObservableCollection<MemberNameEntry> Members { get; } = new();

	private async Task CheckExistingAsync()
	{
		try { CanGoBack = await _households.AnyHouseholdExistsAsync(); }
		catch { CanGoBack = true; }
	}

	[RelayCommand]
	private void AddMember()
	{
		
		if (Members.Count + 1 >= Household.MaxMembers)
		{
			ErrorMessage = Loc.Format("S.Setup.MaxMembers", Household.MaxMembers);
			return;
		}
		Members.Add(new MemberNameEntry());
	}

	[RelayCommand]
	private void RemoveMember(MemberNameEntry entry)
	{
		if (Members.Count > 1) Members.Remove(entry);
		else entry.Name = string.Empty;
	}

	[RelayCommand]
	private Task Back() => _navigator.ShowLoginAsync();

	[RelayCommand]
	private async Task CompleteSetup()
	{
		ErrorMessage = null;
		if (AdminPassword != ConfirmPassword)
		{
			ErrorMessage = Loc.Get("S.Setup.PasswordMismatch");
			return;
		}
		try
		{
			IsBusy = true;
			var (household, admin) = await _households.CreateHouseholdAsync(new NewHouseholdRequest(
				HouseholdName, AdminName, AdminEmail, AdminPassword, Members.Select(m => m.Name).ToList()));
			var login = await _auth.LoginAsync(AdminEmail, AdminPassword);
			_session.SignIn(login.Member ?? admin, login.Household ?? household);
			AdminPassword = ConfirmPassword = string.Empty;
			await _navigator.ShowShellAsync();
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
		finally
		{
			IsBusy = false;
		}
	}
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using RoomieSync.App.Localization;
using RoomieSync.App.Services;
using RoomieSync.App.ViewModels.Dialogs;
using RoomieSync.App.ViewModels.Items;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Models;

namespace RoomieSync.App.ViewModels.Pages;


public sealed partial class ChoresViewModel(
    ISessionContext session,
    IChoreService chores,
    IHouseholdService households,
    IDialogService dialogs,
    IToastService toast,
    IClock clock) : PageViewModel
{
    private IReadOnlyList<Member> _members = Array.Empty<Member>();

    public override string TitleKey => "S.Chores.Title";
    public override string CaptionKey => "S.Chores.Caption";
    public override NavSection Section => NavSection.Chores;

    public ObservableCollection<ChoreItem> ToDo { get; } = new();
    public ObservableCollection<ChoreItem> InProgress { get; } = new();
    public ObservableCollection<ChoreItem> Done { get; } = new();

    public override Task LoadAsync(object? parameter) => RunAsync(async () =>
    {
        if (session.CurrentHousehold is not { } h) return;
        _members = await households.GetMembersAsync(h.Id);
        var byId = _members.ToDictionary(m => m.Id);
        var list = await chores.GetChoresAsync(h.Id);
        var now = clock.Now;

        ChoreItem Item(Chore c) => new(c, c.AssigneeId is { } id ? byId.GetValueOrDefault(id) : null, now);

        ToDo.Clear();
        InProgress.Clear();
        Done.Clear();
        foreach (var c in list.Where(c => c.Status == ChoreStatus.ToDo).OrderBy(c => c.Deadline ?? DateTime.MaxValue)) ToDo.Add(Item(c));
        foreach (var c in list.Where(c => c.Status == ChoreStatus.InProgress).OrderBy(c => c.Deadline ?? DateTime.MaxValue)) InProgress.Add(Item(c));
       
        foreach (var c in list.Where(c => c.Status == ChoreStatus.Done && (c.CompletedAt ?? now) > now.AddDays(-14)).OrderByDescending(c => c.CompletedAt))
            Done.Add(Item(c));
    }, toast);

    [RelayCommand]
    private async Task CreateTask()
    {
        if (session.CurrentHousehold is not { } h) return;
        if (await dialogs.ShowAsync(new ChoreEditorDialogViewModel(chores, h.Id, _members, null, clock)))
        {
            toast.Show(Loc.Get("S.Chores.Created"));
            await LoadAsync(null);
        }
    }

    [RelayCommand]
    private async Task Edit(ChoreItem item)
    {
        if (session.CurrentHousehold is not { } h) return;
        var dialog = new ChoreEditorDialogViewModel(chores, h.Id, _members, item.Chore, clock);
        if (!await dialogs.ShowAsync(dialog)) return;
        if (dialog.DeleteRequested)
        {
            if (!await dialogs.ConfirmAsync(Loc.Get("S.Chores.DeleteTitle"), Loc.Format("S.Chores.DeleteMessage", item.Title), Loc.Get("S.Common.Delete"), danger: true))
                return;
            await RunAsync(() => chores.DeleteChoreAsync(item.Chore.Id), toast);
            toast.Show(Loc.Get("S.Chores.Deleted"));
        }
        await LoadAsync(null);
    }

    [RelayCommand]
    private async Task ChangeAssignee(ChoreItem item)
    {
        var dialog = new AssigneeDialogViewModel(item.Title, _members, item.Chore.AssigneeId);
        if (!await dialogs.ShowAsync(dialog)) return;
        await RunAsync(async () =>
        {
            await chores.AssignAsync(item.Chore.Id, dialog.SelectedMember?.Id);
            toast.Show(Loc.Format("S.Chores.Reassigned", dialog.SelectedMember?.FirstName ?? Loc.Get("S.Chores.Unassigned")));
            await LoadAsync(null);
        }, toast);
    }

    
    [RelayCommand]
    private Task Advance(ChoreItem item) => SetStatus(item, item.Status switch
    {
        ChoreStatus.ToDo => ChoreStatus.InProgress,
        ChoreStatus.InProgress => ChoreStatus.Done,
        _ => ChoreStatus.ToDo
    });

    [RelayCommand]
    private Task MarkDone(ChoreItem item) => SetStatus(item, ChoreStatus.Done);

    private Task SetStatus(ChoreItem item, ChoreStatus status) => RunAsync(async () =>
    {
        var rotated = await chores.ChangeStatusAsync(item.Chore.Id, status);
        if (status == ChoreStatus.Done)
        {
            var nextName = rotated?.AssigneeId is { } id ? _members.FirstOrDefault(m => m.Id == id)?.FirstName : null;
            toast.Show(nextName is null ? Loc.Get("S.Chores.MarkedDone") : Loc.Format("S.Chores.RotatedTo", nextName));
        }
        await LoadAsync(null);
    }, toast);
}

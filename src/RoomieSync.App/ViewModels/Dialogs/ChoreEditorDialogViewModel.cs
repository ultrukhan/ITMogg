using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomieSync.App.Localization;
using RoomieSync.App.ViewModels.Items;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Common;
using RoomieSync.Core.Models;

namespace RoomieSync.App.ViewModels.Dialogs;

public sealed record RotationOption(ChoreRotation Value, string Name)
{
    public override string ToString() => Name;
}

public sealed record StatusOption(ChoreStatus Value, string Name)
{
    public override string ToString() => Name;
}


public sealed partial class ChoreEditorDialogViewModel : DialogViewModel
{
    private readonly IChoreService _chores;
    private readonly Guid _householdId;
    private readonly Chore? _existing;

    public ChoreEditorDialogViewModel(IChoreService chores, Guid householdId, IReadOnlyList<Member> members, Chore? existing, IClock clock)
    {
        _chores = chores;
        _householdId = householdId;
        _existing = existing;

        Assignees = new[] { new MemberOption(null, Loc.Get("S.Chores.Unassigned")) }
            .Concat(members.Select(m => new MemberOption(m, m.DisplayName))).ToList();
        Rotations = Enum.GetValues<ChoreRotation>().Select(r => new RotationOption(r, Loc.Get($"S.Rotation.{r}"))).ToList();
        Statuses = Enum.GetValues<ChoreStatus>().Select(s => new StatusOption(s, Loc.Get($"S.Status.{s}"))).ToList();

        var deadline = existing?.Deadline ?? clock.Now.Date.AddDays(1).AddHours(18);
        _choreTitle = existing?.Title ?? string.Empty;
        _selectedAssignee = Assignees.FirstOrDefault(a => a.Member?.Id == existing?.AssigneeId && existing?.AssigneeId != null)
                            ?? (existing is null && members.Count > 0 ? Assignees[1] : Assignees[0]);
        _deadlineDate = deadline.Date;
        _deadlineTime = deadline.ToString("HH:mm");
        _selectedRotation = Rotations.First(r => r.Value == (existing?.Rotation ?? ChoreRotation.Weekly));
        _selectedStatus = Statuses.First(s => s.Value == (existing?.Status ?? ChoreStatus.ToDo));
    }

    public override string Title => Loc.Get(IsEdit ? "S.Chores.EditTask" : "S.Chores.NewTask");
    public bool IsEdit => _existing is not null;
    public bool DeleteRequested { get; private set; }

    public IReadOnlyList<MemberOption> Assignees { get; }
    public IReadOnlyList<RotationOption> Rotations { get; }
    public IReadOnlyList<StatusOption> Statuses { get; }

    [ObservableProperty] private string _choreTitle;
    [ObservableProperty] private MemberOption _selectedAssignee;
    [ObservableProperty] private DateTime? _deadlineDate;
    [ObservableProperty] private string _deadlineTime;
    [ObservableProperty] private RotationOption _selectedRotation;
    [ObservableProperty] private StatusOption _selectedStatus;

    private DateTime? Deadline()
    {
        if (DeadlineDate is not { } d) return null;
        return TimeSpan.TryParse(DeadlineTime, out var t) ? d.Date + t : d.Date.AddHours(18);
    }

    protected override async Task<bool> OnConfirmAsync()
    {
        if (string.IsNullOrWhiteSpace(ChoreTitle)) throw new DomainException(Loc.Get("S.Chores.TitleRequired"));
        if (_existing is null)
        {
            await _chores.CreateChoreAsync(_householdId, new NewChoreRequest(ChoreTitle, SelectedAssignee.Member?.Id, Deadline(), SelectedRotation.Value));
        }
        else
        {
            var statusChanged = _existing.Status != SelectedStatus.Value;
            _existing.Title = ChoreTitle.Trim();
            _existing.AssigneeId = SelectedAssignee.Member?.Id;
            _existing.Deadline = Deadline();
            _existing.Rotation = SelectedRotation.Value;
            await _chores.UpdateChoreAsync(_existing);
            if (statusChanged) await _chores.ChangeStatusAsync(_existing.Id, SelectedStatus.Value);
        }
        return true;
    }

    [RelayCommand]
    private void Delete()
    {
        DeleteRequested = true;
        Close(true);
    }
}


public sealed partial class AssigneeDialogViewModel : DialogViewModel
{
    public AssigneeDialogViewModel(string choreTitle, IReadOnlyList<Member> members, Guid? current)
    {
        ChoreTitle = choreTitle;
        Members = members;
        _selectedMember = members.FirstOrDefault(m => m.Id == current);
    }

    public override string Title => Loc.Get("S.Chores.ChangeAssignee");
    public override double Width => 400;
    public string ChoreTitle { get; }
    public IReadOnlyList<Member> Members { get; }

    [ObservableProperty] private Member? _selectedMember;

    [RelayCommand]
    private void Pick(Member member)
    {
        SelectedMember = member;
        Close(true);
    }
}

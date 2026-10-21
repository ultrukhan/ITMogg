using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RoomieSync.App.Services;

public enum ToastKind { Success, Error, Info }

public sealed record ToastMessage(string Text, ToastKind Kind);

public interface IToastService
{
    ToastMessage? Current { get; }
    void Show(string text, ToastKind kind = ToastKind.Success);
}


public sealed class ToastService : ObservableObject, IToastService
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private ToastMessage? _current;

    public ToastService()
    {
        _timer.Tick += (_, _) => { _timer.Stop(); Current = null; };
    }

    public ToastMessage? Current
    {
        get => _current;
        private set => SetProperty(ref _current, value);
    }

    public void Show(string text, ToastKind kind = ToastKind.Success)
    {
        Current = new ToastMessage(text, kind);
        _timer.Stop();
        _timer.Start();
    }
}

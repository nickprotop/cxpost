using SharpConsoleUI;
using SharpConsoleUI.Builders;
using CXPost.UI.Components;

namespace CXPost.UI.Dialogs;

public abstract class DialogBase<TResult>
{
    private readonly TaskCompletionSource<TResult> _tcs = new();
    protected TResult? Result { get; set; }
    protected Window Modal { get; private set; } = null!;
    protected ConsoleWindowSystem WindowSystem { get; private set; } = null!;

    public Task<TResult> ShowAsync(ConsoleWindowSystem windowSystem)
    {
        WindowSystem = windowSystem;
        Modal = CreateModal();
        BuildContent();
        AttachEventHandlers();
        WindowSystem.AddWindow(Modal);
        WindowSystem.SetActiveWindow(Modal);
        SetInitialFocus();
        return _tcs.Task;
    }

    protected virtual Window CreateModal()
    {
        var (w, h) = GetSize();
        var builder = new WindowBuilder(WindowSystem)
            .AsModal()
            .WithTitle(GetTitle())
            .WithSize(w, h)
            .Centered()
            .Resizable(GetResizable())
            .Movable(true)
            .Minimizable(false)
            .Maximizable(false)
            .WithColors(Color.Grey93, ColorScheme.WindowBackground)
            .WithBorderStyle(BorderStyle.DoubleLine)
            .WithBorderColor(ColorScheme.BorderColor);

        return builder.Build();
    }

    protected abstract void BuildContent();
    protected abstract string GetTitle();
    protected virtual (int width, int height) GetSize() => (60, 18);
    protected virtual bool GetResizable() => true;
    protected virtual void SetInitialFocus() { }
    protected virtual TResult GetDefaultResult() => default!;
    protected virtual void OnCleanup() { }

    protected void CloseWithResult(TResult result)
    {
        Result = result;
        Modal.Close();
    }

    protected virtual void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape)
        {
            CloseWithResult(GetDefaultResult());
            e.Handled = true;
        }
    }

    private void AttachEventHandlers()
    {
        Modal.KeyPressed += OnKeyPressed;
        Modal.OnClosed += OnModalClosed;
    }

    private void OnModalClosed(object? sender, EventArgs e)
    {
        OnCleanup();
        _tcs.TrySetResult(Result ?? GetDefaultResult());
    }
}

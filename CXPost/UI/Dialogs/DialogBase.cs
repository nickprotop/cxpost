using SharpConsoleUI;
using SharpConsoleUI.Animation;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Windows;
using CXPost.UI.Components;

namespace CXPost.UI.Dialogs;

public abstract class DialogBase<TResult>
{
    private readonly TaskCompletionSource<TResult> _tcs = new();
    protected TResult? Result { get; set; }
    protected Window Modal { get; private set; } = null!;
    protected ConsoleWindowSystem WindowSystem { get; private set; } = null!;

    // Dim overlay state
    private readonly List<(Window window, WindowRenderer.BufferPaintDelegate handler)> _dimOverlays = [];
    private float _dimIntensity;
    private const float DimTarget = 0.45f;

    public Task<TResult> ShowAsync(ConsoleWindowSystem windowSystem)
    {
        WindowSystem = windowSystem;
        Modal = CreateModal();
        BuildContent();
        AttachEventHandlers();

        // Apply dim overlay to all existing windows before showing modal
        ApplyDimOverlay();

        WindowSystem.AddWindow(Modal);
        WindowSystem.SetActiveWindow(Modal);

        // Animate the modal entrance
        PlayEnterAnimation();

        SetInitialFocus();
        return _tcs.Task;
    }

    protected virtual Window CreateModal()
    {
        var (w, h) = GetSize();
        var builder = new WindowBuilder(WindowSystem)
            .AsModal()
            .HideTitle()
            .WithSize(w, h)
            .Centered()
            .Resizable(GetResizable())
            .Movable(true)
            .Minimizable(false)
            .Maximizable(GetMaximizable())
            .WithColors(Color.Grey93, ColorScheme.WindowBackground)
            .WithBorderStyle(BorderStyle.Rounded)
            .WithBorderColor(Color.Grey35);

        return builder.Build();
    }

    protected abstract void BuildContent();
    protected abstract string GetTitle();
    protected virtual (int width, int height) GetSize() => (60, 18);
    protected virtual bool GetResizable() => true;
    protected virtual bool GetMaximizable() => false;
    protected virtual void SetInitialFocus() { }
    protected virtual TResult GetDefaultResult() => default!;
    protected virtual void OnCleanup() { }

    /// <summary>
    /// Override to change the modal entrance animation. Default is a quick fade-in.
    /// </summary>
    protected virtual void PlayEnterAnimation()
    {
        WindowAnimations.FadeIn(Modal, TimeSpan.FromMilliseconds(120));
    }

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

    private void ApplyDimOverlay()
    {
        _dimIntensity = DimTarget;
        var dimColor = Color.Black;

        foreach (var window in WindowSystem.Windows.Values)
        {
            if (window == Modal) continue;

            WindowRenderer.BufferPaintDelegate handler =
                (CharacterBuffer buffer, LayoutRect dirtyRegion, LayoutRect clipRect) =>
                {
                    if (_dimIntensity <= 0.01f) return;
                    ColorBlendHelper.ApplyColorOverlay(buffer, dimColor, _dimIntensity, 0.6f);
                };

            window.PostBufferPaint += handler;
            window.Invalidate(redrawAll: true);
            _dimOverlays.Add((window, handler));
        }
    }

    private void RemoveDimOverlay()
    {
        foreach (var (window, handler) in _dimOverlays)
        {
            window.PostBufferPaint -= handler;
            window.Invalidate(redrawAll: true);
        }
        _dimOverlays.Clear();
    }

    private void AttachEventHandlers()
    {
        Modal.KeyPressed += OnKeyPressed;
        Modal.OnClosed += OnModalClosed;
    }

    private void OnModalClosed(object? sender, EventArgs e)
    {
        RemoveDimOverlay();
        OnCleanup();
        _tcs.TrySetResult(Result ?? GetDefaultResult());
    }
}

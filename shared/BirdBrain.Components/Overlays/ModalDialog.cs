using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace BirdBrain.Components.Overlays;

/// <summary>Non-generic handle the host keeps to the active modal so a backdrop click can cancel it.</summary>
public interface IModalDialog
{
    /// <summary>Cancel the dialog from the host (e.g. a backdrop click).</summary>
    void CancelFromHost();
}

/// <summary>
/// Base for an <em>internal</em> modal dialog rendered inside an app's overlay host rather than a
/// separate OS <see cref="Window"/> — the same model as the command palette. A dialog builds its
/// content panel, assigns <see cref="DialogContent"/>, and finishes by calling <see cref="Close"/>
/// with its result; a host shows it (typically over a blurred/dimmed backdrop) and awaits
/// <see cref="Result"/>.
///
/// Mirrors the small slice of the <see cref="Window"/> API the dialogs used (<c>Title</c>,
/// <c>Close(result)</c>) so a dialog body reads the same whether shown as a window or an overlay.
/// Esc cancels (returns the type's default), Enter is handled by each dialog's default button.
/// </summary>
public abstract class ModalDialog<TResult> : ContentControl, IModalDialog
{
    private readonly TaskCompletionSource<TResult?> _completion = new();
    private bool _closed;

    protected ModalDialog()
    {
        // Keyboard-operable: the dialog is focusable so Esc reaches OnKeyDown even before a field
        // inside it takes focus, and it exposes an accessible name for VoiceOver.
        Focusable = true;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
    }

    /// <summary>The dialog's window title — shown as a heading on the internal modal's chrome.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The result returned to the awaiter once the dialog closes.</summary>
    public Task<TResult?> Result => _completion.Task;

    /// <summary>The dialog body. Derived dialogs assign this in their constructor.</summary>
    protected Control? DialogContent
    {
        get => Content as Control;
        set
        {
            Content = value;
            if (value is not null && !string.IsNullOrEmpty(Title))
            {
                AutomationProperties.SetName(value, Title);
            }
        }
    }

    /// <summary>Close the modal, returning <paramref name="result"/> to the awaiter (idempotent).</summary>
    public void Close(TResult? result = default)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _completion.TrySetResult(result);
        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised once when the dialog closes, so the host can tear down the overlay.</summary>
    public event EventHandler? Closed;

    /// <summary>Whether Esc cancels this dialog. Overridable for dialogs that own Esc themselves.</summary>
    protected virtual bool CancelOnEscape => true;

    /// <summary>The value returned when the user cancels (Esc / clicking the backdrop). Default is the type default.</summary>
    protected virtual TResult? CancelResult => default;

    /// <summary>The control a dialog wants focused when it opens (e.g. its text field). Null = focus the dialog.</summary>
    protected Control? InitialFocus { get; set; }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Defer so the template is realized before we grab focus (mirrors the old Window.Opened hook).
        Avalonia.Threading.Dispatcher.UIThread.Post(() => (InitialFocus ?? (Control)this).Focus());
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (CancelOnEscape && e.Key == Key.Escape)
        {
            Close(CancelResult);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>Cancel the dialog from the host (e.g. a backdrop click), returning <see cref="CancelResult"/>.</summary>
    public void CancelFromHost() => Close(CancelResult);
}

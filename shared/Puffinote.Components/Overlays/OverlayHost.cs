using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Puffinote.Components.Theming;

namespace Puffinote.Components.Overlays;

/// <summary>
/// The shared host for in-app overlays — command palettes and internal <see cref="ModalDialog{TResult}"/>
/// dialogs — rendered above the app's content instead of in a separate OS window. It manages the overlay
/// lifecycle (show / dismiss), backdrop-click and <see cref="TryCancel">Esc</see> dismissal, and wraps a
/// modal body in a themed floating card with Tab trapped inside it.
///
/// App-specific side effects (blurring the content behind, suspending background shortcuts, restoring
/// focus on close) are <em>not</em> baked in: the host raises <see cref="Opened"/> / <see cref="Closed"/>
/// so each app wires its own. The scrim color stays app-controlled via this panel's
/// <see cref="Panel.Background"/>. Place one in the app's root layout, above the content it covers.
/// </summary>
public sealed class OverlayHost : Panel
{
    private IModalDialog? _activeModal;

    public OverlayHost()
    {
        IsVisible = false;
    }

    /// <summary>Whether an overlay is currently shown.</summary>
    public bool IsOpen => IsVisible;

    /// <summary>Raised when an overlay opens (so the app can blur the background, suspend shortcuts, …).</summary>
    public event EventHandler? Opened;

    /// <summary>Raised when the overlay closes (so the app can clear the blur and restore focus).</summary>
    public event EventHandler? Closed;

    /// <summary>
    /// Show arbitrary overlay content (e.g. a command-palette card that brings its own chrome). Not a
    /// result-returning modal; a backdrop click or <see cref="TryCancel"/> simply closes it. The caller
    /// focuses whatever it wants once shown.
    /// </summary>
    public void ShowContent(Control content)
    {
        Children.Clear();
        _activeModal = null;
        Children.Add(content);
        Open();
    }

    /// <summary>
    /// Host an internal modal dialog in a themed card over the backdrop and await its result. Esc and a
    /// backdrop click cancel it (resolving the awaiter to the dialog's cancel result).
    /// </summary>
    public Task<TResult?> ShowModalAsync<TResult>(ModalDialog<TResult> dialog)
    {
        Children.Clear();
        _activeModal = dialog;
        Children.Add(BuildChrome(dialog, dialog.Title));
        Open();

        dialog.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeModal, dialog))
            {
                Close();
            }
        };

        // Focus the dialog so Esc reaches it even before an inner field claims focus — but not if the dialog
        // already claimed one: its InitialFocus is posted first (on attach), and focusing here would undo it.
        Dispatcher.UIThread.Post(() =>
        {
            if (!dialog.IsKeyboardFocusWithin)
            {
                dialog.Focus();
            }
        });
        return dialog.Result;
    }

    /// <summary>Tear down the overlay and raise <see cref="Closed"/>. Idempotent.</summary>
    public void Close()
    {
        if (!IsVisible)
        {
            return;
        }

        PointerPressed -= OnBackdropPressed;
        Children.Clear();
        IsVisible = false;
        _activeModal = null;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Dismiss the open overlay as a backdrop click would (cancel a modal so its awaiter resolves to the
    /// cancel result, or close a palette). Returns false if nothing is open — let the key fall through.
    /// </summary>
    public bool TryCancel()
    {
        if (!IsVisible)
        {
            return false;
        }

        DismissFromHost();
        return true;
    }

    private void Open()
    {
        IsVisible = true;
        PointerPressed += OnBackdropPressed;
        Opened?.Invoke(this, EventArgs.Empty);
    }

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        // Only a click on the backdrop itself dismisses — not one that bubbled up from the card.
        if (ReferenceEquals(e.Source, this))
        {
            DismissFromHost();
        }
    }

    private void DismissFromHost()
    {
        if (_activeModal is { } modal)
        {
            modal.CancelFromHost();
        }
        else
        {
            Close();
        }
    }

    /// <summary>Wrap a modal body in a themed floating card (title + shadow) with Tab trapped inside it.</summary>
    private Border BuildChrome(Control body, string title)
    {
        var stack = new StackPanel { Spacing = 10 };
        if (!string.IsNullOrEmpty(title))
        {
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeight.SemiBold,
                FontSize = 14,
            });
        }

        stack.Children.Add(body);

        var chrome = new Border
        {
            MaxWidth = 560,
            MaxHeight = 640,
            Background = Tokens.For(ActualThemeVariant).Surface,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 16, OffsetY = 4, Color = Color.FromArgb(0x40, 0, 0, 0) }),
            Child = stack,
        };
        // Trap Tab within the card so focus cycles through its own fields and never escapes to whatever
        // is behind the overlay (a dialog's sub-modals set their own Cycle scope inside this one).
        KeyboardNavigation.SetTabNavigation(chrome, KeyboardNavigationMode.Cycle);
        return chrome;
    }
}

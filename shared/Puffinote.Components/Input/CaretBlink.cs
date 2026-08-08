using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Puffinote.Components.Input;

/// <summary>
/// The family's blinking text cursor, wired once for every surface that has one.
///
/// <para><b>Why this is shared.</b> Puffinote types text into two different kinds of surface. Cellestra's
/// cell editor, Pivotask's task fields and the shell's search box are Avalonia <see cref="TextBox"/>es,
/// which blink on their own; Typage's and Plaintain's editing surfaces are custom controls that paint
/// their own caret, and painted a <em>steady</em> one — so the same person, in the same window, got a
/// blinking caret in one tab and a caret they had to hunt for in the next. A blink is how a caret says
/// "typing lands here"; a steady bar is indistinguishable from a table rule or a text-selection edge until
/// you type at it. Both halves therefore read one interval from here: this class drives the custom
/// surfaces, and <see cref="TextBoxStyles"/> pins the built-in ones to the same rate rather than leaving
/// them on whatever Avalonia's default happens to be this version.</para>
///
/// <para><b>The rate</b> is <see cref="IntervalMs"/> per phase — half a second lit, half a second dark, a
/// one-second cycle. That is macOS's own caret (Puffinote is macOS-first), and slow enough to stay
/// legible: much faster reads as a flicker, much slower and the caret is missing for long enough to look
/// gone. It is the interval between phase flips, not the cycle length, because that is the unit
/// <see cref="TextBox.CaretBlinkInterval"/> is expressed in and the two must not drift apart.</para>
///
/// <para><b>The caret goes solid whenever it moves</b> (<see cref="Show"/>). A caret that keeps blinking
/// through a keystroke can be dark at the moment the character lands, which hides the one thing the blink
/// exists to point at; every mature editor restarts the phase on input, and so does Avalonia's own
/// <c>TextPresenter</c>. Blinking only ever happens while the surface has focus — an unfocused editor's
/// caret is not drawn at all, and a timer ticking behind a background tab would repaint it for nothing.</para>
/// </summary>
public sealed class CaretBlink
{
    /// <summary>Milliseconds a phase lasts — see the class remarks for why this number.</summary>
    public const double IntervalMs = 500;

    /// <summary>How long the caret stays lit, and then dark. One flip per interval.</summary>
    public static TimeSpan Interval { get; } = TimeSpan.FromMilliseconds(IntervalMs);

    private readonly Control _surface;
    private readonly DispatcherTimer _timer;

    private CaretBlink(Control surface)
    {
        _surface = surface;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Interval };
        _timer.Tick += (_, _) => Advance();
    }

    /// <summary>
    /// Whether the caret should be painted right now. A surface asks this <em>in addition to</em> its own
    /// focus check: this stays true while unfocused (there is nothing to blink) so that a surface which
    /// forgot the focus check draws a steady caret rather than a phantom blinking one in a background tab.
    /// </summary>
    public bool IsCaretVisible { get; private set; } = true;

    /// <summary>
    /// Whether the phase timer is running — i.e. the caret is actually blinking rather than sitting lit.
    /// True while the surface is focused and in the tree. Worth asserting on: every other observable here
    /// would look exactly the same if the timer had never been started, which is the bug this class exists
    /// to fix, and a headless test has no way to catch a tick to prove otherwise.
    /// </summary>
    public bool IsBlinking => _timer.IsEnabled;

    /// <summary>
    /// Give <paramref name="surface"/> a blinking caret: it blinks while focused, stops when focus leaves,
    /// and repaints itself on every phase. The surface still decides <i>where</i> the caret goes and
    /// whether to draw one at all — it just gates that on <see cref="IsCaretVisible"/> — and calls
    /// <see cref="Show"/> when the caret moves.
    ///
    /// <para>Attaching is for the lifetime of the control, as with <see cref="PinchZoom.Attach"/>; the
    /// timer is stopped when the surface leaves the visual tree so a closed tab stops waking the
    /// dispatcher, and starts again if it comes back focused.</para>
    /// </summary>
    public static CaretBlink Attach(Control surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        var blink = new CaretBlink(surface);
        surface.GotFocus += (_, _) => blink.Show();
        surface.LostFocus += (_, _) => blink.Pause();

        // A surface can be pulled out of the tree while still focused — closing a tab, swapping panes — and
        // that raises no LostFocus. Without this the timer keeps ticking (and keeps the control alive)
        // forever, repainting something nobody can see. Coming back focused starts it up again.
        surface.DetachedFromVisualTree += (_, _) => blink.Pause();
        surface.AttachedToVisualTree += (_, _) =>
        {
            if (surface.IsFocused)
            {
                blink.Show();
            }
        };

        return blink;
    }

    /// <summary>
    /// The caret just moved, or focus just arrived: paint it solid and measure the next phase from now, so
    /// it can't blink out from under the character you are typing. Idempotent, and cheap enough to call on
    /// every keystroke — which is exactly how it is called.
    /// </summary>
    public void Show()
    {
        bool wasDark = !IsCaretVisible;
        IsCaretVisible = true;

        // Restart, not merely start: DispatcherTimer measures its interval from Start, so stopping and
        // starting is what moves the next flip a full interval away from this keystroke.
        _timer.Stop();
        if (_surface.IsFocused)
        {
            _timer.Start();
        }

        if (wasDark)
        {
            _surface.InvalidateVisual();
        }
    }

    /// <summary>
    /// Flip the phase — the timer's own callback. Public because a headless UI test has no wall clock to
    /// wait on: stepping the phase directly is how a test asserts that the caret does, in fact, go away and
    /// come back.
    /// </summary>
    public void Advance()
    {
        IsCaretVisible = !IsCaretVisible;
        _surface.InvalidateVisual();
    }

    /// <summary>Stop blinking and leave the caret lit, so it is solid the instant focus returns.</summary>
    private void Pause()
    {
        _timer.Stop();
        bool wasDark = !IsCaretVisible;
        IsCaretVisible = true;
        if (wasDark)
        {
            _surface.InvalidateVisual();
        }
    }

    /// <summary>
    /// Styles that put every Avalonia <see cref="TextBox"/> in an app on the family's blink rate. Each
    /// app adds this to its <c>Application.Styles</c> after the Fluent theme — one line, five apps — which
    /// is what keeps a spreadsheet cell, a task field and a Markdown document blinking together instead of
    /// each following whatever default its control shipped with. A fresh <see cref="Styles"/> per call:
    /// a style collection belongs to the one <c>Application</c> that owns it.
    /// </summary>
    public static Styles TextBoxStyles() =>
    [
        new Style(x => x.OfType<TextBox>())
        {
            Setters = { new Setter(TextBox.CaretBlinkIntervalProperty, Interval) },
        },
    ];
}

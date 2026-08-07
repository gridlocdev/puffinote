using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Puffinote.Text.Document;
using Typage.Control.Controls;
using Typage.Control.Views;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// What a reader expects of a link in an editor: hover it to see where it goes, ⌘/Ctrl-click it to follow,
/// right-click it to copy or edit it — and a plain click still just puts the caret in it, because the document
/// is text.
/// </summary>
public class LinkInteractionTests
{
    /// <summary>An editor laid out in a real window, so hit-testing has geometry to answer with.</summary>
    private static MarkdownEditor Laid(string text)
    {
        var view = new MarkdownEditorView();
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();
        view.Text = text;
        window.CaptureRenderedFrame(); // lay the text out, so columns have real x's
        return view.Editor;
    }

    /// <summary>The point a pointer hovering the character at <paramref name="offset"/> would be at.</summary>
    private static Point PointAtColumn(MarkdownEditor e, int offset) => e.PointAtColumnForTest(offset);

    [AvaloniaFact]
    public void TheCaretsLink_IsFoundWithItsTextAndUrl()
    {
        var e = new MarkdownEditor { Text = "see [the docs](https://example.com/a) now" };
        e.Buffer.Selection = Selection.At(8); // inside the link text

        var link = e.LinkAtCaret();
        Assert.NotNull(link);
        Assert.Equal("the docs", link!.Value.Text);
        Assert.Equal("https://example.com/a", link.Value.Url);
        Assert.False(link.Value.IsImage);
        Assert.True(e.IsCaretInLink);
    }

    [AvaloniaFact]
    public void OutsideALink_ThereIsNoLink()
    {
        var e = new MarkdownEditor { Text = "see [the docs](https://example.com) now" };
        e.Buffer.Selection = Selection.At(37); // in "now"
        Assert.Null(e.LinkAtCaret());
        Assert.False(e.IsCaretInLink);
    }

    [AvaloniaFact]
    public void AutolinksAndImages_AreLinksToo()
    {
        var bare = new MarkdownEditor { Text = "go to https://example.com/x today" };
        bare.Buffer.Selection = Selection.At(12);
        Assert.Equal("https://example.com/x", bare.LinkAtCaret()!.Value.Url);
        Assert.True(bare.LinkAtCaret()!.Value.IsAutolink);

        var bracketed = new MarkdownEditor { Text = "go to <https://example.com/y> today" };
        bracketed.Buffer.Selection = Selection.At(12);
        Assert.Equal("https://example.com/y", bracketed.LinkAtCaret()!.Value.Url);

        var image = new MarkdownEditor { Text = "![a cat](https://example.com/cat.png)" };
        image.Buffer.Selection = Selection.At(4);
        var link = image.LinkAtCaret();
        Assert.True(link!.Value.IsImage);
        Assert.Equal("https://example.com/cat.png", link.Value.Url);
    }

    // ----- what may be handed to the OS -----

    [AvaloniaTheory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com/a?b=c#d", true)]
    [InlineData("mailto:someone@example.com", true)]
    [InlineData("notes/other.md", false)]          // relative: Typage edits one file, it doesn't follow paths
    [InlineData("file:///etc/passwd", false)]      // a document is untrusted text — a click can't reach the disk
    [InlineData("javascript:alert(1)", false)]     // …or run anything
    [InlineData("data:text/html;base64,PHM+", false)]
    [InlineData("", false)]
    public void OnlyAWebOrMailAddressIsEverLaunched(string url, bool launchable) =>
        Assert.Equal(launchable, MarkdownEditor.TryGetLaunchUriForTest(url));

    // ----- hover -----

    [AvaloniaFact]
    public void HoveringALink_ShowsItsUrlInATooltip()
    {
        var e = Laid("[docs](https://example.com/a)");
        e.HoverForTest(PointAtColumn(e, 2)); // over the visible word "docs"

        Assert.Equal(("https://example.com/a", null), e.LinkTipForTest());
        Assert.True(ToolTip.GetIsOpen(e));
    }

    [AvaloniaFact]
    public void MovingOffTheLink_TakesTheTooltipAway()
    {
        var e = Laid("[docs](https://example.com/a) and text");
        e.HoverForTest(PointAtColumn(e, 2));
        Assert.True(ToolTip.GetIsOpen(e));

        e.HoverForTest(PointAtColumn(e, 31)); // out in the plain prose after it
        Assert.Null(e.LinkTipForTest());
        Assert.False(ToolTip.GetIsOpen(e));
        Assert.Null(ToolTip.GetTip(e)); // cleared, not merely closed — or the hover service would offer it over prose
    }

    [AvaloniaFact]
    public void TheFollowModifier_TurnsTheCursorIntoAHand()
    {
        var e = Laid("[docs](https://example.com/a)");
        var over = PointAtColumn(e, 2);
        var follow = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

        e.HoverForTest(over);
        Assert.Null(e.Cursor); // plain hover: the caret is still what a click would place

        e.HoverForTest(over, follow);
        Assert.NotNull(e.Cursor); // "this click will open it"
    }

    [AvaloniaFact]
    public void ARelativeLink_GetsNoHandCursor_NorAHint_ButStillTooltipsItsDestination()
    {
        // Nothing will be launched, so neither the cursor nor the hint may promise it will.
        var e = Laid("[notes](other.md)");
        var over = PointAtColumn(e, 3);
        var follow = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

        e.HoverForTest(over, follow);
        Assert.Null(e.Cursor);
        Assert.Equal(("other.md", null), e.LinkTipForTest());
    }

    // ----- the hint the tooltip grows while the modifier is held -----

    [AvaloniaFact]
    public void TheFollowModifier_AddsAHintLineToTheLinksOwnTooltip()
    {
        // A hand cursor says a click does *something*; the hint says what, and with which key — in the tooltip the
        // reader is already looking at, not in a second popup beside it.
        var e = Laid("[docs](https://example.com/a)");
        var over = PointAtColumn(e, 2);
        var follow = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

        e.HoverForTest(over);
        Assert.Equal(("https://example.com/a", null), e.LinkTipForTest()); // a plain hover opens nothing, and says so

        e.HoverForTest(over, follow);
        Assert.Equal(("https://example.com/a", MarkdownEditor.FollowHintText(OperatingSystem.IsMacOS())), e.LinkTipForTest());
    }

    [AvaloniaFact]
    public void TheHint_NamesTheModifierOfTheMachineItIsReadOn()
    {
        Assert.Equal("⌘ + click to open in your default browser", MarkdownEditor.FollowHintText(mac: true));
        Assert.Equal("Ctrl + click to open in your default browser", MarkdownEditor.FollowHintText(mac: false));
    }

    [AvaloniaFact]
    public void MovingOffTheLink_TakesTheHintAwayWithTheTooltip()
    {
        var e = Laid("[docs](https://example.com/a) and text");
        var follow = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

        e.HoverForTest(PointAtColumn(e, 2), follow);
        Assert.NotNull(e.LinkTipForTest()!.Value.Hint);

        e.HoverForTest(PointAtColumn(e, 31), follow); // out in the plain prose after it
        Assert.Null(e.LinkTipForTest());
    }

    [AvaloniaFact]
    public void PressingTheModifierOnARestingPointer_GrowsTheOpenTooltip_AndReleasingItShrinksItBack()
    {
        // The reader who is already hovering a link and then presses ⌘ never moves the mouse — so the hint can't
        // wait for a pointer move to appear. And it lands in the tooltip they are already reading: the popup is
        // the same object throughout, updated in place rather than replaced or doubled.
        var (window, editor) = Clickable("[docs](https://example.com/a)");
        editor.Focus();
        editor.HoverForTest(editor.PointAtColumnForTest(2));
        Assert.Null(editor.LinkTipForTest()!.Value.Hint);
        var tip = ToolTip.GetTip(editor);

        var (key, modifiers) = OperatingSystem.IsMacOS()
            ? (PhysicalKey.MetaLeft, RawInputModifiers.Meta)
            : (PhysicalKey.ControlLeft, RawInputModifiers.Control);

        window.KeyPressQwerty(key, modifiers);
        Assert.Equal(("https://example.com/a", MarkdownEditor.FollowHintText(OperatingSystem.IsMacOS())), editor.LinkTipForTest());
        Assert.NotNull(editor.Cursor);
        Assert.Same(tip, ToolTip.GetTip(editor)); // the tooltip grew a line; no second popup was raised

        window.KeyReleaseQwerty(key, modifiers);
        Assert.Equal(("https://example.com/a", null), editor.LinkTipForTest());
        Assert.Null(editor.Cursor);
    }

    [AvaloniaFact]
    public void PastTheEndOfTheLine_IsNotTheLink()
    {
        // A caret hit-test answers with the nearest character even far to the right of the text; a link's
        // glyphs are what a reader means by "hovering the link", so the empty space past them isn't it.
        var e = Laid("[docs](https://example.com/a)");
        Assert.Null(e.LinkAtPointForTest(new Point(600, 12)));
    }

    // ----- editing an existing link -----

    [AvaloniaFact]
    public void ReplacingALink_RewritesJustThatLink()
    {
        var e = new MarkdownEditor { Text = "see [old](https://old.example) now" };
        e.Buffer.Selection = Selection.At(6);

        e.ReplaceLink(e.LinkAtCaret()!.Value, "new", "https://new.example");
        Assert.Equal("see [new](https://new.example) now", e.Text);
    }

    [AvaloniaFact]
    public void ReplacingAnImage_KeepsItAnImage()
    {
        var e = new MarkdownEditor { Text = "![cat](old.png)" };
        e.Buffer.Selection = Selection.At(3);

        e.ReplaceLink(e.LinkAtCaret()!.Value, "a cat", "new.png");
        Assert.Equal("![a cat](new.png)", e.Text);
    }

    [AvaloniaFact]
    public void EditingAnAutolink_GivesItADisplayText()
    {
        var e = new MarkdownEditor { Text = "https://example.com/a" };
        e.Buffer.Selection = Selection.At(10);

        e.ReplaceLink(e.LinkAtCaret()!.Value, "Example", "https://example.com/a");
        Assert.Equal("[Example](https://example.com/a)", e.Text);
    }

    [AvaloniaFact]
    public void EditLinkIsOfferedOnlyWhereThereIsALink()
    {
        var view = new MarkdownEditorView { Text = "see [docs](https://example.com) now" };
        var command = view.EditingCommands.Single(c => c.Id == "format.editLink");

        view.Editor.Buffer.Selection = Selection.At(7); // in the link
        Assert.True(command.IsEnabled);

        view.Editor.Buffer.Selection = Selection.At(33); // in "now"
        Assert.False(command.IsEnabled);
    }

    // ----- clicking -----

    /// <summary>An editor that records what it was asked to open instead of handing it to a real browser.</summary>
    private sealed class RecordingEditor : MarkdownEditor
    {
        public List<Uri> Launched { get; } = [];

        protected override bool Launch(Uri uri)
        {
            Launched.Add(uri);
            return true;
        }
    }

    private static (Window Window, RecordingEditor Editor) Clickable(string text)
    {
        var editor = new RecordingEditor { Text = text };
        var window = new Window { Width = 800, Height = 600, Content = editor };
        window.Show();
        window.CaptureRenderedFrame();
        return (window, editor);
    }

    private static RawInputModifiers FollowModifier =>
        OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    [AvaloniaFact]
    public void CommandClickingALink_OpensIt_AndLeavesTheCaretAlone()
    {
        var (window, editor) = Clickable("[docs](https://example.com/a)");
        editor.Buffer.Selection = Selection.At(0);

        window.MouseDown(editor.PointAtColumnForTest(2), MouseButton.Left, FollowModifier);

        Assert.Equal(new Uri("https://example.com/a"), Assert.Single(editor.Launched));
        Assert.Equal(0, editor.Buffer.Selection.Caret); // the click was spent on the link, not on the caret
    }

    [AvaloniaFact]
    public void APlainClickOnALink_JustPlacesTheCaret()
    {
        // The document is still text: a link has to be editable by clicking into it.
        var (window, editor) = Clickable("[docs](https://example.com/a)");

        window.MouseDown(editor.PointAtColumnForTest(2), MouseButton.Left);

        Assert.Empty(editor.Launched);
        Assert.InRange(editor.Buffer.Selection.Caret, 1, 5); // somewhere in "docs"
    }

    [AvaloniaFact]
    public void CommandClickingANonWebLink_FallsBackToPlacingTheCaret()
    {
        // Nothing gets launched, so the click must still do the ordinary thing rather than being swallowed.
        var (window, editor) = Clickable("[notes](other.md)");

        window.MouseDown(editor.PointAtColumnForTest(3), MouseButton.Left, FollowModifier);

        Assert.Empty(editor.Launched);
        Assert.InRange(editor.Buffer.Selection.Caret, 1, 6); // in "notes"
    }

    [AvaloniaFact]
    public void RightClickingALink_RemembersItForTheContextMenu()
    {
        var (window, editor) = Clickable("[docs](https://example.com/a) plain");

        window.MouseDown(editor.PointAtColumnForTest(2), MouseButton.Right);
        Assert.Equal("https://example.com/a", editor.ContextLinkForTest()!.Value.Url);

        // …and a right-click on ordinary prose has no link to act on, so the menu's link items stay hidden.
        window.MouseDown(editor.PointAtColumnForTest(31), MouseButton.Right);
        Assert.Null(editor.ContextLinkForTest());
    }
}

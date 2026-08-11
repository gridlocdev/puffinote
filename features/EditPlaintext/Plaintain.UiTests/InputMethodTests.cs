using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Threading;
using Plaintain.Control.Controls;
using Puffinote.Text.Document;
using Xunit;

namespace Plaintain.UiTests;

/// <summary>
/// The platform text-input seam — what macOS dictation talks to. A custom-drawn surface is invisible to it
/// until it hands over a client, which is why dictation used to arrive in one lump when it was dismissed
/// rather than word by word as it was spoken. These drive the client the way the platform does: mark some
/// text, mark more, then commit.
/// </summary>
public class InputMethodTests
{
    private static (PlainTextEditor Editor, TextInputMethodClient Client, Window Window) Open(string text = "", int caret = 0)
    {
        var editor = new PlainTextEditor();
        var window = new Window { Width = 400, Height = 200, Content = editor };
        window.Show();
        editor.Buffer.Load(text);
        editor.Buffer.Selection = Selection.At(caret);
        Dispatcher.UIThread.RunJobs();
        return (editor, editor.InputMethodClientForTest, window);
    }

    private static void Commit(PlainTextEditor editor, string text)
    {
        editor.RaiseEvent(new TextInputEventArgs
        {
            RoutedEvent = InputElement.TextInputEvent,
            Text = text,
            Source = editor,
        });
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void TheEditorOffersAClientWhenThePlatformAsksForOne()
    {
        // Without this the OS has nowhere to put recognised words, and holds the whole utterance instead.
        var (editor, client, _) = Open();
        var request = new TextInputMethodClientRequestedEventArgs
        {
            RoutedEvent = InputElement.TextInputMethodClientRequestedEvent,
            Source = editor,
        };
        editor.RaiseEvent(request);

        Assert.Same(client, request.Client);
        Assert.True(client.SupportsPreedit);
        Assert.Same(editor, client.TextViewVisual);
    }

    [AvaloniaFact]
    public void MarkedTextAppearsAsItIsRecognised_AndGrowsInPlace()
    {
        var (editor, client, _) = Open();

        client.SetPreeditText("hello");
        Assert.Equal("hello", editor.Buffer.Document.GetText()); // the word is there while you are still speaking

        client.SetPreeditText("hello there");
        Assert.Equal("hello there", editor.Buffer.Document.GetText()); // replaced, not appended
        Assert.Equal(11, editor.Buffer.Selection.Caret);              // and the caret rides the end of it
    }

    [AvaloniaFact]
    public void CommittingWritesThePhraseOnce()
    {
        var (editor, client, _) = Open("say: ", caret: 5);

        client.SetPreeditText("hello");
        client.SetPreeditText("hello there");
        Commit(editor, "hello there");

        Assert.Equal("say: hello there", editor.Buffer.Document.GetText());
    }

    [AvaloniaFact]
    public void AWithdrawnCompositionLeavesTheDocumentAsItFoundIt()
    {
        var (editor, client, _) = Open("before after", caret: 7);

        client.SetPreeditText("nev");
        Assert.Equal("before nevafter", editor.Buffer.Document.GetText());

        client.SetPreeditText(string.Empty); // dictation dismissed without committing anything
        Assert.Equal("before after", editor.Buffer.Document.GetText());
        Assert.Equal(7, editor.Buffer.Selection.Caret);
    }

    [AvaloniaFact]
    public void MarkedTextLandsAtTheCaret_NotAtTheEndOfTheDocument()
    {
        var (editor, client, _) = Open("start end", caret: 6);
        client.SetPreeditText("mid ");
        Assert.Equal("start mid end", editor.Buffer.Document.GetText());
    }

    [AvaloniaFact]
    public void SurroundingTextAndSelectionAreTheCaretsOwnLine()
    {
        var (_, client, _) = Open("first\nsecond line\nthird", caret: 9); // 3 chars into "second line"
        Assert.Equal("second line", client.SurroundingText);
        Assert.Equal(new TextSelection(3, 3), client.Selection);

        client.Selection = new TextSelection(0, 6); // the platform selecting "second"
        Assert.Equal(new TextSelection(0, 6), client.Selection);
    }

    [AvaloniaFact]
    public void TypingAfterACompositionStartsAFreshOne()
    {
        // An edit from anywhere else invalidates the range the composition was written into; the next
        // update must not reach back and rewrite text the user has since typed.
        var (editor, client, _) = Open();
        client.SetPreeditText("abc");
        Commit(editor, "abc");
        Commit(editor, "!");

        client.SetPreeditText("xyz");
        Assert.Equal("abc!xyz", editor.Buffer.Document.GetText());
    }
}

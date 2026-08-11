using Avalonia;
using Avalonia.Input.TextInput;

namespace Plaintain.Control.Controls;

public sealed partial class PlainTextEditor
{
    /// <summary>
    /// The surface's input-method client. A custom-drawn editor is invisible to the platform's text-input
    /// machinery until it hands one over, which is what left macOS <b>dictation</b> mute: the OS had nowhere
    /// to put the words as it recognised them, so it held the whole utterance as marked text and only
    /// committed it when dictation was dismissed — "nothing enters, then all of it at once", with
    /// <c>IMKCFRunLoopWakeUpReliable</c> complaining in the console meanwhile.
    /// </summary>
    private readonly EditorInputMethodClient _inputMethod;

    /// <summary>Offer the client to the platform when it asks the focused element for one.</summary>
    private void WatchInputMethod() =>
        AddHandler(TextInputMethodClientRequestedEvent, (_, e) => e.Client = _inputMethod);

    /// <summary>
    /// End the composition in flight, if any, before text arrives by the ordinary route. The committed text
    /// comes through <see cref="OnTextInput"/>, and the marked text it replaces is already in the document —
    /// so it has to come back out first, or a dictated phrase would land twice.
    /// </summary>
    private void EndComposition() => _inputMethod.EndComposition();

    /// <summary>The client the platform would be given; for tests.</summary>
    internal TextInputMethodClient InputMethodClientForTest => _inputMethod;

    /// <summary>
    /// Bridges the editor to the platform's input methods — dictation, and any IME that composes before it
    /// commits (PRD §9: the surface is custom-drawn, so none of this comes for free from a TextBox).
    ///
    /// <para><b>Marked text lives in the document.</b> Each <see cref="SetPreeditText(string?, int?)"/>
    /// replaces the range the last one wrote, so a phrase being dictated grows in place, word by word, exactly
    /// where it will end up — and every one of those states is a real document the caret, the wrap and the
    /// renderer already know how to handle. Drawing it as a separate floating run would mean a second text
    /// model beside the piece table, which is the sort of parallel truth this editor is built to avoid.</para>
    ///
    /// <para>The commit then arrives as an ordinary text-input event; the marked text is withdrawn first
    /// (<see cref="EndComposition"/>) so the phrase is written once, not twice.</para>
    /// </summary>
    internal sealed class EditorInputMethodClient : TextInputMethodClient
    {
        private readonly PlainTextEditor _editor;

        /// <summary>Where the marked text starts in the document, and how long it is; -1 = no composition.</summary>
        private int _start = -1;
        private int _length;

        /// <summary>Set while this client is the one editing, so its own document changes don't re-enter it.</summary>
        private bool _applying;

        public EditorInputMethodClient(PlainTextEditor editor)
        {
            _editor = editor;
            editor.Buffer.Changed += (_, _) =>
            {
                if (_applying)
                {
                    return;
                }

                // Anything else touching the document — typing, a click, a reload — invalidates the range the
                // composition was written into, so it is no longer ours to replace.
                _start = -1;
                _length = 0;
                RaiseSurroundingTextChanged();
                RaiseSelectionChanged();
                RaiseCursorRectangleChanged();
            };
        }

        public override Visual TextViewVisual => _editor;

        public override bool SupportsPreedit => true;

        public override bool SupportsSurroundingText => true;

        /// <summary>
        /// The caret's line, which is as much context as an input method needs to make a decision about the
        /// word being typed — and all a 50 MB log can afford to hand over on every keystroke.
        /// </summary>
        public override string SurroundingText
        {
            get
            {
                var document = _editor.Buffer.Document;
                var line = document.GetPosition(Caret).Line;
                return document.GetLineText(line).TrimEnd('\r', '\n');
            }
        }

        /// <summary>The selection in <see cref="SurroundingText"/>'s own coordinates — columns of the caret's line.</summary>
        public override TextSelection Selection
        {
            get
            {
                var document = _editor.Buffer.Document;
                var selection = _editor.Buffer.Selection;
                var lineStart = document.GetLineStart(document.GetPosition(Caret).Line);
                var length = SurroundingText.Length;
                return new TextSelection(
                    Math.Clamp(selection.Start - lineStart, 0, length),
                    Math.Clamp(selection.End - lineStart, 0, length));
            }

            set
            {
                var document = _editor.Buffer.Document;
                var lineStart = document.GetLineStart(document.GetPosition(Caret).Line);
                _editor.Buffer.Selection = new Puffinote.Text.Document.Selection(
                    Math.Clamp(lineStart + value.Start, 0, document.Length),
                    Math.Clamp(lineStart + value.End, 0, document.Length));
            }
        }

        /// <summary>Where the platform draws its candidate window: the caret, in the editor's own space.</summary>
        public override Rect CursorRectangle => _editor.CaretRectForTest();

        public override void SetPreeditText(string? preeditText) => SetPreeditText(preeditText, null);

        public override void SetPreeditText(string? preeditText, int? cursorPos)
        {
            var text = preeditText ?? string.Empty;
            var buffer = _editor.Buffer;
            var at = _start >= 0 ? _start : buffer.Selection.Start;

            _applying = true;
            try
            {
                // Replace what the last update wrote (nothing, the first time) with what has been recognised
                // so far. An empty update withdraws the composition entirely — which is also how a dictation
                // that ends up committing nothing leaves the document exactly as it found it.
                buffer.ReplaceRange(at, _length, text);
                _start = text.Length > 0 ? at : -1;
                _length = text.Length;
                var caret = at + Math.Clamp(cursorPos ?? text.Length, 0, text.Length);
                buffer.Selection = Puffinote.Text.Document.Selection.At(Math.Clamp(caret, 0, buffer.Document.Length));
            }
            finally
            {
                _applying = false;
            }

            _editor.BringCaretIntoView();
            RaiseSurroundingTextChanged();
            RaiseSelectionChanged();
            RaiseCursorRectangleChanged();
        }

        /// <summary>Withdraw the marked text (the commit that follows carries the same words).</summary>
        public void EndComposition()
        {
            if (_start < 0)
            {
                return;
            }

            var at = _start;
            var length = _length;
            _start = -1;
            _length = 0;

            _applying = true;
            try
            {
                _editor.Buffer.ReplaceRange(at, length, string.Empty);
                _editor.Buffer.Selection = Puffinote.Text.Document.Selection.At(at);
            }
            finally
            {
                _applying = false;
            }
        }

        private int Caret => Math.Clamp(_editor.Buffer.Selection.Caret, 0, _editor.Buffer.Document.Length);
    }
}

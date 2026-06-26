using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using BirdBrain.Components.Overlays;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views;
using Cellestra.Desktop.Views.Dialogs;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// The internal-modal host: dialogs render inside the window's overlay (not a separate OS window),
/// report the same modal state as the command palette, and resolve a result via Esc/confirm.
/// </summary>
public class InternalModalTests
{
    [AvaloniaFact]
    public void Showing_a_dialog_marks_the_window_modal_and_esc_cancels_it()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;

        Assert.False(env.IsModalOpen);

        // ShowFormatCellsDialog hosts the Format Cells dialog as an internal modal.
        env.ShowFormatCellsDialog();
        Assert.True(env.IsModalOpen);

        // Esc cancels the internal modal, just like the palette.
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(env.IsModalOpen);
    }

    [AvaloniaFact]
    public void Confirm_dialog_resolves_true_when_confirmed_and_default_on_cancel()
    {
        var confirm = new ConfirmDialog("Delete?", "Sure?", "Delete");
        confirm.Close(true);
        Assert.True(confirm.Result.IsCompleted);
        Assert.True(confirm.Result.Result);

        var cancelled = new ConfirmDialog("Delete?", "Sure?", "Delete");
        cancelled.CancelFromHost();
        Assert.False(cancelled.Result.Result);
    }

    [AvaloniaFact]
    public void About_dialog_constructs_with_the_expected_title()
    {
        var about = new AboutDialog();
        Assert.Equal("About Cellestra", about.Title);
    }

    [Fact]
    public void About_command_is_in_the_help_section_of_the_catalog()
    {
        var about = AppCommands.ById("help.about");
        Assert.NotNull(about);
        Assert.Equal("About Cellestra", about!.Label);
    }
}

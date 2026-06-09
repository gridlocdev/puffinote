using Avalonia.Automation.Peers;
using Avalonia.Headless.XUnit;
using Cellestra.Core.Values;
using Cellestra.Desktop.Controls.SpreadsheetGrid;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>The grid exposes a control-level automation peer that reports the active cell (#C).</summary>
public class GridAutomationPeerTests
{
    [AvaloniaFact]
    public void Grid_exposes_a_datagrid_automation_peer()
    {
        var window = new MainWindow();
        window.Show();

        var peer = ControlAutomationPeer.CreatePeerForElement(window.Grid);

        Assert.IsType<SpreadsheetGridAutomationPeer>(peer);
        Assert.Equal(AutomationControlType.DataGrid, peer.GetAutomationControlType());
    }

    [AvaloniaFact]
    public void Peer_name_reports_the_active_cell_address_and_value()
    {
        var window = new MainWindow();
        window.Show();
        var sheet = window.Session.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Number(1250));
        sheet.SetValue(1, 1, CellValue.Text("Hello"));

        var peer = ControlAutomationPeer.CreatePeerForElement(window.Grid);

        window.Selection.MoveTo(0, 0);
        Assert.Contains("A1", peer.GetName());
        Assert.Contains("1250", peer.GetName());

        window.Selection.MoveTo(1, 1);
        Assert.Contains("B2", peer.GetName());
        Assert.Contains("Hello", peer.GetName());
    }

    [AvaloniaFact]
    public void Peer_describes_an_empty_active_cell()
    {
        var window = new MainWindow();
        window.Show();

        var peer = ControlAutomationPeer.CreatePeerForElement(window.Grid);
        window.Selection.MoveTo(2, 2);

        Assert.Contains("C3", peer.GetName());
        Assert.Contains("empty", peer.GetName());
    }
}

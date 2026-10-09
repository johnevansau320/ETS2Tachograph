using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using Tachograph.Core;

namespace Tachograph.App;

public static class HistoryExporter
{
    public static void Save(Window owner, TachographState state)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Export tachograph history",
            Filter = "CSV file (*.csv)|*.csv",
            FileName = "ets2-tachograph-history.csv",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dlg.ShowDialog(owner) != true) return;
        try
        {
            // UTF-8 with a byte-order mark so Excel opens it correctly.
            File.WriteAllText(dlg.FileName, HistoryExport.ToCsv(state), new UTF8Encoding(true));
            MessageBox.Show(owner, $"Saved to {dlg.FileName}", "Export complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(owner, $"Couldn't save the file: {e.Message}", "Export failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

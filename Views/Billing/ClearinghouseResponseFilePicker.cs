using System.IO;
using Microsoft.Win32;
using Sati.Services;

namespace Sati.Views.Billing;

internal sealed class ClearinghouseResponseFilePicker : IClearinghouseResponseFilePicker
{
    public async Task<string?> ReadResponseAsync(CancellationToken cancellationToken)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import a clearinghouse response",
            Filter = "X12 responses (*.txt;*.edi;*.x12)|*.txt;*.edi;*.x12|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return null;
        cancellationToken.ThrowIfCancellationRequested();
        await using var stream = new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read,
            FileShare.Read, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await ClearinghouseResponseFile.ReadAsync(stream, cancellationToken);
    }
}

using System.IO;
using System.Text;
using Sati.Contracts.V1;

namespace Sati.Services;

internal static class ClearinghouseResponseFile
{
    public static async Task<string> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (!stream.CanSeek || stream.Length is <= 0 or > ClaimResponseReader.MaximumDocumentCharacters)
            throw new FormatException("Choose an X12 response within the supported file-size limit.");

        var length = checked((int)stream.Length);
        var bytes = new byte[length + 1];
        try
        {
            await stream.ReadExactlyAsync(bytes.AsMemory(0, length), cancellationToken);
            if (await stream.ReadAsync(bytes.AsMemory(length, 1), cancellationToken) != 0)
                throw new FormatException("The file changed while it was being read. Choose it again.");
            for (var index = 0; index < length; index++)
            {
                if (bytes[index] > 127 || (bytes[index] < 32 && bytes[index] is not (10 or 13)))
                    throw new FormatException("Choose the original ASCII X12 response, without editing or re-encoding it.");
            }
            return Encoding.ASCII.GetString(bytes, 0, length);
        }
        finally
        {
            Array.Clear(bytes);
        }
    }
}

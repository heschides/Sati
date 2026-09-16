namespace Sati.Contracts.V1;

/// <summary>
/// The current profile photograph for one consumer. Photo bytes travel only on the
/// dedicated, person-scoped route; they are never part of a caseload or Person DTO.
/// </summary>
public sealed record PersonPhotoDto(
    string ContentType,
    byte[] Content,
    int PixelWidth,
    int PixelHeight,
    long Revision);

public sealed record PersonPhotoStateDto(PersonPhotoDto? Photo);

public sealed record SavePersonPhotoRequest(
    string ContentType,
    byte[] Content,
    long? ExpectedRevision);

/// <summary>
/// The single validation owner for consumer profile photographs. It recognizes the
/// file from its bytes instead of trusting a file extension or request header.
/// </summary>
public static class PersonPhotoRules
{
    public const int MaximumBytes = 5 * 1024 * 1024;
    public const int MaximumDimension = 4_096;
    public const long MaximumPixels = 16_777_216;

    public static PersonPhotoInspection? Inspect(
        ReadOnlySpan<byte> content,
        string? declaredContentType,
        out string? problem)
    {
        problem = null;
        if (content.IsEmpty)
        {
            problem = "Choose a JPG or PNG photo.";
            return null;
        }

        if (content.Length > MaximumBytes)
        {
            problem = "The photo must be 5 MB or smaller.";
            return null;
        }

        PersonPhotoInspection? inspection = InspectPng(content) ?? InspectJpeg(content);
        if (inspection is null)
        {
            problem = "The selected file is not a valid JPG or PNG image.";
            return null;
        }

        var declared = NormalizeContentType(declaredContentType);
        if (declared is not null && !declared.Equals(inspection.ContentType, StringComparison.Ordinal))
        {
            problem = "The selected file type does not match its image content.";
            return null;
        }

        if (inspection.PixelWidth > MaximumDimension || inspection.PixelHeight > MaximumDimension ||
            (long)inspection.PixelWidth * inspection.PixelHeight > MaximumPixels)
        {
            problem = "The photo dimensions are too large. Use an image no larger than 4096 pixels on either side.";
            return null;
        }

        return inspection;
    }

    private static string? NormalizeContentType(string? contentType) =>
        contentType?.Trim().ToLowerInvariant() switch
        {
            null or "" => null,
            "image/jpeg" or "image/jpg" => "image/jpeg",
            "image/png" => "image/png",
            _ => string.Empty
        };

    private static PersonPhotoInspection? InspectPng(ReadOnlySpan<byte> content)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (content.Length < 45 || !content[..8].SequenceEqual(signature))
            return null;

        var offset = 8;
        var width = 0;
        var height = 0;
        var firstChunk = true;
        while (offset + 12 <= content.Length)
        {
            var length = ReadBigEndianUInt32(content.Slice(offset, 4));
            if (length > int.MaxValue || length > content.Length - offset - 12)
                return null;
            var dataLength = (int)length;
            var type = content.Slice(offset + 4, 4);
            if (firstChunk)
            {
                if (dataLength != 13 || !type.SequenceEqual("IHDR"u8))
                    return null;
                width = ReadBigEndianInt32(content.Slice(offset + 8, 4));
                height = ReadBigEndianInt32(content.Slice(offset + 12, 4));
                if (width <= 0 || height <= 0)
                    return null;
                firstChunk = false;
            }

            var next = offset + 12 + dataLength;
            if (type.SequenceEqual("IEND"u8))
            {
                return dataLength == 0 && next == content.Length
                    ? new PersonPhotoInspection("image/png", width, height)
                    : null;
            }
            offset = next;
        }

        return null;
    }

    private static PersonPhotoInspection? InspectJpeg(ReadOnlySpan<byte> content)
    {
        if (content.Length < 4 || content[0] != 0xFF || content[1] != 0xD8 ||
            content[^2] != 0xFF || content[^1] != 0xD9)
            return null;

        var offset = 2;
        while (offset + 3 < content.Length)
        {
            while (offset < content.Length && content[offset] != 0xFF)
                offset++;
            while (offset < content.Length && content[offset] == 0xFF)
                offset++;
            if (offset >= content.Length)
                return null;

            var marker = content[offset++];
            if (marker is 0xD8 or 0xD9 || marker is >= 0xD0 and <= 0xD7)
                continue;
            if (offset + 1 >= content.Length)
                return null;

            var length = (content[offset] << 8) | content[offset + 1];
            if (length < 2 || offset + length > content.Length)
                return null;

            if (IsStartOfFrame(marker))
            {
                if (length < 7)
                    return null;
                var height = (content[offset + 3] << 8) | content[offset + 4];
                var width = (content[offset + 5] << 8) | content[offset + 6];
                return width > 0 && height > 0
                    ? new PersonPhotoInspection("image/jpeg", width, height)
                    : null;
            }

            // Start of Scan before a frame header means this is not a structurally
            // valid JPEG profile photo for this deliberately small parser.
            if (marker == 0xDA)
                return null;
            offset += length;
        }

        return null;
    }

    private static bool IsStartOfFrame(byte marker) => marker is
        0xC0 or 0xC1 or 0xC2 or 0xC3 or
        0xC5 or 0xC6 or 0xC7 or
        0xC9 or 0xCA or 0xCB or
        0xCD or 0xCE or 0xCF;

    private static int ReadBigEndianInt32(ReadOnlySpan<byte> value) =>
        (value[0] << 24) | (value[1] << 16) | (value[2] << 8) | value[3];

    private static uint ReadBigEndianUInt32(ReadOnlySpan<byte> value) =>
        ((uint)value[0] << 24) | ((uint)value[1] << 16) | ((uint)value[2] << 8) | value[3];
}

public sealed record PersonPhotoInspection(string ContentType, int PixelWidth, int PixelHeight);

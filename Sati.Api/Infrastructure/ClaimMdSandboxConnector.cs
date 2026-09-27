using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Sati.Contracts.V1;
using Sati.Models.Billing;

namespace Sati.Api.Infrastructure;

/// <summary>Only the API host resolves account keys. The database stores a variable name, never a key.</summary>
internal interface IClaimMdSandboxKeySource
{
    string Resolve(string? reference);
}

internal sealed class EnvironmentClaimMdSandboxKeySource : IClaimMdSandboxKeySource
{
    internal static bool IsValidReference(string? reference) =>
        reference is { Length: >= 24 and <= 100 } &&
        reference.StartsWith("CLAIMMD_SANDBOX_KEY_", StringComparison.Ordinal) &&
        reference.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');

    public string Resolve(string? reference)
    {
        if (!IsValidReference(reference))
            throw new InvalidOperationException("The Claim.MD sandbox secret reference is invalid.");
        var key = Environment.GetEnvironmentVariable(reference!);
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("The Claim.MD sandbox key is unavailable to this API host.");
        return key;
    }
}

internal sealed record ClaimMdStatusPage(string Cursor, IReadOnlyList<ClaimMdStatusClaim> Claims, string RawXml);
internal sealed record ClaimMdStatusClaim(string ResponseId, string FileId, string ClaimReference,
    string RemoteClaimId, string Status);
internal sealed record ClaimMdEraEntry(string EraId);
internal sealed record ClaimMdEraPage(string Cursor, IReadOnlyList<ClaimMdEraEntry> Eras, string RawXml);
internal sealed record ClaimMdUploadListing(string FileId, string FileName, long UploadTime);

/// <summary>Fixed-host Claim.MD API 1.19 transport. No endpoint is supplied by a client or account row.</summary>
internal sealed class ClaimMdSandboxConnector(HttpClient http, IClaimMdSandboxKeySource keys) : IClearinghouseConnector
{
    private const string Base = "https://svc.claim.md/services/";
    private const int MaximumResponseBytes = 16 * 1024 * 1024;
    internal const int EraListPageSize = 100;

    public async Task<ClearinghouseUploadResult> UploadAsync(ClearinghouseUpload upload, CancellationToken token)
    {
        if (upload.Partner != TradingPartnerKind.ClaimMd)
            throw new InvalidOperationException("The Claim.MD transport cannot upload another partner's file.");
        var submission = ClaimResponseReader.ReadSubmission(upload.Content);
        if (!submission.Envelope.IsTestInterchange || submission.Claims.Count is < 1 or > 2000 ||
            submission.Envelope.ReceiverId != "CLAIMMD" ||
            submission.Claims.Any(claim => string.IsNullOrEmpty(claim.RemoteClaimId)))
            throw new InvalidOperationException("The Claim.MD transport requires a valid test 837P file.");
        if (upload.FileName.Length is < 1 or > 260 ||
            !upload.FileName.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
            throw new InvalidOperationException("The retained upload filename is invalid.");
        var bytes = Encoding.ASCII.GetBytes(upload.Content);
        if (upload.Content.Any(character => !char.IsAscii(character)) ||
            Convert.ToHexString(SHA256.HashData(bytes)) != upload.ContentSha256)
            throw new InvalidOperationException("The retained upload content hash is invalid.");
        var key = keys.Resolve(upload.SecretReference);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(key), "AccountKey");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/edi-x12");
        form.Add(file, "File", upload.FileName);
        var document = await PostAsync("upload/", form, token);
        XElement root;
        try { root = Parse(document); }
        catch (Exception failure) when (failure is FormatException or XmlException)
        { return Unknown("claimmd_upload_invalid_response", document); }
        var claims = root.Elements("claim").ToArray();
        if (claims.Length != submission.Claims.Count)
            return Unknown("claimmd_upload_count_mismatch", document);
        var expected = submission.Claims.ToDictionary(claim => claim.ClaimReference, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? fileId = null;
        var accepted = 0;
        var rejected = 0;
        foreach (var claim in claims)
        {
            var pcn = Attribute(claim, "pcn");
            var remoteId = Attribute(claim, "remote_claimid");
            var candidateFileId = Attribute(claim, "fileid");
            if (pcn is null || !expected.TryGetValue(pcn, out var submitted) ||
                submitted.RemoteClaimId != remoteId || !seen.Add(pcn) ||
                !SafeDigits(candidateFileId) || Attribute(claim, "status") is not ("A" or "R") ||
                claim.Elements("messages").Any(message => Attribute(message, "status") is not ("A" or "R")) ||
                fileId is not null && fileId != candidateFileId)
                return Unknown("claimmd_upload_requires_review", document);
            if (Attribute(claim, "status") == "A") accepted++;
            else rejected++;
            fileId = candidateFileId;
        }
        return new ClearinghouseUploadResult(ClearinghouseAttemptOutcome.Accepted,
            fileId, null, accepted, rejected, document);
    }

    // These read-only methods are deliberately not scheduled until account-specific, atomic
    // receipt processing is installed. A caller must never advance a cursor from an HTTP read.
    internal async Task<ClaimMdStatusPage> GetStatusesAsync(string? secretReference, string cursor, CancellationToken token)
    {
        if (!SafeDigits(cursor)) throw new ArgumentException("Invalid status cursor.", nameof(cursor));
        var xml = await PostFormAsync("response/", secretReference,
            new("ResponseID", cursor), token);
        var root = Parse(xml);
        var next = Attribute(root, "last_responseid") ?? cursor;
        if (!SafeDigits(next) || CompareIds(next, cursor) < 0)
            throw new FormatException("Invalid Claim.MD status cursor.");
        var claims = root.Elements("claim").Select(claim =>
        {
            var messages = claim.Elements("messages").ToArray();
            if (messages.Length == 0) throw new FormatException("Status row has no response ID.");
            var responseId = Attribute(messages[^1], "responseid") ?? string.Empty;
            var fileId = Attribute(claim, "fileid") ?? string.Empty;
            var pcn = Attribute(claim, "pcn") ?? string.Empty;
            var remote = Attribute(claim, "remote_claimid") ?? string.Empty;
            var status = Attribute(claim, "status") ?? string.Empty;
            if (!SafeDigits(responseId) || !SafeDigits(fileId) ||
                CompareIds(responseId, cursor) <= 0 || CompareIds(responseId, next) > 0 ||
                pcn.Length is < 1 or > 80 || remote.Length is < 1 or > 80 ||
                status is not ("A" or "R")) throw new FormatException("Invalid Claim.MD status row.");
            return new ClaimMdStatusClaim(responseId, fileId, pcn, remote, status);
        }).GroupBy(claim => (claim.FileId, claim.ClaimReference, claim.RemoteClaimId))
            .Select(group => group.OrderBy(claim => decimal.Parse(claim.ResponseId,
                CultureInfo.InvariantCulture)).Last()).ToArray();
        return new ClaimMdStatusPage(next, claims, xml);
    }

    internal async Task<ClaimMdEraPage> GetErasAsync(string? secretReference, string cursor,
        int page, CancellationToken token)
    {
        if (!SafeDigits(cursor) || page < 1) throw new ArgumentException("Invalid ERA cursor or page.");
        var xml = await PostFormAsync("eralist/", secretReference,
            new("ERAID", cursor), token, new KeyValuePair<string, string>("Page", page.ToString(CultureInfo.InvariantCulture)));
        var root = Parse(xml);
        var next = Attribute(root, "last_eraid") ?? cursor;
        if (!SafeDigits(next) || CompareIds(next, cursor) < 0)
            throw new FormatException("Invalid Claim.MD ERA cursor.");
        var eras = root.Elements("era").Select(era =>
        {
            var id = Attribute(era, "eraid") ?? string.Empty;
            if (!SafeDigits(id) || CompareIds(id, cursor) <= 0 || CompareIds(id, next) > 0)
                throw new FormatException("Invalid Claim.MD ERA ID.");
            return new ClaimMdEraEntry(id);
        }).ToArray();
        if (eras.Length > EraListPageSize)
            throw new FormatException("Claim.MD ERA listing exceeds its documented page size.");
        return new ClaimMdEraPage(next, eras, xml);
    }

    internal async Task<string> GetEra835Async(string? secretReference, string eraId, CancellationToken token)
    {
        if (!SafeDigits(eraId)) throw new ArgumentException("Invalid ERA ID.", nameof(eraId));
        var root = Parse(await PostFormAsync("era835/", secretReference, new("eraid", eraId), token));
        if (Attribute(root, "eraid") != eraId || root.Element("data")?.Value is not { } x12 ||
            x12.Length > ClaimResponseReader.MaximumDocumentCharacters)
            throw new FormatException("Invalid Claim.MD ERA document.");
        var parsed = ClaimResponseReader.ReadDocument(x12);
        if (parsed.Envelope.Kind != ClaimResponseKind.RemittanceAdvice || !parsed.Envelope.IsTestInterchange)
            throw new FormatException("The ERA is not a test 835 document.");
        return x12;
    }

    internal async Task<IReadOnlyList<ClaimMdUploadListing>> ListUploadsAsync(
        string? secretReference, DateOnly uploadDate, int page, CancellationToken token)
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        var root = Parse(await PostFormAsync("uploadlist/", secretReference,
            new("UploadDate", uploadDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), token,
            new KeyValuePair<string, string>("Page", page.ToString(CultureInfo.InvariantCulture))));
        return root.Elements("file").Where(file => Attribute(file, "file_type") == "claim")
            .Select(file =>
            {
                var id = Attribute(file, "inboundid") ?? string.Empty;
                var name = Attribute(file, "filename") ?? string.Empty;
                var time = Attribute(file, "uploadtime") ?? string.Empty;
                if (!SafeDigits(id) || name.Length is < 1 or > 260 ||
                    !long.TryParse(time, NumberStyles.None, CultureInfo.InvariantCulture, out var epoch))
                    throw new FormatException("Invalid Claim.MD upload listing.");
                return new ClaimMdUploadListing(id, name, epoch);
            }).ToArray();
    }

    private async Task<string> PostFormAsync(string path, string? reference,
        KeyValuePair<string, string> first, CancellationToken token, params KeyValuePair<string, string>[] extra)
    {
        var fields = new List<KeyValuePair<string, string>> { new("AccountKey", keys.Resolve(reference)), first };
        fields.AddRange(extra);
        using var form = new FormUrlEncodedContent(fields);
        return await PostAsync(path, form, token);
    }

    private async Task<string> PostAsync(string path, HttpContent content, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Base + path) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        // Redirects must be disabled on the injected handler; never forward an AccountKey.
        if (!response.IsSuccessStatusCode || (int)response.StatusCode is >= 300 and < 400)
            throw new HttpRequestException("Claim.MD did not confirm the request.");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, token)) > 0)
        {
            if (buffer.Length + read > MaximumResponseBytes)
                throw new FormatException("Claim.MD response exceeds the safe limit.");
            buffer.Write(chunk, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static XElement Parse(string xml)
    {
        using var text = new StringReader(xml);
        using var reader = XmlReader.Create(text, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = MaximumResponseBytes
        });
        var root = XDocument.Load(reader).Root;
        if (root?.Name != "result" || root.Elements("error").Any() || root.Attribute("error") is not null)
            throw new FormatException("Claim.MD returned an error or an invalid result.");
        return root;
    }

    private static ClearinghouseUploadResult Unknown(string code, string response) =>
        new(ClearinghouseAttemptOutcome.OutcomeUnknown, null, code, null, null, response);
    private static string? Attribute(XElement element, string name) => element.Attribute(name)?.Value;
    private static bool SafeDigits(string? value) => value is { Length: > 0 and <= 20 } && value.All(char.IsAsciiDigit);
    private static int CompareIds(string a, string b) =>
        decimal.Parse(a, CultureInfo.InvariantCulture).CompareTo(decimal.Parse(b, CultureInfo.InvariantCulture));
}

internal sealed class SandboxConnectorRouter(
    ClearinghouseDispatchGate gate, SyntheticClearinghouseConnector synthetic,
    ClaimMdSandboxConnector claimMd) : IClearinghouseConnector
{
    public Task<ClearinghouseUploadResult> UploadAsync(ClearinghouseUpload upload, CancellationToken token) =>
        gate.IsRealSandboxEnabled && upload.Partner == TradingPartnerKind.ClaimMd
            ? claimMd.UploadAsync(upload, token)
            : gate.IsRealSandboxEnabled
                ? throw new InvalidOperationException("Only Claim.MD test accounts use real sandbox transport.")
                : synthetic.UploadAsync(upload, token);
}

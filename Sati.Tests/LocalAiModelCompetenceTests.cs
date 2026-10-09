using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Options;
using Sati.Models;
using Sati.Services.LocalAi;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace Sati.Tests;

/// <summary>
/// Opt-in device/model release gate. Ordinary CI proves the deterministic security and grounding
/// contract without acquiring model weights. Setting SATI_RUN_LOCAL_AI_MODEL_EVAL=1 explicitly
/// authorizes Foundry Local initialization and any first-use multi-gigabyte model download.
/// </summary>
public sealed class LocalAiModelCompetenceTests(ITestOutputHelper output)
{
    [LocalAiModelFact]
    [Trait("Category", "LocalAiModelEvaluation")]
    public async Task ConfiguredModelCompletesGroundedWorkflowAcrossRepresentativeCurrentNoteInputs()
    {
        using var formatter = new FoundryLocalCaseNoteFormatter(Options.Create(new LocalAiOptions
        {
            Enabled = true,
            ModelAlias = "phi-4-mini",
            MaxInputWords = 500,
            MaxOutputTokens = 900,
            RulesFile = "AI_CASE_NOTE_RULES.md"
        }));

        var scenarios = new[]
        {
            CaseNoteFactCompiler.Build(
                91_001,
                """
                Phone call from Andrew and Rob. Rob stated transportation did not arrive.
                CCM called ModivCare about the standing order. Hanna asked for the schedule by email.
                Follow-up: CCM will confirm transportation Friday.
                """,
                NoteType.Contact,
                null,
                "Joshua White",
                "Andrew",
                null),
            CaseNoteFactCompiler.Build(
                91_002,
                "Consumer selected the morning schedule.",
                NoteType.Visit,
                null,
                "Joshua White",
                "Taylor",
                new VisitDocumentation
                {
                    ConsumerPresent = true,
                    Setting = VisitSetting.Community,
                    Appearance = VisitAppearance.NeatAndAppropriatelyDressed,
                    Participation = VisitParticipation.ParticipatedWithSupport,
                    SafetyObservation = VisitSafetyObservation.NoConcernsObserved,
                    AskedQuestions = true,
                    MadeChoices = true,
                    GoalsReviewed = true,
                    ServicesDiscussed = true,
                    ObservationDetails = "Consumer pointed to Tuesday.",
                    Attendees =
                    [
                        new VisitAttendeeSnapshot
                        {
                            FullName = "Robin Smith",
                            Role = "Shared Living Provider"
                        }
                    ]
                }),
            CaseNoteFactCompiler.Build(
                91_003,
                """
                Guardian Mia stated, "Transportation did not arrive."
                CCM called the provider at 2:30 PM.
                Follow-up: CCM will call Mia Friday.
                """,
                NoteType.Contact,
                null,
                "Joshua White",
                "Morgan",
                null)
        };

        var requestedScenario = Environment.GetEnvironmentVariable("SATI_LOCAL_AI_EVAL_SCENARIO");
        var selectedScenarios = int.TryParse(requestedScenario, out var personId)
            ? scenarios.Where(scenario => scenario.PersonId == personId).ToArray()
            : scenarios;
        Assert.NotEmpty(selectedScenarios);

        foreach (var scenario in selectedScenarios)
        {
            CaseNoteFormattingResult result;
            var diagnostics = LocalAiFirstChanceDiagnostics.StartIfEnabled(scenario.PersonId);
            try
            {
                result = await formatter.FormatAsync(new CaseNoteFormattingRequest(
                    scenario.PersonId,
                    scenario.RawNarrative,
                    scenario.NoteType,
                    scenario.FormType,
                    scenario.CaseManagerFullName,
                    scenario.ConsumerFirstName,
                    scenario.Fingerprint,
                    scenario.Facts));
            }
            catch (CaseNoteDraftRejectedException exception)
            {
                Assert.Fail(
                    $"Scenario {scenario.PersonId} was rejected:{Environment.NewLine}" +
                    string.Join(Environment.NewLine, exception.Errors));
                throw;
            }
            finally
            {
                diagnostics?.Dispose();
                diagnostics?.WriteTo(output);
            }

            var requiredIds = scenario.Facts
                .Where(fact => fact.Required)
                .Select(fact => fact.Id)
                .ToHashSet(StringComparer.Ordinal);
            Assert.True(requiredIds.IsSubsetOf(result.UsedFactIds));
            Assert.Equal(scenario.Fingerprint, result.SourceFingerprint);
            Assert.True(
                result.Warnings.Count == 0,
                $"Scenario {scenario.PersonId} used the deterministic fallback:{Environment.NewLine}" +
                string.Join(Environment.NewLine, result.Warnings));
            Assert.StartsWith(
                $"Community Case Manager (CCM) {scenario.CaseManagerFullName} ",
                result.DraftNarrative,
                StringComparison.Ordinal);
            Assert.Contains("Follow-up:", result.DraftNarrative, StringComparison.Ordinal);

            output.WriteLine($"Scenario {scenario.PersonId}:");
            output.WriteLine(result.DraftNarrative);
            output.WriteLine(string.Empty);
        }

    }

    private sealed class LocalAiFirstChanceDiagnostics : IDisposable
    {
        private const int MaximumEvents = 16;
        private const int MaximumInnerExceptions = 4;
        private const int MaximumNativeErrorCharacters = 32_768;
        private const string ChatCommandErrorPrefix = "Error from chat_completions command: ";
        [ThreadStatic] private static bool observing;
        private readonly object gate = new();
        private readonly ConcurrentQueue<ExceptionEvent> events = new();
        private readonly int scenarioId;
        private bool disposed;
        private int observed;
        private int captureFailures;

        private LocalAiFirstChanceDiagnostics(int scenarioId)
        {
            this.scenarioId = scenarioId;
            AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
        }

        public static LocalAiFirstChanceDiagnostics? StartIfEnabled(int scenarioId) =>
            string.Equals(Environment.GetEnvironmentVariable("SATI_LOCAL_AI_EVAL_DIAGNOSTICS"),
                "1", StringComparison.Ordinal)
                ? new(scenarioId)
                : null;

        private void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs args)
        {
            if (observing || args.Exception is not FoundryLocalException)
                return;

            observing = true;
            try
            {
                lock (gate)
                {
                    if (disposed)
                        return;
                    observed++;
                    if (events.Count >= MaximumEvents)
                        return;

                    var inner = new List<ExceptionMetadata>(MaximumInnerExceptions);
                    for (var current = args.Exception.InnerException;
                         current is not null && inner.Count < MaximumInnerExceptions;
                         current = current.InnerException)
                    {
                        inner.Add(CaptureMetadata(current));
                    }
                    events.Enqueue(new(CaptureMetadata(args.Exception), inner));
                }
            }
            catch
            {
                // Diagnostics must not turn a recoverable model exception into a test/runtime fault.
                Interlocked.Increment(ref captureFailures);
            }
            finally
            {
                observing = false;
            }
        }

        private static ExceptionMetadata CaptureMetadata(Exception exception)
        {
            var type = exception.GetType().FullName ?? exception.GetType().Name;
            var message = exception.Message;
            // SDK command failures can embed the request in Message. Inspect only these fixed
            // flags in memory; never retain Message, the exception, Data, stack or request text.
            // Phrase flags are heuristics: embedded synthetic input can also match them.
            return new(
                type[..Math.Min(type.Length, 160)],
                exception.HResult,
                exception is Win32Exception win32 ? win32.NativeErrorCode : null,
                exception is ExternalException external ? external.ErrorCode : null,
                ClassifyMessage(message),
                ProjectNativeError(message));
        }

        private static MessageFlags ClassifyMessage(string message) => new(
                message.StartsWith(ChatCommandErrorPrefix, StringComparison.Ordinal),
                message.StartsWith("Error executing command 'chat_completions' with input ", StringComparison.Ordinal),
                string.Equals(message, "Failed to deserialize ChatCompletion", StringComparison.Ordinal),
                string.Equals(message, "Exception in callback handler. See InnerException for details", StringComparison.Ordinal),
                message.Contains(") cannot be greater than model context_length (", StringComparison.Ordinal),
                message.Contains(") exceeds max length (", StringComparison.Ordinal) ||
                    message.Contains(") exceeds the max length (", StringComparison.Ordinal),
                message.Contains("User-defined tokens exceed max_length.", StringComparison.Ordinal),
                message.Contains("Try reducing the max_length requested or reducing the batch size.", StringComparison.Ordinal),
                message.Contains("Failed to allocate a block.", StringComparison.Ordinal),
                message.Contains("Provider options not found for provider: ", StringComparison.Ordinal),
                message.Contains("Invalid JSON in tokenizer_json: ", StringComparison.Ordinal),
                message.Contains("Failed to parse tokenizer module json.", StringComparison.Ordinal),
                message.Contains("Error creating grammar: ", StringComparison.Ordinal),
                message.Contains("Error committing tokens: ", StringComparison.Ordinal),
                message.Contains("' is not loaded. Please load the model before getting a ChatClient", StringComparison.Ordinal),
                message.Contains("Invalid type in response format", StringComparison.Ordinal),
                message.Contains("An error occurred while sending the request", StringComparison.Ordinal),
                message.Contains("The server returned an invalid or unrecognized response", StringComparison.Ordinal),
                message.Contains("Invalid or unsupported chat template.", StringComparison.Ordinal),
                message.Contains("Invalid token range: ", StringComparison.Ordinal),
                message.Contains("Invalid 'max_completion_tokens': integer below minimum value. Expected a value >= ", StringComparison.Ordinal),
                message.Contains("[json.exception.", StringComparison.Ordinal),
                ContainsAny(message, "out of memory", "bad_alloc", "bad allocation", "not enough memory",
                    "failed to allocate", "allocation failed"),
                ContainsAny(message, "context length exceeded", "maximum context length", "context window",
                    "max_length", "prompt too long", "too many tokens"),
                ContainsAny(message, "execution provider", "CPUExecutionProvider", "DmlExecutionProvider",
                    "CUDAExecutionProvider", "OpenVINOExecutionProvider", "provider initialization"));

        private static NativeErrorProjection ProjectNativeError(string message)
        {
            if (!message.StartsWith(ChatCommandErrorPrefix, StringComparison.Ordinal))
                return new("PrefixAbsent");
            if (message.Length - ChatCommandErrorPrefix.Length > MaximumNativeErrorCharacters)
                return new("TooLarge");

            var nativeSuffix = message[ChatCommandErrorPrefix.Length..];
            try
            {
                using var json = JsonDocument.Parse(nativeSuffix,
                    new JsonDocumentOptions
                    {
                        MaxDepth = 8,
                        AllowTrailingCommas = false,
                        CommentHandling = JsonCommentHandling.Disallow
                    });
                var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return new("NonObject");

                var keys = new HashSet<string>(StringComparer.Ordinal);
                var duplicateKeys = false;
                var unknownKeys = false;
                foreach (var property in root.EnumerateObject())
                {
                    duplicateKeys |= !keys.Add(property.Name);
                    unknownKeys |= property.Name is not ("code" or "message" or "isTransient");
                }

                var hasCode = root.TryGetProperty("code", out var code);
                var hasMessage = root.TryGetProperty("message", out var nativeMessage);
                var hasTransient = root.TryGetProperty("isTransient", out var transient);
                var codeIsString = hasCode && code.ValueKind == JsonValueKind.String;
                var messageIsString = hasMessage && nativeMessage.ValueKind == JsonValueKind.String;
                var transientIsBoolean = hasTransient &&
                    transient.ValueKind is JsonValueKind.True or JsonValueKind.False;
                // These exact tokens are observations, not causal mappings. Only fixed labels
                // and flags are returned; raw values, JSON and keys never enter the queue/output.
                var codeObserved = !hasCode ? "Missing" : codeIsString && !duplicateKeys
                    ? code.GetString() switch
                    {
                        "InvalidArgument" => "InvalidArgument",
                        "InvalidResponse" => "InvalidResponse",
                        "InternalError" => "InternalError",
                        _ => "Other"
                    }
                    : "Other";
                var decodedMessage = messageIsString && !duplicateKeys ? nativeMessage.GetString() : null;

                return new("Object", hasCode, codeIsString, hasMessage, messageIsString,
                    hasTransient, transientIsBoolean, duplicateKeys, unknownKeys, codeObserved,
                    transientIsBoolean && !duplicateKeys ? transient.GetBoolean() : null,
                    decodedMessage is null ? null : ClassifyMessage(decodedMessage),
                    decodedMessage is null ? null : ClassifyNativeHeuristics(decodedMessage));
            }
            catch (JsonException)
            {
                // Plain native errors are valid diagnostic possibilities; JSON is not assumed.
                return new("InvalidJson", Heuristics: ClassifyNativeHeuristics(nativeSuffix));
            }
        }

        private static NativeHeuristicFlags ClassifyNativeHeuristics(string nativeText) => new(
            // Presence only: these broad terms may also occur in quoted synthetic input.
            // Call only with the bounded prefix-stripped suffix or decoded, unambiguous message.
            ContainsAny(nativeText, "json"),
            ContainsAny(nativeText, "parse", "parsing", "parser", "deserialize", "deserialization",
                "decode", "decoding", "malformed"),
            ContainsAny(nativeText, "invalid", "unexpected", "unsupported", "unrecognized",
                "not supported", "not valid"),
            ContainsAny(nativeText, "tool", "function"),
            ContainsAny(nativeText, "generation", "generate", "generated", "completion", "output",
                "response", "sampling", "inference"),
            ContainsAny(nativeText, "abort", "interrupted"),
            ContainsAny(nativeText, "cancel"),
            ContainsAny(nativeText, "timeout", "timed out", "deadline"),
            ContainsAny(nativeText, "model", "load", "unload", "initializ", "session", "disposed", "not ready"),
            ContainsAny(nativeText, "network", "http", "socket", "connection", "connect", "request",
                "server", "service", "transport", "endpoint", "status code", "unavailable", "offline"),
            nativeText.Contains("USE_SAFE_BASELINE", StringComparison.Ordinal));

        private static bool ContainsAny(string message, params string[] phrases) =>
            phrases.Any(phrase => message.Contains(phrase, StringComparison.OrdinalIgnoreCase));

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed)
                    return;
                disposed = true;
                AppDomain.CurrentDomain.FirstChanceException -= OnFirstChanceException;
            }
        }

        public void WriteTo(ITestOutputHelper output)
        {
            try
            {
                output.WriteLine($"Local AI diagnostics scenario {scenarioId}: observed={observed}, " +
                    $"retained={events.Count}, dropped={Math.Max(0, observed - events.Count)}, " +
                    $"captureFailures={captureFailures}. Phrase flags are heuristic.");
                foreach (var captured in events)
                    output.WriteLine(JsonSerializer.Serialize(captured));
            }
            catch
            {
                // Optional diagnostics never change the model competence assertions.
            }
        }

        private sealed record ExceptionEvent(ExceptionMetadata Exception, IReadOnlyList<ExceptionMetadata> InnerExceptions);
        private sealed record ExceptionMetadata(string Type, int HResult, int? Win32NativeErrorCode,
            int? ExternalErrorCode, MessageFlags MessageFlags, NativeErrorProjection NativeError);
        private sealed record MessageFlags(bool ChatCommandError, bool CommandExecutionError,
            bool ResponseDeserializationFailure, bool CallbackFailure, bool ModelContextLengthError,
            bool InputMaxLengthError, bool UserDefinedTokenLimitError, bool LengthOrBatchReductionAdvice,
            bool BlockAllocationFailure, bool ProviderOptionsMissing, bool InvalidTokenizerJson,
            bool TokenizerJsonParseFailure, bool GrammarCreationFailure, bool TokenCommitFailure,
            bool ModelNotLoaded, bool InvalidResponseFormatType, bool RequestSendFailure,
            bool InvalidServerResponse, bool UnsupportedChatTemplate, bool InvalidTokenRange,
            bool InvalidCompletionTokenMinimum, bool GenericNativeJsonFailure, bool MemoryPhrase,
            bool ContextLimitPhrase, bool ProviderPhrase);
        private sealed record NativeErrorProjection(string Shape, bool CodeKeyPresent = false,
            bool CodeIsString = false, bool MessageKeyPresent = false, bool MessageIsString = false,
            bool IsTransientKeyPresent = false, bool IsTransientIsBoolean = false,
            bool DuplicateKeys = false, bool UnknownKeys = false, string CodeObserved = "Missing",
            bool? IsTransient = null, MessageFlags? DecodedMessageFlags = null,
            NativeHeuristicFlags? Heuristics = null);
        private sealed record NativeHeuristicFlags(bool JsonPhrase, bool ParsePhrase, bool InvalidPhrase,
            bool ToolOrFunctionPhrase, bool GenerationOrOutputPhrase, bool AbortPhrase, bool CancelPhrase,
            bool TimeoutPhrase, bool ModelStatePhrase, bool TransportPhrase, bool ExactBaselineSentinelMention);
    }
}

internal sealed class LocalAiModelFactAttribute : FactAttribute
{
    public LocalAiModelFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("SATI_RUN_LOCAL_AI_MODEL_EVAL"),
                "1",
                StringComparison.Ordinal))
        {
            Skip = "Set SATI_RUN_LOCAL_AI_MODEL_EVAL=1 to authorize the on-device Foundry Local model evaluation.";
        }
    }
}

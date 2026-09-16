namespace Sati.Data;

/// <summary>An expected refusal to enter review, not a failed draft write.</summary>
public sealed class NoteSubmissionException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

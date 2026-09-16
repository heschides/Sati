using Sati.Contracts.V1;

namespace Sati.Helpers;

/// <summary>
/// Wording for a consumer's last recorded contact in the client list. The overdue
/// judgement is <see cref="MonthlyContactRules"/>'s, the same one the billing gate
/// uses; the text carries it in words so colour is never the only cue.
/// </summary>
public static class MonthlyContactPresentation
{
    public static string Describe(MonthlyContactStatus? status)
    {
        if (status is null)
            return string.Empty;

        var text = status.LastContactOn is DateTime last
            ? $"Last contact {last:MM/dd/yy}"
            : "No contact recorded";
        return status.IsOverdue ? $"{text} · overdue" : text;
    }

    public static bool IsOverdue(MonthlyContactStatus? status) => status?.IsOverdue ?? false;
}

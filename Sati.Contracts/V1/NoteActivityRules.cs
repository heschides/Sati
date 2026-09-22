namespace Sati.Contracts.V1;

/// <summary>
/// Work documented by one note. These are independent facts, unlike the
/// historical, single-choice NoteType ordinals. Reminder is deliberately absent.
/// </summary>
[Flags]
public enum NoteActivity
{
    None = 0,
    Visit = 1 << 0,
    Phone = 1 << 1,
    Email = 1 << 2,
    Form = 1 << 3,
    Other = 1 << 4
}

public static class NoteActivityRules
{
    public const NoteActivity All = NoteActivity.Visit | NoteActivity.Phone |
        NoteActivity.Email | NoteActivity.Form | NoteActivity.Other;

    public static NoteActivity Effective(int? activities, string? legacyNoteType) =>
        activities is int value ? (NoteActivity)value : FromLegacy(legacyNoteType);

    public static NoteActivity FromLegacy(string? noteType) => noteType switch
    {
        "Visit" => NoteActivity.Visit,
        // Historical Contact never distinguished its medium. New-entry editing
        // has long defaulted it to Phone; the original label remains in history.
        "Contact" or "Phone" => NoteActivity.Phone,
        "Email" => NoteActivity.Email,
        "Form" => NoteActivity.Form,
        "Other" => NoteActivity.Other,
        _ => NoteActivity.None
    };

    public static bool Has(int? activities, string? legacyNoteType, NoteActivity activity) =>
        (Effective(activities, legacyNoteType) & activity) == activity;

    public static bool Has(NoteActivity? activities, string? legacyNoteType, NoteActivity activity) =>
        Has((int?)activities, legacyNoteType, activity);

    public static string? PrimaryLegacyType(NoteActivity activities)
    {
        if ((activities & NoteActivity.Form) != 0) return "Form";
        if ((activities & NoteActivity.Visit) != 0) return "Visit";
        if ((activities & NoteActivity.Phone) != 0) return "Phone";
        if ((activities & NoteActivity.Email) != 0) return "Email";
        if ((activities & NoteActivity.Other) != 0) return "Other";
        return null;
    }

    public static string DisplayLabel(int? activities, string? legacyNoteType)
    {
        if (activities is null)
            return legacyNoteType ?? "Unclassified note";
        var selected = (NoteActivity)activities.Value;
        if (selected == NoteActivity.None)
            return legacyNoteType ?? "Unclassified note";
        var labels = new List<string>(5);
        if ((selected & NoteActivity.Visit) != 0) labels.Add("Visit");
        if ((selected & NoteActivity.Phone) != 0) labels.Add("Phone");
        if ((selected & NoteActivity.Email) != 0) labels.Add("Email");
        if ((selected & NoteActivity.Form) != 0) labels.Add("Form");
        if ((selected & NoteActivity.Other) != 0) labels.Add("Other");
        return string.Join(" + ", labels);
    }

    public static string? Validate(int? activities, string? legacyNoteType)
    {
        if (activities is null) return null; // Existing API callers and stored notes.
        var selected = (NoteActivity)activities.Value;
        if (selected < 0 || (selected & ~All) != 0)
            return "The note has an unknown activity selection.";
        if (string.Equals(legacyNoteType, "Reminder", StringComparison.Ordinal))
            return selected == NoteActivity.None ? null :
                "A Reminder cannot be combined with completed work.";
        if (selected == NoteActivity.None)
            return "Select at least one kind of work for the note.";
        var primary = PrimaryLegacyType(selected);
        // Existing callers can still submit Contact, the historical phone label.
        // Its explicit activity must remain Phone if that label is retained.
        return (string.Equals(legacyNoteType, primary, StringComparison.Ordinal) ||
                (string.Equals(legacyNoteType, "Contact", StringComparison.Ordinal) &&
                 string.Equals(primary, "Phone", StringComparison.Ordinal)))
            ? null
            : "The note's activity selection does not match its primary type.";
    }
}

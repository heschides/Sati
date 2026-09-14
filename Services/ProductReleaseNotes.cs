namespace Sati.Services;

public sealed record ReleaseNoteSection(
    string Title,
    IReadOnlyList<string> Items);

public static class ProductReleaseNotes
{
    public const string ReleaseName = "Safe Local startup restored";
    public const string ReleaseDate = "September 14, 2026";

    public static IReadOnlyList<ReleaseNoteSection> Sections { get; } =
    [
        new(
            "Local startup works with its safety checks intact",
            [
                "The Local client now carries the required SatiProduction database name in tracked, non-secret configuration.",
                "Sati checks that required name against the workstation's private integrated-security connection before opening the database.",
                "A missing or mismatched database name still stops safely instead of opening an unintended database."
            ]),
        new(
            "The Local installer now proves its startup configuration",
            [
                "The installer build refuses a package unless its public database name and private connection agree exactly.",
                "Isolated installer acceptance repeats that complete configuration check on the files actually installed.",
                "SQL usernames and passwords remain forbidden; Local Sati continues to use the signed-in Windows identity."
            ]),
        new(
            "No records or workflows changed",
            [
                "This hotfix adds no database migration and changes no consumer, billing, document, or workflow records.",
                "The 1.3.9 Local client stopped before database access, so installing this release is the recovery step.",
                "All application features delivered in 1.3.9 remain available in this corrected build."
            ])
    ];
}

namespace Sati.Helpers;

/// <summary>
/// Calm, original reflections shown while an authenticated workspace is prepared.
/// This is presentation copy, not a clinical or operational rule.
/// </summary>
internal static class WorkspaceReflections
{
    private static readonly string[] Items =
    [
        "Care begins by noticing what is present, including what does not fit the first explanation. Attention makes room for a better question.",
        "A careful pause is not empty time. It is often where the detail that changes our understanding becomes visible.",
        "Good work rarely starts with certainty. It starts with enough attention to describe the situation honestly.",
        "Listening is more than waiting for a turn to speak. It is allowing another person's meaning to revise our own.",
        "The smallest fact can matter when it belongs to someone's life. Accuracy is one way of treating that life with respect.",
        "Attention has a generous quality: it says that this person, this task, and this moment are worth seeing clearly.",
        "Before a pattern can guide us, each person must still be allowed to be particular. Similar circumstances do not make identical lives.",
        "A hurried answer may close a question too soon. A useful answer leaves enough room for reality to correct it.",
        "Some important changes arrive quietly. They become visible only when we remember where someone began.",
        "To notice well is to hold detail and context together. Neither tells the whole story alone.",

        "Dignity is not something a person earns by making progress. It is the ground from which every helpful action should begin.",
        "A record can describe needs without reducing a person to them. The difference lives in the words we choose and the facts we preserve.",
        "Respect becomes practical in small ways: a name used correctly, a choice made visible, a promise followed through.",
        "People remain the authors of their own lives even when many systems hold pieces of their story. Our work should help those pieces serve them.",
        "A person can need support and still possess expertise no professional can replace: the knowledge of living their own life.",
        "Kindness does not require vagueness. Clear expectations and humane language can occupy the same sentence.",
        "The goal is not to make someone easier for a system to understand. It is to make the system more responsive to the person before it.",
        "A label may organize a task, but it cannot contain a life. Useful categories should remain tools, never conclusions.",
        "Every form represents a person who will continue living after the form is filed. That fact belongs in the room while we complete it.",
        "Respect is durable when it survives inconvenience. It matters most when the easy path would overlook someone's voice.",

        "A durable record is a promise to the future: this is what we knew, this is what we did, and this is what remains uncertain.",
        "Documentation is memory made shareable. Its value depends on whether another person can tell fact, interpretation, and next step apart.",
        "A clear note does not need to sound impressive. It needs to let the next careful reader understand what happened.",
        "Dates, sources, and decisions are modest details with long lives. Preserving them keeps later work from resting on guesswork.",
        "Good records leave room for correction without erasing history. Learning is safer when the earlier understanding remains visible.",
        "Writing something down changes its reach. A sentence may guide care months later, so it deserves the same care as today's conversation.",
        "A record should carry enough context to be fair and no more private detail than the work truly requires.",
        "Consistency makes information trustworthy, but truth sometimes requires an exception. Record the exception rather than forcing it into the pattern.",
        "The strongest audit trail is not the longest one. It is the one that makes responsibility and sequence easy to understand.",
        "When a correction is needed, clarity serves everyone better than concealment. A visible amendment protects both memory and trust.",

        "Humility is not uncertainty about whether our work matters. It is honesty about how much of another person's world we can see.",
        "Expertise grows when it remains teachable. The more we know, the more precisely we can name what we still need to learn.",
        "A good system can support judgment without pretending to replace it. Human situations keep their edges even inside orderly workflows.",
        "Being wrong is not the deepest failure. Refusing the evidence that would help us become less wrong is.",
        "A question asked in good faith can be a form of competence. It keeps confidence from outrunning knowledge.",
        "The first account of a situation is a beginning, not a verdict. New information deserves a real chance to change the picture.",
        "Our tools should make uncertainty visible instead of quietly converting it into fact. Honest gaps are safer than invented completeness.",
        "No role sees the whole journey. Better decisions emerge when each perspective can add what the others cannot observe.",
        "Humility makes revision possible. It allows a better idea to feel like progress rather than defeat.",
        "A careful professional can say both, 'Here is what I understand,' and, 'Here is what I may be missing.'",

        "Boundaries protect the conditions in which care can continue. They are not the opposite of generosity; they help generosity endure.",
        "A sustainable pace respects tomorrow's responsibilities as well as today's urgency. Steady work needs somewhere to return from.",
        "Not every need can be met by one person or in one day. Naming that limit clearly is better than making a promise reality cannot keep.",
        "Privacy is respect expressed through restraint. Information should travel only as far as the work truly needs it to go.",
        "A secure system helps people focus on service without asking them to remember every safeguard alone.",
        "Rest and reflection are part of responsible work. Exhaustion can make the obvious invisible and the uncertain seem settled.",
        "A boundary can be warm and firm at once. Clarity is often kinder than reluctant agreement followed by disappointment.",
        "Urgency deserves attention, but it does not deserve every resource forever. Good systems help the important survive the immediate.",
        "Confidentiality is built from ordinary choices repeated well: the right audience, the necessary detail, the protected path.",
        "We do not honor people by becoming limitless. We honor them by making commitments that can be kept.",

        "Steady work can look uneventful from the outside. Its quiet repetitions are often what make trust possible.",
        "Progress does not always announce itself. Sometimes it is a task returned to, a detail corrected, or a conversation made a little clearer.",
        "A large responsibility becomes workable when the next honest step is visible. Completion begins by making that step small enough to take.",
        "Reliability is care with a calendar. It turns good intentions into something another person can plan around.",
        "There is craft in doing familiar work attentively. Repetition need not become indifference.",
        "A difficult day does not erase the value of earlier effort. Work can resume from the last sound foothold.",
        "The pace of meaningful change is not always the pace of a checklist. Good tools should track progress without pretending to command it.",
        "When the whole path is unclear, preserve what is known and prepare the next decision well. That is movement, too.",
        "Small improvements accumulate when they are made durable. A fix that can be understood and repeated is more than a momentary rescue.",
        "Patience is active when it keeps observing, adjusting, and returning. It is not the same as waiting without attention.",

        "A system reveals its values through the burdens it removes and the burdens it quietly creates. Both deserve measurement.",
        "Efficiency matters because attention is finite. Time not spent wrestling with a tool can return to judgment, conversation, and care.",
        "The best workflow makes the right action easier to see without hiding the reasons behind it.",
        "A useful safeguard should fail visibly. Silence is a poor substitute for assurance.",
        "Automation is most helpful when it handles repetition and leaves meaning where it belongs: with people.",
        "Every shortcut moves responsibility somewhere. Good design makes that destination explicit.",
        "A system should remember what people should not have to remember, while still showing them what deserves a decision.",
        "Speed and care are not enemies when the work is designed well. Removing waste can create more room for attention.",
        "A warning earns trust when it is timely, specific, and possible to act upon. More warnings do not always create more safety.",
        "Good infrastructure is often quiet. Its success is that people can do their work without having to think about it.",

        "Uncertainty is information, not an inconvenience to delete. Naming it helps the next person ask the right question.",
        "Some decisions must be made before every fact is available. Integrity lies in showing which facts carried the decision and which remained unknown.",
        "Ambiguity becomes safer when it is shared plainly. Hidden uncertainty tends to reappear later as false confidence.",
        "A provisional answer can be useful if it remains visibly provisional. Labels should change when the evidence changes.",
        "Two accounts can differ without either being disposable. The work may be to understand what each vantage point could see.",
        "A missing detail should invite inquiry, not invention. Blank space is sometimes the most accurate entry.",
        "Complexity is not always a defect to simplify away. Sometimes it is the honest shape of the situation.",
        "When evidence conflicts, preserving the conflict is more useful than choosing the tidier story too soon.",
        "Confidence should be proportional to what the record can support. Precision includes knowing the limits of a claim.",
        "An unresolved question can still be well managed. Give it an owner, a next step, and a place where it will not disappear.",

        "Collaboration begins when information can cross a boundary without losing its context. A handoff is part of the work, not the space between it.",
        "People coordinate better when responsibility has a name and asking for help has no penalty.",
        "A shared plan is stronger when disagreement can appear inside it. Agreement that hides concern is fragile.",
        "The next person should not have to reconstruct today's reasoning from scattered clues. A thoughtful handoff is a form of welcome.",
        "Trust grows when people can see how a decision was reached, not only what the decision was.",
        "Different roles ask different questions of the same facts. Keeping those questions connected prevents useful knowledge from becoming separate worlds.",
        "A team does not need everyone to notice the same thing. It needs a way for different observations to become shared understanding.",
        "Clear ownership prevents important work from belonging vaguely to everyone and practically to no one.",
        "A good collaboration leaves each person more able to act. It does not merely distribute information; it builds orientation.",
        "When a process crosses many hands, kindness can take the form of leaving the next hand a clear place to begin.",

        "Hope is not a forecast that everything will be easy. It is a reason to keep building conditions in which something better can happen.",
        "A person's future should remain larger than the records we hold about their past.",
        "Good support notices strength without turning it into a demand to struggle alone.",
        "Change often begins before it becomes measurable. A new question, a safer conversation, or a returned choice may be its first evidence.",
        "A humane system remembers that setbacks belong to a story, not to a person's identity.",
        "We can be realistic about difficulty without making difficulty the horizon. Honest hope keeps both the obstacle and the possibility in view.",
        "The work is worthwhile even when its results cannot be claimed by one action or one person. Conditions matter, and improving them matters.",
        "A plan can hold direction without pretending the path will be straight. Revision is often evidence that people are still engaged.",
        "Possibility becomes practical when it is connected to a next step, a responsible partner, and enough time to begin.",
        "There is value in preparing well for a future we cannot fully predict. Readiness is one form of hope made concrete."
    ];

    internal static IReadOnlyList<string> All => Items;
    internal static int Count => Items.Length;

    internal static string At(int index)
    {
        if ((uint)index >= (uint)Items.Length)
            throw new ArgumentOutOfRangeException(nameof(index));

        return Items[index];
    }

    internal static WorkspaceReflectionSequence CreateRandomSequence() =>
        new(Random.Shared.Next(Items.Length));
}

/// <summary>
/// Walks every reflection exactly once before returning to the first. The explicit
/// start index is a deterministic seam for tests; the production factory chooses it.
/// </summary>
internal sealed class WorkspaceReflectionSequence
{
    // 37 and 100 are coprime, so modular stepping visits the complete bank without
    // repeating. Keep the invariant covered if the catalog size changes.
    private const int Step = 37;
    private int _index;

    internal WorkspaceReflectionSequence(int startIndex)
    {
        if ((uint)startIndex >= (uint)WorkspaceReflections.Count)
            throw new ArgumentOutOfRangeException(nameof(startIndex));

        _index = startIndex;
    }

    internal string Current => WorkspaceReflections.At(_index);

    internal string MoveNext()
    {
        _index = (_index + Step) % WorkspaceReflections.Count;
        return Current;
    }
}

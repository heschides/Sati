using Sati.Models.Assessments;

namespace Sati.Contracts.V1;

public sealed record AssessmentQuestionDefinition(string Key, string Prompt, string WhyAsked, string CompleteAnswerIncludes, string Avoid, bool UsesSupports, AssessmentQuestionKind Kind, IReadOnlyList<string> Activities);
public sealed record AssessmentSectionDefinition(string Title, string Subtitle, IReadOnlyList<AssessmentQuestionDefinition> Questions);

public static class AssessmentCatalog
{
    public const int Version = 1;
    public static string DisplayPrompt(string template, string name = "the person") => template
        .Replace("{Name}", name).Replace("{subject}", "they").Replace("{object}", "them")
        .Replace("{possessive}", "their").Replace("{reflexive}", "themselves")
        .Replace("{does}", "do").Replace("{Has}", "Have").Replace("{Is}", "Are").Replace("{wants}", "want");
    public static IReadOnlyList<AssessmentSectionDefinition> Sections { get; } = BuildSections();
    public static IReadOnlyList<AssessmentQuestionDefinition> Questions { get; } = Sections.SelectMany(section => section.Questions).ToArray();
    private static IReadOnlyList<AssessmentSectionDefinition> BuildSections()
    {
        var target = new List<AssessmentSectionDefinition>();
        AssessmentQuestionDefinition Q(
            string key, string prompt, string why, string include, string avoid) =>
            new(key, prompt, why, include, avoid, false,
                AssessmentQuestionKind.Narrative, []);
        AssessmentQuestionDefinition SQ(
            string key, string prompt, string why, string include, string avoid) =>
            new(key, prompt, why, include, avoid, true,
                AssessmentQuestionKind.Narrative, []);
        AssessmentQuestionDefinition YN(string key, string prompt) =>
            new(key, prompt, string.Empty, string.Empty, string.Empty, false,
                AssessmentQuestionKind.YesNo, []);
        AssessmentQuestionDefinition HC(string key, string prompt) =>
            new(key, prompt, string.Empty, string.Empty, string.Empty, false,
                AssessmentQuestionKind.HealthConcern, []);
        AssessmentQuestionDefinition TH(string key, string prompt) =>
            new(key, prompt, string.Empty, string.Empty, string.Empty, false,
                AssessmentQuestionKind.Therapy, []);
        AssessmentQuestionDefinition AS(string key, string prompt, params string[] activities) =>
            new(key, prompt, string.Empty, string.Empty, string.Empty, false,
                AssessmentQuestionKind.ActivitySupport, activities);
        void AddSection(
            string title,
            string subtitle,
            params AssessmentQuestionDefinition[] questions) =>
            target.Add(new AssessmentSectionDefinition(title, subtitle, questions));

        AddSection("Getting started", "People & context",
            Q("self-view", "How {Name} sees {reflexive}",
              "Center the assessment in the person’s own understanding of who they are.",
              "Strengths, identity, interests, values, and anything the person wants others to understand.", "A diagnostic or service-based description written only by others."),
            Q("what-people-like", "What do people like about {object}?",
              "Capture strengths that other people experience in their relationship with the person.",
              "Specific qualities, contributions, examples, and perspectives from people who know the person.", "Generic compliments without an example."),
            Q("good-life", "What does a good life look like to {object}?",
              "Describe the person’s own priorities, routines, relationships, places, and experiences that make life meaningful.",
              "Concrete preferences and examples in everyday language.", "Happy, appropriate, doing well, or generic service goals without examples."));

        AddSection("Communication", "Understanding & expression",
            Q("communication-overview", "What should people know about communicating with {Name}?",
              "Explain how the person communicates choices, agreement, refusal, discomfort, and a need for a break.",
              "Methods that work, signs others may miss, processing time, and useful accommodations.", "Labels without describing how communication works."),
            Q("receptive-communication", "What support {does} {subject} need with receptive communication?",
              "Describe what helps the person understand information from other people.",
              "Language, pacing, visual supports, repetition, environment, and how understanding is confirmed.", "Understands or does not understand without examples."),
            Q("expressive-communication", "What support {does} {subject} need with expressive communication?",
              "Describe what helps the person communicate thoughts, needs, preferences, and decisions.",
              "Speech, gestures, devices, behavior, time, prompting, and how others confirm meaning.", "Verbal or nonverbal as a complete description."),
            YN("communication-assessment-received", "{Has} {subject} received a communication assessment?"),
            YN("communication-assessment-schedule", "Should a communication assessment be scheduled for the coming year?"));

        AddSection("Home & daily life", "Home, routines & personal activities",
            Q("home-independent", "What activities does {Name} do at home independently?",
              "Identify existing independence before describing support needs.",
              "Specific household, personal, and routine activities completed independently.", "Independent as a general label without examples."),
            Q("home-supports", "What supports {does} {subject} need in the home with daily activities?",
              "Describe how support helps while preserving choice, privacy, and existing skills.",
              "Who helps, what they do, when support is needed, and what the person still does.", "Needs help with ADLs without naming the activity or support."),
            AS("home-support-levels", "Indicate the level of support needed for each home and daily-life activity.",
               "Meal preparation", "Eating and drinking", "Household cleaning", "Laundry", "Shopping and errands",
               "Personal hygiene", "Dressing", "Toileting", "Medication routines", "Mobility and transfers"));

        AddSection("Health & wellness", "Physical, behavioral & emotional health",
            Q("physical-health-view", "How does {Name} feel about {possessive} physical health?",
              "Capture the person’s view of their health, comfort, energy, and access to care.",
              "What feels well, what does not, and the person’s own priorities.", "A diagnosis list without the person’s perspective."),
            Q("physical-health-change", "Is there anything {subject} {wants} to change about {possessive} physical health?",
              "Identify changes the person wants rather than assuming clinical priorities are shared.",
              "Desired change, motivation, barriers, and support requested.", "Provider goals without the person’s view."),
            HC("health-concerns", "Are there health and wellness concerns being actively monitored by healthcare providers?"),
            Q("mental-health-view", "How does {Name} feel about {possessive} mental health?",
              "Capture the person’s view of emotional well-being and any support they value.",
              "Mood, stress, coping, relationships, routines, and what helps.", "A diagnosis or behavior list without the person’s perspective."),
            TH("therapy", "{Is} {subject} seeing a therapist?"));

        AddSection("Safety & rights", "Risk, safeguards, autonomy & restrictions",
            Q("risks", "What current risks require planning or support?",
              "Use factual, individualized information and distinguish possibility from established history.",
              "Trigger, likelihood, consequence, existing safeguard, whether it works, and a backup response.", "Vague labels such as unsafe, elopes, noncompliant, or aggressive without context."),
            Q("rights", "Are any rights, choices, access, or privacy currently restricted?",
              "Identify any rule or practice that limits ordinary access or choice, even if intended for safety.",
              "Specific assessed need, less restrictive approaches tried, consent, review date, and plan to reduce it.", "House rules or provider policy as the sole justification."));

        AddSection("Community & relationships", "Belonging, transportation & social connection",
            SQ("community-access", "How does the person access places and activities they choose?",
               "Describe actual access, not merely what is available in theory.",
               "Chosen destinations, transportation, scheduling, cost, staffing, accessibility, and barriers.", "Has community support or goes into the community without frequency, choice, or barriers."),
            Q("relationships", "Which relationships matter, and what support is wanted to maintain or develop them?",
              "Include paid and unpaid relationships while respecting privacy and the person’s preferences.",
              "Who matters, desired contact, barriers, boundaries, intimacy, and unwanted isolation.", "Family involved or socializes with staff as a complete answer."));

        AddSection("Learning & work", "Employment, education & skill development",
            SQ("employment-learning", "What does the person want regarding work, learning, or meaningful activity?",
               "Start with the person’s interests and desired life outcome before describing services.",
               "Current experience, preferences, strengths, barriers, accommodations, benefits concerns, and next step.", "Not interested unless options were meaningfully explored and the context is documented."));

        AddSection("Choice & advocacy", "Decisions, control & self-advocacy",
            SQ("decision-support", "How does the person make decisions and what support helps?",
               "Describe decision-making by topic rather than treating capacity as all-or-nothing.",
               "How choices are presented, processing time, trusted supporters, risks understood, and final decision-maker.", "Guardian makes decisions without describing the person’s participation."),
            Q("dissent", "How does the person show disagreement, refusal, or a desire for change?",
              "Describe signals and the response expected from supporters.",
              "Words, gestures, behavior, communication technology, escalation signs, and how choices are honored.", "Behaviors when told no without considering whether the person is communicating refusal."));

        AddSection("Summary & needs", "Strengths, unmet needs & priorities",
            Q("strengths", "Which strengths, resources, and supports should the plan build upon?",
              "Identify capabilities and resources that are currently useful, not compliments detached from planning.",
              "Skills, interests, relationships, technology, community resources, routines, and successful strategies.", "Sweet, nice, high-functioning, or resilient without practical examples."),
            Q("priority-needs", "What needs attention during the coming plan year?",
              "Include material needs and broader support, access, autonomy, health, relationship, or planning needs.",
              "What is missing, desired result, urgency, responsible next step, and whether a provider should be associated.", "Needs more services without explaining the need or desired result."));
        return target;
    }

}

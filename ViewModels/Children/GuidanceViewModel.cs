using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace Sati.ViewModels.Children
{
    public record GuidanceBlock(string Title, string Category, string Content)
    {
        public bool MatchesSearch(string search) =>
            string.IsNullOrWhiteSpace(search) ||
            Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            Content.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    public partial class GuidanceBlockViewModel : ObservableObject
    {
        public GuidanceBlock Block { get; }
        public string Title => Block.Title;
        public string Category => Block.Category;
        public string Content => Block.Content;

        [ObservableProperty] private bool isExpanded;
        [ObservableProperty] private bool isVisible = true;

        public GuidanceBlockViewModel(GuidanceBlock block)
        {
            Block = block;
        }

        public void ApplySearch(string search)
        {
            if (Block.MatchesSearch(search))
            {
                IsVisible = true;
                if (!string.IsNullOrWhiteSpace(search))
                    IsExpanded = true;
            }
            else
            {
                IsVisible = false;
                IsExpanded = false;
            }
        }
        [RelayCommand]
        private void Toggle() => IsExpanded = !IsExpanded;
    }

    public partial class GuidanceViewModel : ObservableObject
    {
        // -------------------------------------------------------------------------
        // Observable properties
        // -------------------------------------------------------------------------

        [ObservableProperty] private string? searchText;

        // -------------------------------------------------------------------------
        // Collections
        // -------------------------------------------------------------------------

        public ObservableCollection<GuidanceBlockViewModel> Blocks { get; } = [];

        // -------------------------------------------------------------------------
        // Property change callbacks
        // -------------------------------------------------------------------------

        partial void OnSearchTextChanged(string? value)
        {
            foreach (var block in Blocks)
                block.ApplySearch(value ?? string.Empty);
        }

        // -------------------------------------------------------------------------
        // Commands
        // -------------------------------------------------------------------------

        [RelayCommand]
        public void CollapseAll()
        {
            foreach (var block in Blocks)
                block.IsExpanded = false;
        }

        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        public GuidanceViewModel()
        {
            LoadBlocks();
        }

        // -------------------------------------------------------------------------
        // Private methods
        // -------------------------------------------------------------------------

        private void LoadBlocks()
        {
            var blocks = new[]
            {
                new GuidanceBlock(
                    "Where do I find the ModivCare trip request forms?",
                    "Transportation",
                    "ModivCare trip request forms are available on the ModivCare provider portal at modivcare.com. Log in with your agency credentials and navigate to Trip Management > New Request. You can also call ModivCare directly at 1-844-215-4264 to request a trip by phone. Remember that trips must be scheduled at least 48 hours in advance for non-emergency medical transportation. For same-day urgent trips, contact your supervisor before calling ModivCare so the request can be escalated appropriately."),

                new GuidanceBlock(
                    "What's the difference between a Section 21 and Section 29 waiver?",
                    "Services & Eligibility",
                    "Section 21 (Home and Community-Based Services) and Section 29 (Brain Injury) are both Medicaid waiver programs administered by OADS, but they serve different populations and have different service arrays. Section 21 serves individuals with intellectual disabilities and autism spectrum disorder. Section 29 serves individuals with acquired brain injury. The key practical difference is that Section 29 has a more flexible service array and different funding caps. If you are unsure which waiver a consumer is enrolled in, check their MaineCare member record or contact your OADS liaison."),

                new GuidanceBlock(
                    "Which services require SIPs and which don't?",
                    "Services & Eligibility",
                    "Support Intensity Profiles (SIPs) are required for consumers receiving Home Support, Community Support, or Employment Support services under Section 21. They are not required for consumers receiving only waiver-funded goods and services, assistive technology, or home modifications. Section 29 consumers have a different assessment tool — the Mayo-Portland Adaptability Inventory (MPAI) — rather than the SIP. If a consumer transitions between waivers, a new assessment may be required before services can be authorized. Always confirm with your OADS liaison before scheduling an assessment to avoid unnecessary delays."),

                new GuidanceBlock(
                    "What if the consumer's support agency won't provide a passthrough service?",
                    "Services & Eligibility",
                    "If a support agency declines to provide a passthrough service that has been authorized in the consumer's plan, document the refusal in a service note immediately. Contact your supervisor and your OADS liaison to report the situation. The consumer has the right to change support agencies, and you should discuss this option with them and their guardian or representative if applicable. In urgent situations where the consumer is at risk, contact OADS directly. Do not allow an agency refusal to go undocumented — this protects both the consumer and your caseload record."),

                new GuidanceBlock(
                    "How often can we get AT assessments?",
                    "Services & Eligibility",
                    "Assistive Technology (AT) assessments are generally funded once per waiver year unless there is a documented change in the consumer's functional needs or living situation. If a consumer needs a second assessment within the same year — for example, following a significant health event or a move to a new home — submit a prior authorization request to MaineCare with supporting documentation from the consumer's physician or therapist. AT assessments must be conducted by a qualified AT specialist. Contact your OADS liaison if you are unsure whether a second assessment will be approved before scheduling."),

                new GuidanceBlock(
                    "Can a consumer change their support agency mid-year?",
                    "Services & Eligibility",
                    "Yes. Consumers have the right to change their support agency at any time. Notify OADS of the change as soon as possible so that the new agency can be authorized and services are not interrupted. Document the reason for the change in a service note. If the consumer is changing agencies due to dissatisfaction or a grievance, encourage them to file a formal complaint with the agency before or during the transition. Allow at least 30 days for the transition to be processed, and coordinate directly with both agencies to ensure continuity of care during the changeover period."),

                new GuidanceBlock(
                    "How do I document a failed contact attempt?",
                    "Documentation",
                    "Failed contact attempts should be documented as a Contact note with a status of Pending and a brief narrative describing the attempt — date, time, method (phone, email, home visit), and outcome. For example: 'Attempted contact by phone at 2:15 PM. No answer, voicemail left.' If you make three failed contact attempts within a 30-day period without reaching the consumer or their representative, notify your supervisor and document a summary note. Persistent inability to contact a consumer may require an in-person wellness check or referral to adult protective services depending on the consumer's risk level."),

                new GuidanceBlock(
                    "What happens if a consumer misses their 90-day review window?",
                    "Documentation",
                    "If a 90-day review is not completed within the review window, document the reason in a service note immediately. Contact your supervisor to determine whether a late review can still be accepted by OADS or whether a corrective action plan is required. In most cases, OADS will accept a late review with documentation of the extenuating circumstances. However, repeated late reviews may trigger a quality assurance review of your caseload. Prevention is the best approach — use Sati's upcoming tasks panel to monitor review windows at least 30 days in advance."),

                new GuidanceBlock(
                    "How do I add and remove contacts in Evergreen?",
                    "Documentation",
                    "To add a contact in Evergreen, navigate to the consumer's record and select Contacts > Add New Contact. Fill in the required fields — name, relationship, phone number, and whether the contact has legal authority (guardian, DPOA, etc.). To remove a contact, select the contact record and choose Deactivate rather than Delete — Evergreen does not allow permanent deletion of contact records for audit purposes. If you need to update a contact's legal status, contact your OADS liaison as this may require updated documentation from the consumer or their attorney."),

                new GuidanceBlock(
                    "What local dentists accept MaineCare?",
                    "Community Resources",
                    "MaineCare dental coverage for adults is limited, but several providers in the area accept MaineCare for basic services. The MaineCare member portal at mainecare.maine.gov includes a provider search tool — filter by Dental and MaineCare Accepted. Common options in the region include community health centers, which typically accept all MaineCare plans regardless of managed care organization. For consumers with significant dental needs, consider a referral to the University of New England College of Dental Medicine in Portland, which offers reduced-cost services for MaineCare members. Always verify coverage before scheduling."),

                new GuidanceBlock(
                    "Who do I contact for MaineCare billing questions?",
                    "Community Resources",
                    "For billing questions related to your agency's MaineCare claims, contact your agency's billing department first — they have direct lines to the MaineCare Provider Relations team. For questions about a specific consumer's MaineCare eligibility or coverage, call the MaineCare Member Services line at 1-800-977-6740. For EDI or electronic claims issues, contact the MaineCare EDI help desk at 1-800-964-0341. Do not attempt to contact MaineCare on behalf of a consumer without their consent or the consent of their authorized representative."),
            };

            foreach (var block in blocks)
                Blocks.Add(new GuidanceBlockViewModel(block));
        }
    }
}
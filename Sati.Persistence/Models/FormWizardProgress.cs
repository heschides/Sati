using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;

namespace Sati.Models;

public sealed class FormWizardProgress
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public int AgencyId { get; set; }
    public int AuthorUserId { get; set; }
    public string FormKey { get; set; } = string.Empty;
    public int Revision { get; set; }
    public int StepIndex { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] Ciphertext { get; set; } = [];
    public byte[] Nonce { get; set; } = [];
    public byte[] Tag { get; set; } = [];
    public byte[] WrappedKey { get; set; } = [];
    public string KeyId { get; set; } = string.Empty;

    public ProtectedValue ProtectedAnswers => new(Ciphertext, Nonce, Tag, WrappedKey, KeyId);
    public void SetProtectedAnswers(ProtectedValue value)
    {
        Ciphertext = value.Ciphertext;
        Nonce = value.Nonce;
        Tag = value.Tag;
        WrappedKey = value.WrappedDataKey;
        KeyId = value.KeyId;
    }
    public FieldBinding Binding => new(AgencyId, PersonId, $"FormWizard:{AuthorUserId}:{FormKey}");
}

public static class FormWizardProgressPersistenceModel
{
    public static void Configure<TPerson>(ModelBuilder modelBuilder) where TPerson : class
    {
        modelBuilder.Entity<FormWizardProgress>(entity =>
        {
            entity.ToTable("FormWizardProgress");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FormKey).HasMaxLength(60).IsRequired();
            entity.Property(x => x.KeyId).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Revision).IsConcurrencyToken();
            entity.HasIndex(x => new { x.PersonId, x.AuthorUserId, x.FormKey }).IsUnique();
            entity.HasIndex(x => new { x.AgencyId, x.UpdatedAtUtc });
            entity.HasOne<TPerson>().WithMany().HasForeignKey(x => x.PersonId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

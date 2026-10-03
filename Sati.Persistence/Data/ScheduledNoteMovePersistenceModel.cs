using Microsoft.EntityFrameworkCore;
using Sati.Models;

namespace Sati.Data;

/// <summary>One schema mapping shared by local and API DbContexts.</summary>
public static class ScheduledNoteMovePersistenceModel
{
    public static void Configure<TNote>(ModelBuilder modelBuilder) where TNote : class
    {
        modelBuilder.Entity<ScheduledNoteMove>(entity =>
        {
            entity.ToTable("ScheduledNoteMoves");
            entity.HasKey(move => move.Id);
            entity.Property(move => move.FromDate).HasColumnType("date");
            entity.Property(move => move.ToDate).HasColumnType("date");
            entity.Property(move => move.MovedAtUtc).HasColumnType("datetime2");
            // The note Revision concurrency token, in the writer transaction, is
            // authoritative. A unique insert here could fail before that token's
            // typed conflict when a form conversion races a note edit.
            entity.HasIndex(move => new { move.NoteId, move.NoteRevision });
            entity.HasIndex(move => new { move.UserId, move.FromDate });
            entity.HasOne<TNote>().WithMany().HasForeignKey(move => move.NoteId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

using Microsoft.EntityFrameworkCore;

namespace Workout.Api;

public static class BuddyModel
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<BuddyConnection>().HasKey(c => new { c.FirstId, c.SecondId });
        b.Entity<BuddyConnection>().ToTable("BuddyConnections", t => t.HasCheckConstraint("CK_BuddyConnection_Order", "\"FirstId\" < \"SecondId\""));
        b.Entity<BuddyConnection>().HasOne<Member>().WithMany().HasForeignKey(c => c.FirstId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<BuddyConnection>().HasOne<Member>().WithMany().HasForeignKey(c => c.SecondId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<BuddyConnection>().HasData(Buddies.Connection(Members.YourId, Members.BrittId, DateTimeOffset.UnixEpoch));
        b.Entity<BuddyInvitation>().HasIndex(i => i.TokenHash).IsUnique();
        b.Entity<BuddyInvitation>().HasIndex(i => new { i.InviterId, i.Email }).IsUnique().HasFilter("\"Status\" = 'pending'");
        b.Entity<BuddyInvitation>().Property(i => i.Email).HasMaxLength(320);
        b.Entity<BuddyInvitation>().Property(i => i.TokenHash).HasMaxLength(64).IsConcurrencyToken();
        b.Entity<BuddyInvitation>().Property(i => i.Status).HasMaxLength(12).IsConcurrencyToken();
        b.Entity<BuddyInvitation>().HasOne<Member>().WithMany().HasForeignKey(i => i.InviterId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<BuddyInvitation>().HasOne<Member>().WithMany().HasForeignKey(i => i.AcceptedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

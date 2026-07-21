using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.CodeAnalysis.Elfie.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Mono.TextTemplating;
using OnlineVotingApplication.Models;
using System.Reflection.Emit;

namespace OnlineVotingApplication.Areas.Identity.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);


        // === 1. GUID PRIMARY KEYS & PROPERTY CONSTRAINTS ===

        builder.Entity<States>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Id).HasDefaultValueSql("NEWID()"); // Auto-generates GUID in DB
            entity.Property(s => s.Name).IsRequired().HasMaxLength(100);
        });

        builder.Entity<LGA>(entity =>
        {
            entity.HasKey(l => l.Id);
            entity.Property(l => l.Id).HasDefaultValueSql("NEWID()");
            entity.Property(l => l.Name).IsRequired().HasMaxLength(100);
        });

        builder.Entity<Party>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).HasDefaultValueSql("NEWID()");
            entity.Property(p => p.Name).IsRequired().HasMaxLength(150);
          
        });

        builder.Entity<Positions>(entity =>
        {
            entity.HasKey(pos => pos.Id);
            entity.Property(pos => pos.Id).HasDefaultValueSql("NEWID()");
            entity.Property(pos => pos.Name).IsRequired().HasMaxLength(100);
        });

        builder.Entity<Candidate>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Id).HasDefaultValueSql("NEWID()");
            entity.Property(c => c.Name).IsRequired().HasMaxLength(200);
        });


        // === 2. GUID-BASED RELATIONSHIPS ===

        builder.Entity<LGA>()
            .HasOne(l => l.State)
            .WithMany(s => s.Lgas)
            .HasForeignKey(l => l.StateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Candidate>()
            .HasOne(c => c.Party)
            .WithMany(p => p.Candidates)
            .HasForeignKey(c => c.PartyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Candidate>()
            .HasOne(c => c.Position)
            .WithMany(p => p.Candidates)
            .HasForeignKey(c => c.PositionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Candidate>()
            .HasOne(c => c.LGA)
            .WithMany(l => l.Candidates)
            .HasForeignKey(c => c.LgaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ApplicationUser>()
            .HasOne(u => u.State)
            .WithMany(s => s.Users)
            .HasForeignKey(u => u.StateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Vote>()
            .HasOne(v => v.Positions)
            .WithMany()
            .HasForeignKey(v => v.PositionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Vote>()
            .HasOne(v => v.Candidate)
            .WithMany()
            .HasForeignKey(v => v.CandidateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Vote>()
            .HasOne(v => v.Voter)
            .WithMany()
            .HasForeignKey(v => v.VoterId)
            .OnDelete(DeleteBehavior.Restrict);
    



    builder.Entity<ApplicationUser>(x =>
        {
            x.ToTable("Users");
            x.Property(m => m.FullName).HasMaxLength(200).IsRequired();
        });
    }

    public DbSet<Election> Election { get; set; }
    public DbSet<Party>Party { get; set; }
    public DbSet<Candidate>Candidate { get; set; }
    public DbSet<AuditLog>AuditLogs { get; set; }
    public DbSet<Result> results { get; set; }
    public DbSet<Vote> Votes { get; set; }
    public DbSet<VoterRegistration>voterRegistrations { get; set; }
    public DbSet<States> States { get; set; }
    public DbSet<Positions>Position { get; set; }
    public DbSet<LGA> Lgas { get;  set; }
    public DbSet<PendingEmail>PendingEmails { get; set; }
    public DbSet<PendingFile> PendingFiles { get; set; }
}

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OnlineVotingApplication.Models;
using OnlineVotingApplication.Repository.iServices;
using System;
using System.Linq;
using System.Reflection.Emit;

namespace OnlineVotingApplication.Areas.Identity.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    private readonly ITenantProvider _tenantProvider;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantProvider tenantProvider)
        : base(options)
    {
        _tenantProvider = tenantProvider;
    }

    public Guid? CurrentTenantId => _tenantProvider?.GetCurrentTenantId();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<States>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Id).HasDefaultValueSql("NEWID()");
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

        builder.Entity<ElectionEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("NEWID()");
            entity.Property(e => e.TenantId).IsRequired(false);
        });

        builder.Entity<Candidate>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Id).HasDefaultValueSql("NEWID()");
            entity.Property(c => c.Name).IsRequired().HasMaxLength(200);
            entity.HasIndex(c => c.Slug).IsUnique();
        });

        builder.Entity<CandidateCustomValue>()
            .HasOne(cv => cv.Candidate)
            .WithMany(c => c.CustomValues)
            .HasForeignKey(cv => cv.CandidateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ElectionCustomField>()
            .HasOne(m => m.ElectionEvent)
            .WithMany(e => e.CustomFields)
            .HasForeignKey(f => f.ElectionEventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ElectionCustomField>()
            .Property(m => m.FieldType)
            .HasConversion<string>();

        builder.Entity<CandidateGallery>()
            .HasOne(cg => cg.Candidate)
            .WithMany(c => c.GalleryPhotos)
            .HasForeignKey(cg => cg.CandidateId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<LGA>()
            .HasOne(l => l.State)
            .WithMany(s => s.Lgas)
            .HasForeignKey(l => l.StateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Candidate>()
            .HasOne(c => c.ElectionEvent)
            .WithMany(e => e.Candidates)
            .HasForeignKey(c => c.ElectionEventId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<Candidate>()
            .HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<Candidate>()
            .HasOne(c => c.State)
            .WithMany()
            .HasForeignKey(c => c.StateId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<Candidate>()
            .HasOne(c => c.Party)
            .WithMany(p => p.Candidates)
            .HasForeignKey(c => c.PartyId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<Candidate>()
            .HasOne(c => c.Position)
            .WithMany(p => p.Candidates)
            .HasForeignKey(c => c.PositionId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<Candidate>()
            .HasOne(c => c.LGA)
            .WithMany(l => l.Candidates)
            .HasForeignKey(c => c.LgaId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<ApplicationUser>()
            .HasOne(u => u.State)
            .WithMany(s => s.Users)
            .HasForeignKey(u => u.StateId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Vote>()
            .HasOne(v => v.Positions)
            .WithMany()
            .HasForeignKey(v => v.PositionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Vote>()
            .HasOne(v => v.Voter)
            .WithMany()
            .HasForeignKey(v => v.VoterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Vote>()
            .HasOne(v => v.Candidate)
            .WithMany(c => c.Votes)
            .HasForeignKey(v => v.CandidateId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ApplicationUser>(x =>
        {
            x.ToTable("Users");
            x.Property(m => m.FullName).HasMaxLength(200).IsRequired();
        });

        // ==========================================
        // CANDIDATE INVITATION (Single Unified Block)
        // ==========================================
        builder.Entity<CandidateInvitation>(entity =>
        {
            entity.HasKey(ci => ci.Id);
            entity.Property(ci => ci.Id).HasDefaultValueSql("NEWID()");
            entity.Property(ci => ci.TenantId).IsRequired(false);

            // Relation to ElectionEvent
            entity.HasOne(ci => ci.ElectionEvent)
                  .WithMany(e => e.CandidateInvitations)
                  .HasForeignKey(ci => ci.ElectionEventId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Relation to Tenant (Clean single definition)
            entity.HasOne(ci => ci.Tenant)
                  .WithMany()
                  .HasForeignKey(ci => ci.TenantId)
                  .IsRequired(false)
                  .OnDelete(DeleteBehavior.NoAction);
        });

        // ==========================================
        // GLOBAL QUERY FILTERS FOR MULTI-TENANCY
        // ==========================================
        builder.Entity<ElectionEvent>().HasQueryFilter(e =>
            CurrentTenantId == null || e.TenantId == CurrentTenantId);

        builder.Entity<Party>().HasQueryFilter(p =>
            CurrentTenantId == null || p.TenantId == CurrentTenantId);

        builder.Entity<Positions>().HasQueryFilter(pos =>
            CurrentTenantId == null || pos.TenantId == CurrentTenantId);

        builder.Entity<ElectionCustomField>().HasQueryFilter(f =>
            CurrentTenantId == null || f.TenantId == CurrentTenantId);

        builder.Entity<Candidate>().HasQueryFilter(c =>
            CurrentTenantId == null || c.TenantId == CurrentTenantId);

        builder.Entity<CandidateCustomValue>().HasQueryFilter(c =>
            CurrentTenantId == null || c.TenantId == CurrentTenantId);

        builder.Entity<CandidateGallery>().HasQueryFilter(c =>
            CurrentTenantId == null || c.TenantId == CurrentTenantId);

        builder.Entity<CandidateInvitation>().HasQueryFilter(c =>
            CurrentTenantId == null || c.TenantId == CurrentTenantId);
    }

    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<ElectionEvent> ElectionEvents { get; set; }
    public DbSet<Party> Party { get; set; }
    public DbSet<ElectionCustomField> ElectionCustomFields { get; set; }
    public DbSet<Candidate> Candidate { get; set; }
    public DbSet<CandidateCustomValue> CandidateCustomValues { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<Result> results { get; set; }
    public DbSet<Vote> Votes { get; set; }
    public DbSet<VoterRegistration> voterRegistrations { get; set; }
    public DbSet<States> States { get; set; }
    public DbSet<Positions> Position { get; set; }
    public DbSet<LGA> Lgas { get; set; }
    public DbSet<CandidateInvitation> candidateInvitations { get; set; }
    public DbSet<PendingEmail> PendingEmails { get; set; }
    public DbSet<PendingFile> PendingFiles { get; set; }
    public DbSet<CandidateGallery> CandidateGalleries { get; set; }
    public DbSet<SupportTicket>SupportTickets { get; set; }
    public DbSet<AuditLog> Audits { get; set; }
}
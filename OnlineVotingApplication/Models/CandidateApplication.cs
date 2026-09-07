using Microsoft.CodeAnalysis.Elfie.Diagnostics;
using OnlineVotingApplication.Models;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class CandidateApplication
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid? TenantId { get; set; }

    [Required]
    public Guid? ElectionEventId { get; set; }

    [Required]
    public Guid? PositionId { get; set; }

    [Required, MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    // Tracks workflow status: False = Pending Review, True = Invite Sent / Approved
    public bool IsInviteSent { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties (Optional but helpful for includes)
    [ForeignKey("TenantId")]
    public virtual Tenant? Tenant { get; set; }

    [ForeignKey("ElectionEventId")]
    public virtual ElectionEvent? ElectionEvent { get; set; }

    [ForeignKey("PositionId")]
    public virtual Position? Position { get; set; }
}
using Microsoft.CodeAnalysis.Elfie.Diagnostics;
using OnlineVotingApplication.Models;
using System.ComponentModel.DataAnnotations.Schema;

public class CandidateInvitation
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? ElectionEventId { get; set; }
    public Guid? PositionId { get; set; } // 🌟 Add this property
    public string CandidateEmail { get; set; } = string.Empty;
    public string CandidateName { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public bool IsUsed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Optional Navigation Properties
    public virtual ElectionEvent? ElectionEvent { get; set; }
    public virtual Positions? Position { get; set; } // 🌟 Add this if you want to link the position
    [ForeignKey(nameof(TenantId))] 
    public virtual Tenant? Tenant { get; internal set; }
}
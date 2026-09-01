using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using OnlineVotingApplication.Models;

namespace OnlineVotingApplication.Areas.Identity.Data;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = null!;
    public string? Address { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? VoterRegistrationID { get; set; }
    public DateTime? VoteCastTime { get; set; }
    public string OfficialStaffId { get; set; } = string.Empty;
    public string? DepartmentorAgency { get; set; }
    public Guid? StateId { get; set; }
    public virtual States? State { get; set; }
    public bool HasVoted { get; set; } = false;
    public bool isVoter { get; set; }
    public string? profileImage { get; set; }
    public Guid? TenantId { get; set; }
    [ForeignKey(nameof(TenantId))]
    public virtual Tenant? Tenant { get; set; }
    public bool IsApproved { get; internal set; }
}
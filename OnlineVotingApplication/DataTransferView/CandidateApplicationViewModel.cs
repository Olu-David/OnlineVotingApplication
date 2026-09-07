using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

public class CandidateApplicationViewModel
{
    public Guid? ElectionEventId { get; set; }
    public Guid? TenantId { get; set; }

    [Required]
    [Display(Name = "First Name")]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Last Name")]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress]
    [Display(Name = "Email Address")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Select Position")]
    public Guid? SelectedPositionId { get; set; }

    public IEnumerable<SelectListItem> PositionOptions { get; set; } = new List<SelectListItem>();
}
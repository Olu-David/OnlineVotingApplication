using System;
using System.ComponentModel.DataAnnotations.Schema;
using OnlineVotingApplication.Enums;

namespace OnlineVotingApplication.Models
{
    public class ElectionCustomField
    {
        public Guid Id { get; set; }

        // 🌟 HYBRID IDENTIFIER: Null = Super Admin Blueprint Template | Guid = Tenant Custom Field Copy
        public Guid? TenantId { get; set; }
        public virtual Tenant? Tenant { get; set; }

        public Guid? ElectionEventId { get; set; }

        [ForeignKey(nameof(ElectionEventId))]
        public virtual ElectionEvent? ElectionEvent { get; set; } = null!;

        public string FieldName { get; set; } = string.Empty;
        public ElectionFieldType FieldType { get; set; }

        // 🔒 THE LOCK: Changed from string to Enum. Matches election.Category for blueprint extraction.
        public TenantCategory FormCategory { get; set; } = TenantCategory.Custom;

        public string? CustomChoicesCsv { get; set; }
        public bool IsRequired { get; set; } = true;
    }
}

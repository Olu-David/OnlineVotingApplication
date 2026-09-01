
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineVotingApplication.Models
{
    public class CandidateCustomValue
    {
        public Guid Id { get; set; }
        public Guid CandidateId { get; set; }

        [ForeignKey(nameof(CandidateId))]
        public virtual Candidate Candidate { get; set; } = null!;

        public Guid FieldId { get; set; }

        [ForeignKey(nameof(FieldId))]
        public virtual ElectionCustomField CustomField { get; set; } = null!;

        public string Value { get; set; } = string.Empty; // Stores text user typed
        public Guid? TenantId { get;  set; }
        public virtual Tenant? Tenant { get; set; }
    }
}

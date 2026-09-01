namespace OnlineVotingApplication.DataTransferView
{
    public class TenantMetricsDto
    {
        public int TotalElections { get;  set; }
        public string? ActiveSubscriptionPlan { get;  set; }
        public int? TotalVotesCast { get;  set; }
        public double? TotalApprovedCandidates { get; internal set; }
    }
}
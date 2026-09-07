namespace OnlineVotingApplication.DataTransferView
{
    public class SystemUserStatsViewModel
    {
        public int TotalUsers { get; set; }
        public int VoterCount { get; set; }
        public int CandidateCount { get; set; }
        public int TenantAdminCount { get; set; }
        public int PlatformAdminCount { get; set; }
        public int SuperAdminCount { get; set; }
    }
}

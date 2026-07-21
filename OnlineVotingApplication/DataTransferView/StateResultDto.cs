namespace OnlineVotingApplication.DataTransferView
{
    public class StateResult_Dto
    {
        public string StateName { get; set; } = string.Empty;
        public List<PositionVoteDto> Positions { get; set; } = null!;
    }
}

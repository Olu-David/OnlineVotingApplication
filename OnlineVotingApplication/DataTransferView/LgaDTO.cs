namespace OnlineVotingApplication.DataTransferView
{
    public class LgaDTO
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;

        public Guid StateId { get; set; }
    }
}

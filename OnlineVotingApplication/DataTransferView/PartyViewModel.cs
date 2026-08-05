namespace OnlineVotingApplication.DataTransferView
{
    public class PartyViewModel
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public IFormFile? PartyLogo;
        public string? LogoUrl { get; set; }
       
    }
}

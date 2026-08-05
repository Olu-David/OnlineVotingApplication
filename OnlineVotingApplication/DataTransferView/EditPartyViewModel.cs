namespace OnlineVotingApplication.DataTransferView
{
    public class EditPartyViewModel
    {

        public Guid Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? LogoUrl { get; set; }
        public IFormFile? LogoFile { get; set; }
      
    }
}


namespace OnlineVotingApplication.DataTransferView
{
    public class ElectionDto
    {
        public Guid Id { get;  set; }
        public bool IsActive { get;  set; }
        public DateTime EndDate { get;  set; }
        public string? Title { get;  set; }
        public DateTime StartDate { get;set; }
        public string? PhotoImage { get; set; }
        public IFormFile? UrlImage { get; set; }
        public string? Description { get; set; }
        public string? RegistrationLink { get;  set; }
        public string? ImageUrl { get;  set; }
        public Guid? TenantId { get;  set; }
    }
}

using Microsoft.EntityFrameworkCore.Storage.ValueConversion.Internal;

namespace OnlineVotingApplication.Repository.Settings
{
    public class EmailSettings
    {
        public required string Host { get; set; } = string.Empty;
        public required int Port { get; set; }
        public required string SenderName {  get; set; } = string.Empty;
        public required string SenderEmail { get; set; }
        public required string Password { get; set; }


    }
}

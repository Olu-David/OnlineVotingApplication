using OnlineVotingApplication.Enums;

namespace OnlineVotingApplication.Models
{
    public class PendingFile
    {
        public long Id { get; set; }
        public string FileName { get; set; } = string.Empty;

        public string FolderName { get; set; } = string.Empty;
        public FileType Type { get; set; }
        public FileProcessingStatus Status { get; set; } = FileProcessingStatus.Pending;
        public int RetryCount { get; set; } = 0;
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? FinishedAt
        {
            get; set;

        }
    }
}


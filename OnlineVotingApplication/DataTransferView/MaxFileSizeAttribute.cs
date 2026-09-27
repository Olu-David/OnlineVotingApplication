using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace OnlineVotingApplication.Validation
{
    public class MaxFileSizeAttribute : ValidationAttribute
    {
        private readonly int _maxFileSize;

        public MaxFileSizeAttribute(int maxFileSize)
        {
            _maxFileSize = maxFileSize;
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            // Skip validation if no file was provided (since ProfilePicture is optional)
            if (value is null)
            {
                return ValidationResult.Success;
            }

            if (value is IFormFile file)
            {
                if (file.Length > _maxFileSize)
                {
                    return new ValidationResult(ErrorMessage ?? "File size exceeds the allowed limit.");
                }
            }

            return ValidationResult.Success;
        }
    }
}
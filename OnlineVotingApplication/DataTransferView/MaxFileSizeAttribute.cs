using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace OnlineVotingApplication.Validation
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class MaxFileSizeAttribute : ValidationAttribute
    {
        private readonly int _maxFileSizeInBytes;

        public MaxFileSizeAttribute(int maxFileSizeInBytes)
        {
            _maxFileSizeInBytes = maxFileSizeInBytes;
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value == null)
                return ValidationResult.Success;

            // Single file
            if (value is IFormFile file)
            {
                if (file.Length > _maxFileSizeInBytes)
                {
                    var maxKb = _maxFileSizeInBytes / 1024;
                    return new ValidationResult(
                        ErrorMessage ?? $"File size must not exceed {maxKb} KB.");
                }
            }

            // Multiple files
            if (value is IEnumerable<IFormFile> files)
            {
                foreach (var f in files)
                {
                    if (f != null && f.Length > _maxFileSizeInBytes)
                    {
                        var maxKb = _maxFileSizeInBytes / 1024;
                        return new ValidationResult(
                            ErrorMessage ?? $"Each file must not exceed {maxKb} KB.");
                    }
                }
            }

            return ValidationResult.Success;
        }
    }
}
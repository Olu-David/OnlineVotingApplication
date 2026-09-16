using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.AspNetCore.Http;

namespace OnlineVotingApplication.Validation
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class AllowedImageTypesAttribute : ValidationAttribute
    {
        private readonly string[] _allowedExtensions;
        private static readonly string[] AllowedMimeTypes = new[]
        {
            "image/jpeg", "image/jpg", "image/png", "image/webp", "image/gif"
        };

        public AllowedImageTypesAttribute(params string[] extensions)
        {
            _allowedExtensions = extensions.Select(e => e.ToLowerInvariant()).ToArray();
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value == null) return ValidationResult.Success;

            if (value is IFormFile file)
            {
                return ValidateSingle(file);
            }

            if (value is IEnumerable<IFormFile> files)
            {
                foreach (var f in files)
                {
                    var result = ValidateSingle(f);
                    if (result != ValidationResult.Success) return result;
                }
            }

            return ValidationResult.Success;
        }

        private ValidationResult ValidateSingle(IFormFile file)
        {
            if (file == null || file.Length == 0) return ValidationResult.Success!;

            var ext = System.IO.Path.GetExtension(file.FileName).ToLowerInvariant();

            if (_allowedExtensions.Length > 0 && !_allowedExtensions.Contains(ext))
            {
                return new ValidationResult(
                    ErrorMessage ?? $"Only these file types are allowed: {string.Join(", ", _allowedExtensions)}.");
            }

            if (!AllowedMimeTypes.Contains(file.ContentType.ToLowerInvariant()))
            {
                return new ValidationResult(
                    ErrorMessage ?? "The uploaded file is not a valid image.");
            }

            return ValidationResult.Success!;
        }
    }
}
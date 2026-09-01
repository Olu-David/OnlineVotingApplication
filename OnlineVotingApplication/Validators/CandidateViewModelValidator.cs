using FluentValidation;
using OnlineVotingApplication.DataTransferView;

namespace OnlineVotingApplication.Validation;

public class CandidateViewModelValidator : AbstractValidator<CandidateViewModel>
{
    public CandidateViewModelValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Enter your Candidate Name")
            .MinimumLength(6).WithMessage("Name Length minimum is 6")
            .MaximumLength(100).WithMessage("Name length cannot exceed 100 characters");

        RuleFor(x => x.Manifesto)
            .NotEmpty().WithMessage("Enter your Candidate Manifesto")
            .MaximumLength(1000).WithMessage("The Manifesto text cannot exceed 1000 characters.");

        RuleFor(x => x.CandidateImageUrl)
            .NotNull().WithMessage("Upload Picture of Candidate");

        RuleFor(x => x.ElectionEventId)
            .NotEmpty().WithMessage("An Election Event ID is required.");

        // You can even add custom rules for your dropdown IDs!
        RuleFor(x => x.PartyId)
            .NotEmpty().WithMessage("Please select a political party.");

        RuleFor(x => x.PositionId)
            .NotEmpty().WithMessage("Please select a contest position.");
    }
}

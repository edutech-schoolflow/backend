using EduTech.Shared.Constants;

namespace EduTech.Students.Admissions;

internal static class ApplicationMapper
{
    public static ApplicationResponse Map(ApplicationRow r) => new ApplicationResponse
    {
        Id = r.Id,
        ReferenceNumber = r.ReferenceNumber,
        ChildProfileId = r.ChildProfileId,
        ChildFirstName = r.ChildFirstName,
        ChildMiddleName = r.ChildMiddleName,
        ChildLastName = r.ChildLastName,
        ChildDateOfBirth = r.ChildDateOfBirth,
        ChildGender = r.ChildGender is null ? null : SnakeCaseEnum.Parse<Gender>(r.ChildGender),
        PreviousSchool = r.PreviousSchool,
        MedicalNotes = r.MedicalNotes,
        SchoolId = r.SchoolId,
        SchoolName = r.SchoolName,
        ParentId = r.ParentId,
        ParentName = r.ParentName,
        ParentPhone = r.ParentPhone,
        DesiredClass = r.DesiredClass,
        TermId = r.TermId,
        ApplicationFee = r.ApplicationFee,
        ApplicationFeePaid = r.ApplicationFeePaid,
        PaymentReference = r.PaymentReference,
        Status = SnakeCaseEnum.Parse<ApplicationStatus>(r.Status),
        ExamDate = r.ExamDate,
        ExamTime = r.ExamTime,
        ExamVenue = r.ExamVenue,
        ExamInstructions = r.ExamInstructions,
        AssessmentRating = r.AssessmentRating is null ? null : SnakeCaseEnum.Parse<AssessmentRating>(r.AssessmentRating),
        AssessmentNotes = r.AssessmentNotes,
        RejectionReason = r.RejectionReason,
        AdmissionNumber = r.AdmissionNumber,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt
    };
}

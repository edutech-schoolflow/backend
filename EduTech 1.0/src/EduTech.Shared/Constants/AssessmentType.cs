namespace EduTech.Shared.Constants;

/// <summary>
/// The assessment columns that make up a term's score for a subject (Nigerian standard: two CAs + an
/// exam). Stored as the snake_case string on <c>grade_records.assessment_type</c> via the repo-boundary
/// convention (see [[dapper-enum-storage]]); serialized to the frontend the same way.
/// </summary>
public enum AssessmentType
{
    FirstCa,
    SecondCa,
    Exam
}

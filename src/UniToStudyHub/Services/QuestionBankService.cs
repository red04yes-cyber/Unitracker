using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public sealed record QuestionEvaluation(
    bool? IsCorrect,
    bool RequiresManualReview,
    string Feedback);

/// <summary>
/// CRUD and deterministic evaluation for the question bank. Open answers are
/// deliberately sent to manual review: no fake AI judge is used here.
/// </summary>
public sealed class QuestionBankService
{
    public QuestionBankItemData Add(StudyData data, QuestionBankItemData question)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(question);
        if (string.IsNullOrWhiteSpace(question.Id))
            question.Id = Guid.NewGuid().ToString("N");
        Validate(data, question);
        if (data.Questions.Any(x => string.Equals(x.Id, question.Id, StringComparison.Ordinal)))
            throw new ArgumentException($"Question id '{question.Id}' already exists.", nameof(question));
        question.Text = question.Text.Trim();
        question.Difficulty = string.IsNullOrWhiteSpace(question.Difficulty) ? "Media" : question.Difficulty.Trim();
        question.Source = string.IsNullOrWhiteSpace(question.Source) ? "Manuale" : question.Source.Trim();
        question.Explanation = question.Explanation?.Trim() ?? string.Empty;
        question.CreatedAt = question.CreatedAt == default ? DateTime.UtcNow : question.CreatedAt;
        question.UpdatedAt = DateTime.UtcNow;
        data.Questions.Add(question);
        return question;
    }

    public void Update(StudyData data, QuestionBankItemData updated)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(updated);
        var existing = data.Questions.FirstOrDefault(x => string.Equals(x.Id, updated.Id, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Question '{updated.Id}' does not exist.", nameof(updated));
        Validate(data, updated, existing.Id);
        existing.CourseId = updated.CourseId;
        existing.SyllabusTopicId = updated.SyllabusTopicId;
        existing.Text = updated.Text.Trim();
        existing.Type = updated.Type;
        existing.Difficulty = string.IsNullOrWhiteSpace(updated.Difficulty) ? "Media" : updated.Difficulty.Trim();
        existing.Options = updated.Options ?? new System.Collections.ObjectModel.ObservableCollection<string>();
        existing.CorrectAnswer = updated.CorrectAnswer?.Trim() ?? string.Empty;
        existing.Explanation = updated.Explanation?.Trim() ?? string.Empty;
        existing.Source = string.IsNullOrWhiteSpace(updated.Source) ? "Manuale" : updated.Source.Trim();
        existing.IsOfficial = updated.IsOfficial;
        existing.UpdatedAt = DateTime.UtcNow;
    }

    public void Remove(StudyData data, string questionId)
    {
        ArgumentNullException.ThrowIfNull(data);
        var existing = data.Questions.FirstOrDefault(x => string.Equals(x.Id, questionId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Question '{questionId}' does not exist.", nameof(questionId));
        data.Questions.Remove(existing);
    }

    public IReadOnlyList<QuestionBankItemData> GetForCourse(StudyData data, string courseId) =>
        data.Questions
            .Where(x => string.Equals(x.CourseId, courseId, StringComparison.Ordinal))
            .OrderBy(x => x.Text, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public QuestionEvaluation Evaluate(QuestionBankItemData question, string answer)
    {
        ArgumentNullException.ThrowIfNull(question);
        if (question.Type == QuestionType.Closed)
        {
            var normalized = answer?.Trim() ?? string.Empty;
            var correct = !string.IsNullOrWhiteSpace(question.CorrectAnswer) &&
                          string.Equals(normalized, question.CorrectAnswer.Trim(), StringComparison.OrdinalIgnoreCase);
            return new QuestionEvaluation(correct, false, correct ? "Risposta corretta." : "Risposta non corretta.");
        }

        return new QuestionEvaluation(null, true,
            "Risposta aperta: la valutazione richiede una revisione manuale o un provider AI configurato esplicitamente.");
    }

    private static void Validate(StudyData data, QuestionBankItemData question, string? existingId = null)
    {
        if (!data.Courses.Any(x => string.Equals(x.Id, question.CourseId, StringComparison.Ordinal)))
            throw new ArgumentException($"Course '{question.CourseId}' does not exist.", nameof(question));
        if (string.IsNullOrWhiteSpace(question.Text))
            throw new ArgumentException("Question text is required.", nameof(question));
        if (!Enum.IsDefined(question.Type))
            throw new ArgumentOutOfRangeException(nameof(question), "Question type is invalid.");
        if (!string.IsNullOrWhiteSpace(question.SyllabusTopicId) &&
            !data.SyllabusTopics.Any(x => string.Equals(x.Id, question.SyllabusTopicId, StringComparison.Ordinal) &&
                                          string.Equals(x.CourseId, question.CourseId, StringComparison.Ordinal)))
            throw new ArgumentException("The syllabus topic does not belong to the question course.", nameof(question));

        question.Options ??= new System.Collections.ObjectModel.ObservableCollection<string>();
        if (question.Type == QuestionType.Closed)
        {
            if (question.Options.Count < 2 || question.Options.Distinct(StringComparer.Ordinal).Count() != question.Options.Count)
                throw new ArgumentException("A closed question needs at least two distinct options.", nameof(question));
            if (string.IsNullOrWhiteSpace(question.CorrectAnswer) || !question.Options.Contains(question.CorrectAnswer))
                throw new ArgumentException("A closed question needs a correct answer selected from its options.", nameof(question));
        }
    }
}

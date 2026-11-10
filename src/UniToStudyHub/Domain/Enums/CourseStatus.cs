namespace UniToStudyHub.Domain.Enums;

/// <summary>
/// Lifecycle of a teaching unit. The legacy <c>Completed</c> flag is kept in
/// sync for existing bindings and data, but new code must use this state to
/// distinguish an exam that was passed from one that is merely prepared.
/// </summary>
public enum CourseStatus
{
    NotStarted = 0,
    InProgress = 1,
    Prepared = 2,
    ExamScheduled = 3,
    Passed = 4,
    Failed = 5,
    Archived = 6
}

public enum SyllabusTopicStatus
{
    NotStarted = 0,
    InProgress = 1,
    Completed = 2
}

/// <summary>Hierarchy level for a syllabus entry.</summary>
public enum SyllabusTopicKind
{
    Module = 0,
    Chapter = 1,
    Topic = 2
}

/// <summary>Self-assessed mastery, kept separate from work completion.</summary>
public enum TopicPreparednessLevel
{
    Unrated = 0,
    VeryLow = 1,
    Low = 2,
    Medium = 3,
    High = 4,
    ExamReady = 5
}

public enum QuestionType
{
    Open = 0,
    Closed = 1
}

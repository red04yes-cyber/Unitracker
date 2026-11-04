using System.IO;
using Microsoft.EntityFrameworkCore;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public interface IStudyDataStore
{
    Task<StudyData> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(StudyData data, CancellationToken cancellationToken = default);
}

/// <summary>
/// SQLite-backed store. Every operation gets its own DbContext; writes run in a
/// transaction and are serialized so a failure can never leave a partially
/// replaced dataset. Validation happens before any delete.
/// </summary>
public sealed class SqliteStudyDataStore(IDbContextFactory<StudyHubDbContext> contextFactory) : IStudyDataStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<StudyData> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            return await StudyDataEntityMapper.ReadFromContextAsync(db, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(StudyData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        // Never touch the database with invalid data.
        StudyDataEntityMapper.NormalizeAndValidate(data);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                // Delete children before parents; the whole replacement is atomic.
                await db.QuestionBankItems.ExecuteDeleteAsync(cancellationToken);
                await db.SyllabusTopics.ExecuteDeleteAsync(cancellationToken);
                await db.CoursePrerequisites.ExecuteDeleteAsync(cancellationToken);
                await db.ExamAttempts.ExecuteDeleteAsync(cancellationToken);
                await db.CareerExams.ExecuteDeleteAsync(cancellationToken);
                await db.Announcements.ExecuteDeleteAsync(cancellationToken);
                await db.PlannedExams.ExecuteDeleteAsync(cancellationToken);
                await db.Grades.ExecuteDeleteAsync(cancellationToken);
                await db.Courses.ExecuteDeleteAsync(cancellationToken);
                await db.ProfileSettings.ExecuteDeleteAsync(cancellationToken);

                StudyDataEntityMapper.AddToContext(db, data);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                throw new InvalidDataException(
                    "The data could not be saved; the transaction was rolled back and the previous data is intact.", ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}

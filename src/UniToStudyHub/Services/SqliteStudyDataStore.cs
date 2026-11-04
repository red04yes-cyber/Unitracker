using Microsoft.EntityFrameworkCore;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public interface IStudyDataStore
{
    Task<StudyData> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(StudyData data, CancellationToken cancellationToken = default);
}

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
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(StudyData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        StudyDataEntityMapper.NormalizeAndValidate(data);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
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
        finally { _gate.Release(); }
    }
}

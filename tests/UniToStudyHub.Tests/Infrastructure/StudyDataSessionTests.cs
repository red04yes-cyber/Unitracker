using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Infrastructure;

public sealed class StudyDataSessionTests
{
    [Fact]
    public async Task ConcurrentUpdatesReloadLatestSnapshotAndDoNotLoseChanges()
    {
        await using var database = await SqliteTestDatabase.CreateInMemoryAsync();
        var store = new SqliteStudyDataStore(database.Factory);
        var session = new StudyDataSession(store);
        var notifications = new List<long>();
        session.DataChanged += (_, args) => notifications.Add(args.Revision);

        var updates = Enumerable.Range(1, 12)
            .Select(index => session.UpdateAsync(data =>
            {
                data.Courses.Add(new Course
                {
                    Id = $"course-{index}",
                    Name = $"Course {index}",
                    Cfu = 6
                });
                return index;
            }));

        var results = await Task.WhenAll(updates);
        var names = await session.QueryAsync(data =>
            data.Courses.Select(course => course.Name).OrderBy(name => name).ToArray());

        Assert.Equal(Enumerable.Range(1, 12), results.OrderBy(value => value));
        Assert.Equal(12, names.Length);
        Assert.Equal(12, notifications.Count);
        Assert.Equal(Enumerable.Range(1, 12).Select(value => (long)value), notifications.OrderBy(value => value));
        Assert.Equal(12, session.Revision);
    }

    [Fact]
    public async Task FailedMutationDoesNotPersistOrPublishChange()
    {
        await using var database = await SqliteTestDatabase.CreateInMemoryAsync();
        var session = new StudyDataSession(new SqliteStudyDataStore(database.Factory));
        var notificationCount = 0;
        session.DataChanged += (_, _) => notificationCount++;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.UpdateAsync<int>(data =>
            {
                data.Courses.Add(new Course { Id = "not-saved", Name = "Transient", Cfu = 6 });
                throw new InvalidOperationException("Rejected mutation.");
            }));

        var count = await session.QueryAsync(data => data.Courses.Count);
        Assert.Equal(0, count);
        Assert.Equal(0, notificationCount);
        Assert.Equal(0, session.Revision);
    }
}

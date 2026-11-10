using System.Collections.ObjectModel;
using System.Net;
using System.Text;
using System.Text.Json;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using UniToStudyHub.Tests.Infrastructure;
using UniToStudyHub.Tests.Security;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class NotionIntegrationTests
{
    private static readonly string DataSourceId = "11111111-1111-4111-8111-111111111111";
    private static readonly string PageId = "22222222-2222-4222-8222-222222222222";

    [Fact]
    public void NotionTokenVaultProtectsTokenAndDeletesItOnDisconnect()
    {
        var folder = Path.Combine(Path.GetTempPath(), "UnitrackerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        const string token = "secret_test_token_123";
        try
        {
            var vault = new NotionTokenVault(folder, new TestSecretProtector());
            vault.Save(token);

            Assert.True(vault.HasToken);
            Assert.Equal(token, vault.Load());
            var protectedFile = File.ReadAllBytes(Path.Combine(folder, "secrets", "notion-api-token.secret"));
            Assert.False(protectedFile.AsSpan().IndexOf(Encoding.UTF8.GetBytes(token)) >= 0);

            Assert.True(vault.Delete());
            Assert.False(vault.HasToken);
            Assert.Null(vault.Load());
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ConnectAndPreviewReadsPagedDataAndStoresTokenOnlyAfterSuccess()
    {
        var handler = new RoutingHandler((request, _) =>
        {
            Assert.Equal(NotionApiClient.ApiVersion, request.Headers.GetValues("Notion-Version").Single());
            Assert.Equal("Bearer secret_test_token_123", request.Headers.Authorization?.ToString());
            if (request.RequestUri!.AbsolutePath.EndsWith("/data_sources/" + DataSourceId, StringComparison.Ordinal))
                return Json("""
                    {
                      "id":"11111111-1111-4111-8111-111111111111",
                      "title":[{"plain_text":"Esami — L-33 Economia e Statistica"}],
                      "properties":{"Esame":{"type":"title"},"CFU":{"type":"number"}}
                    }
                    """);
            if (request.RequestUri.AbsolutePath.EndsWith("/data_sources/" + DataSourceId + "/query", StringComparison.Ordinal))
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                return Json(CoursePageJson);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var tokenStore = new InMemoryNotionTokenStore();
        var service = CreateService(handler, tokenStore);

        var preview = await service.ConnectAndPreviewAsync("secret_test_token_123", DataSourceId);

        Assert.Single(preview.Courses);
        Assert.Equal("Microeconomia", preview.Courses[0].Name);
        Assert.Equal("ECM0305", preview.Courses[0].Code);
        Assert.Equal(9, preview.Courses[0].Cfu);
        Assert.Equal("Frequenza attribuita", preview.Courses[0].CareerStatus);
        Assert.True(tokenStore.HasToken);
        Assert.Equal("secret_test_token_123", tokenStore.Load());
    }

    [Fact]
    public async Task InvalidDataSourceDoesNotStoreToken()
    {
        var handler = new RoutingHandler((_, _) => Json("""
            {"id":"11111111-1111-4111-8111-111111111111","title":[],"properties":{"Name":{"type":"title"}}}
            """));
        var tokenStore = new InMemoryNotionTokenStore();
        var service = CreateService(handler, tokenStore);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ConnectAndPreviewAsync("secret_test_token_123", DataSourceId));

        Assert.False(tokenStore.HasToken);
    }

    [Fact]
    public void MergeCoursesIsIdempotentAndPreservesExistingUserValues()
    {
        var data = new StudyData();
        var existing = new Course
        {
            Id = "local-course",
            Name = "Nome personalizzato",
            Code = "ECM0305",
            Cfu = 0,
            Teacher = string.Empty,
            Notes = "Nota personale",
            Source = "manual"
        };
        data.Courses.Add(existing);
        var record = SampleRecord() with
        {
            Name = "Microeconomia",
            CareerStatus = "Superato",
            Grade = 28,
            Teacher = "Docente Notion"
        };

        var first = NotionApplicationService.MergeCourses(data, new[] { record });

        Assert.Equal(0, first.CreatedCourses);
        Assert.Equal(1, first.UpdatedCourses);
        Assert.Equal(1, first.AddedGrades);
        Assert.Equal("Nome personalizzato", existing.Name);
        Assert.Equal("Nota personale", existing.Notes);
        Assert.Equal("ECM0305", existing.Code);
        Assert.Equal(9, existing.Cfu);
        Assert.Equal("Docente Notion", existing.Teacher);
        Assert.Equal(CourseStatus.Passed, existing.Status);
        Assert.True(existing.Completed);
        Assert.Single(data.Grades);
        Assert.Equal(DateTime.UnixEpoch, data.Grades[0].Date);

        var second = NotionApplicationService.MergeCourses(data, new[] { record });

        Assert.Equal(0, second.CreatedCourses);
        Assert.Equal(0, second.UpdatedCourses);
        Assert.Equal(0, second.AddedGrades);
        Assert.Single(data.Courses);
        Assert.Single(data.Grades);
    }

    [Fact]
    public void MergeCoursesSkipsReferenceRowsAndDoesNotDowngradeLocalProgress()
    {
        var data = new StudyData();
        var course = new Course
        {
            Id = "local",
            Name = "Microeconomia",
            Code = "ECM0305",
            Cfu = 9,
            Status = CourseStatus.Prepared,
            Source = "notion:" + PageId.Replace("-", string.Empty, StringComparison.Ordinal)
        };
        data.Courses.Add(course);

        var reference = SampleRecord() with { Name = "REFERENCE — Microeconomia", Code = "ECM0467" };
        var ordinary = SampleRecord() with { CareerStatus = "Da iniziare" };

        var result = NotionApplicationService.MergeCourses(data, new[] { reference, ordinary });

        Assert.Equal(1, result.SkippedReferences);
        Assert.Equal(0, result.CreatedCourses);
        Assert.Equal(CourseStatus.Prepared, course.Status);
        Assert.Single(data.Courses);
    }

    [Fact]
    public async Task NotionBundlePreviewDownloadsFilesAndAppliesOneOrderedMergeOnlyOnRequest()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync();
        var store = new SqliteStudyDataStore(database.Factory);
        await store.SaveAsync(new StudyData
        {
            Courses = new ObservableCollection<Course>
            {
                new() { Id = "local-course", Name = "Locale", Cfu = 6 }
            }
        });
        var importService = new StudyDataImportService(store);
        var tokenStore = new InMemoryNotionTokenStore();
        tokenStore.Save("secret_test_token_123");

        var coreData = Data(("course-1", "Corso dal file principale"));
        var sheetData = Data(("course-1", "Corso duplicato"), ("course-2", "Nuovo corso"));
        var handler = new RoutingHandler((request, _) =>
        {
            if (request.RequestUri!.Host == "api.notion.com")
            {
                Assert.Equal("Bearer secret_test_token_123", request.Headers.Authorization?.ToString());
                Assert.Equal(NotionApiClient.ApiVersion, request.Headers.GetValues("Notion-Version").Single());
                Assert.True(request.RequestUri.AbsolutePath.EndsWith(
                    "/blocks/33333333-3333-4333-8333-333333333333/children", StringComparison.Ordinal));
                return Json("""
                    {
                      "results":[
                        {
                          "id":"44444444-4444-4444-8444-444444444444",
                          "type":"file",
                          "name":"UniToStudyHub-core.json",
                          "has_children":false,
                          "file":{"type":"file","file":{"url":"https://prod-files-secure.s3.us-west-2.amazonaws.com/core.json?X-Amz-Signature=core"}}
                        },
                        {
                          "id":"55555555-5555-4555-8555-555555555555",
                          "type":"file",
                          "name":"UniToStudyHub-schede.json",
                          "has_children":false,
                          "file":{"type":"file","file":{"url":"https://prod-files-secure.s3.us-west-2.amazonaws.com/sheets.json?X-Amz-Signature=sheets"}}
                        }
                      ],
                      "has_more":false,
                      "next_cursor":null
                    }
                    """);
            }

            Assert.Equal("prod-files-secure.s3.us-west-2.amazonaws.com", request.RequestUri.Host);
            Assert.Null(request.Headers.Authorization);
            var content = request.RequestUri.AbsolutePath.EndsWith("core.json", StringComparison.Ordinal)
                ? ExportJson(coreData)
                : ExportJson(sheetData);
            return Json(content);
        });

        var service = new NotionApplicationService(tokenStore,
            new NotionApiClient(new HttpClient(handler)
            {
                BaseAddress = new Uri("https://api.notion.com/v1/")
            }), importService);

        var preview = await service.PreviewImportBundleAsync("33333333-3333-4333-8333-333333333333");

        Assert.Equal(2, preview.FileNames.Count);
        Assert.Equal(2, preview.ImportPreview.ItemCounts["Corsi"]);
        Assert.Contains(preview.ImportPreview.Warnings, warning =>
            warning.Contains("record con ID ripetuto", StringComparison.Ordinal));
        Assert.Single((await store.LoadAsync()).Courses);

        var applied = await service.ApplyImportBundleAsync(preview);

        var saved = await store.LoadAsync();
        Assert.Equal(3, saved.Courses.Count);
        Assert.Equal("Corso dal file principale",
            saved.Courses.Single(x => x.Id == "course-1").Name);
        Assert.Contains(saved.Courses, x => x.Id == "course-2");
        Assert.Equal(2, applied.AddedRecords);
    }

    private static StudyData Data(params (string Id, string Name)[] courses) => new()
    {
        Courses = new ObservableCollection<Course>(courses.Select(x =>
            new Course { Id = x.Id, Name = x.Name, Cfu = 6 }))
    };

    private static string ExportJson(StudyData data) => JsonSerializer.Serialize(new
    {
        schemaVersion = PrivacyService.ExportSchemaVersion,
        generatedAt = DateTimeOffset.Parse("2026-11-08T12:00:00Z"),
        data
    });

    [Fact]
    public async Task MissingTokenFailsBeforeMakingNetworkRequest()
    {
        var handler = new RoutingHandler((_, _) => throw new InvalidOperationException("Network must not be called."));
        var tokenStore = new InMemoryNotionTokenStore();
        var service = CreateService(handler, tokenStore);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PreviewCoursesAsync(DataSourceId));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task ApiErrorDoesNotExposeResponseBodyOrToken()
    {
        var handler = new RoutingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("invalid token secret_test_token_123")
        });
        var client = new NotionApiClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.notion.com/v1/")
        });

        var exception = await Assert.ThrowsAsync<NotionApiException>(() =>
            client.GetDataSourceAsync("secret_test_token_123", DataSourceId));

        Assert.DoesNotContain("secret_test_token_123", exception.Message, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
    }

    private static NotionApplicationService CreateService(RoutingHandler handler, INotionTokenStore tokenStore)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.notion.com/v1/") };
        return new NotionApplicationService(tokenStore, new NotionApiClient(http));
    }

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private static NotionCourseRecord SampleRecord() => new(
        PageId, "https://www.notion.so/" + PageId, "Microeconomia", "ECM0305", 9,
        "Docente", "In corso", null, "2° anno", "Scritto", "Alta", "Studio in corso",
        "Obiettivo principale", new[] { "Invernale 2026/27" }, 25m, 120, null, null,
        "https://elearning.unito.it", "https://www.clest.unito.it", null, true,
        null, null, DateTimeOffset.Parse("2026-11-01T10:00:00Z"));

    private const string CoursePageJson = """
        {
          "results": [
            {
              "id":"22222222-2222-4222-8222-222222222222",
              "url":"https://www.notion.so/22222222222242228222822222222222",
              "last_edited_time":"2026-11-01T10:00:00.000Z",
              "properties":{
                "Esame":{"title":[{"plain_text":"Microeconomia"}]},
                "Codice":{"rich_text":[{"plain_text":"ECM0305"}]},
                "CFU":{"number":9},
                "Docente":{"rich_text":[{"plain_text":"Docente"}]},
                "Stato carriera":{"select":{"name":"Frequenza attribuita"}},
                "Voto":{"number":null},
                "Anno":{"select":{"name":"2° anno"}},
                "Modalità esame":{"select":{"name":"Scritto"}},
                "Priorità":{"select":{"name":"Alta"}},
                "Preparazione sessione":{"select":{"name":"Studio in corso"}},
                "Ruolo nella sessione":{"select":{"name":"Obiettivo principale"}},
                "Sessione target":{"multi_select":[{"name":"Invernale 2026/27"}]},
                "Avanzamento %":{"number":25},
                "Ore stimate":{"number":120},
                "Data appello":{"date":null},
                "Fonte appello":{"url":null},
                "Moodle":{"url":"https://elearning.unito.it"},
                "Scheda UniTo":{"url":"https://www.clest.unito.it"},
                "Propedeuticità":{"rich_text":[]},
                "Studio adesso":{"checkbox":true},
                "Ultima verifica":{"date":null},
                "Ultimo controllo appelli":{"date":null}
              }
            }
          ],
          "has_more":false,
          "next_cursor":null
        }
        """;

    private sealed class RoutingHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> route) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(route(request, cancellationToken));
        }
    }

    private sealed class InMemoryNotionTokenStore : INotionTokenStore
    {
        private string? _token;
        public bool HasToken => _token is not null;
        public string? Load() => _token;
        public void Save(string token) => _token = token;
        public bool Delete()
        {
            var existed = _token is not null;
            _token = null;
            return existed;
        }
    }
}

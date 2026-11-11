using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace UserManagementApi.Tests;

public sealed class UsersApiTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public UsersApiTests() => _client = _factory.AuthorizedClient();

    [Fact]
    public async Task CrudLifecycleReturnsDocumentedStatusesAndPersistsUpdates()
    {
        Assert.Empty((await _client.GetFromJsonAsync<User[]>("/api/users"))!);

        var createdResponse = await _client.PostAsJsonAsync("/api/users",
            new { name = "  Ada Lovelace  ", email = "  ada@example.com  " });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = (await createdResponse.Content.ReadFromJsonAsync<User>())!;
        Assert.True(created.Id > 0);
        Assert.Equal("Ada Lovelace", created.Name);
        Assert.Equal("ada@example.com", created.Email);
        Assert.Equal($"/api/users/{created.Id}", createdResponse.Headers.Location?.OriginalString);
        Assert.Equal(created, await _client.GetFromJsonAsync<User>($"/api/users/{created.Id}"));
        Assert.Equal(created, Assert.Single((await _client.GetFromJsonAsync<User[]>("/api/users"))!));

        var updatedResponse = await _client.PutAsJsonAsync($"/api/users/{created.Id}",
            new { name = "Ada Byron", email = "byron@example.com" });
        Assert.Equal(HttpStatusCode.NoContent, updatedResponse.StatusCode);
        Assert.Equal("", await updatedResponse.Content.ReadAsStringAsync());
        Assert.Equal(new User(created.Id, "Ada Byron", "byron@example.com"),
            await _client.GetFromJsonAsync<User>($"/api/users/{created.Id}"));

        var deletedResponse = await _client.DeleteAsync($"/api/users/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deletedResponse.StatusCode);
        Assert.Equal("", await deletedResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/users/{created.Id}")).StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<User[]>("/api/users"))!);
    }

    [Theory]
    [InlineData(null, "valid@example.com", "name")]
    [InlineData("", "valid@example.com", "name")]
    [InlineData("   ", "valid@example.com", "name")]
    [InlineData(" A ", "valid@example.com", "name")]
    [InlineData("Valid Name", null, "email")]
    [InlineData("Valid Name", "", "email")]
    [InlineData("Valid Name", "   ", "email")]
    [InlineData("Valid Name", "missing-at-sign", "email")]
    [InlineData("Valid Name", "name@", "email")]
    public async Task InvalidFieldsAreRejectedOnCreateAndUpdateWithoutChangingData(
        string? name, string? email, string errorField)
    {
        var existing = await CreateUser("Existing User", "existing@example.com");
        await AssertValidationProblem(await _client.PostAsJsonAsync("/api/users", new { name, email }), errorField);
        await AssertValidationProblem(await _client.PutAsJsonAsync($"/api/users/{existing.Id}", new { name, email }), errorField);
        Assert.Equal(existing, Assert.Single((await _client.GetFromJsonAsync<User[]>("/api/users"))!));
    }

    [Fact]
    public async Task OverlongFieldsAreRejectedOnCreateAndUpdate()
    {
        var existing = await CreateUser("Existing User", "existing@example.com");
        var inputs = new[]
        {
            (Name: new string('N', 101), Email: "valid@example.com", Error: "name"),
            (Name: "Valid Name", Email: new string('a', 243) + "@example.com", Error: "email")
        };
        foreach (var input in inputs)
        {
            await AssertValidationProblem(await _client.PostAsJsonAsync("/api/users",
                new { name = input.Name, email = input.Email }), input.Error);
            await AssertValidationProblem(await _client.PutAsJsonAsync($"/api/users/{existing.Id}",
                new { name = input.Name, email = input.Email }), input.Error);
        }
        Assert.Equal(existing, Assert.Single((await _client.GetFromJsonAsync<User[]>("/api/users"))!));
    }

    [Theory]
    [InlineData("{}")] // Missing fields.
    [InlineData("null")] // JSON null must not reach the endpoint as a valid DTO.
    [InlineData("")] // Missing body.
    [InlineData("{\"name\":")] // Malformed JSON.
    [InlineData("{\"name\":123,\"email\":\"valid@example.com\"}")] // Wrong field type.
    [InlineData("[]")] // Wrong JSON shape.
    public async Task InvalidJsonBodiesReturn400InsteadOf500(string json)
    {
        var existing = await CreateUser("Existing User", "existing@example.com");
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put })
        {
            using var request = new HttpRequestMessage(method,
                method == HttpMethod.Post ? "/api/users" : $"/api/users/{existing.Id}")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
        }
        Assert.Equal(existing, Assert.Single((await _client.GetFromJsonAsync<User[]>("/api/users"))!));
    }

    [Fact]
    public async Task WrongContentTypeReturns415()
    {
        var response = await _client.PostAsync("/api/users", new StringContent("name=Ada"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<User[]>("/api/users"))!);
    }

    [Theory]
    [InlineData("999")]
    [InlineData("-1")]
    [InlineData("0")]
    public async Task UnknownIdsReturn404ForReadUpdateAndDelete(string id)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/users/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PutAsJsonAsync($"/api/users/{id}",
            new { name = "Valid Name", email = "valid@example.com" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/users/{id}")).StatusCode);
    }

    [Fact]
    public async Task NonIntegerIdDoesNotMatchTheRoute()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/users/abc")).StatusCode);
    }

    [Fact]
    public async Task DuplicateEmailsIgnoreCaseAndDoNotOverwriteExistingUsers()
    {
        var first = await CreateUser("First User", "first@example.com");
        var second = await CreateUser("Second User", "second@example.com");

        var duplicatePost = await _client.PostAsJsonAsync("/api/users",
            new { name = "Duplicate User", email = " FIRST@EXAMPLE.COM " });
        Assert.Equal(HttpStatusCode.Conflict, duplicatePost.StatusCode);
        var duplicatePut = await _client.PutAsJsonAsync($"/api/users/{second.Id}",
            new { name = "Should Not Save", email = " FIRST@EXAMPLE.COM " });
        Assert.Equal(HttpStatusCode.Conflict, duplicatePut.StatusCode);
        Assert.Equal(new[] { first, second }, await _client.GetFromJsonAsync<User[]>("/api/users"));

        var ownEmailUpdate = await _client.PutAsJsonAsync($"/api/users/{first.Id}",
            new { name = "First Renamed", email = "FIRST@EXAMPLE.COM" });
        Assert.Equal(HttpStatusCode.NoContent, ownEmailUpdate.StatusCode);
        Assert.Equal("First Renamed", (await _client.GetFromJsonAsync<User>($"/api/users/{first.Id}"))!.Name);
    }

    [Fact]
    public async Task DeleteDoesNotReuseIdsOrChangeOtherUsers()
    {
        var first = await CreateUser("First User", "first@example.com");
        var second = await CreateUser("Second User", "second@example.com");
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/users/{first.Id}")).StatusCode);
        var third = await CreateUser("Third User", "third@example.com");
        Assert.True(third.Id > second.Id);
        Assert.Equal(second, await _client.GetFromJsonAsync<User>($"/api/users/{second.Id}"));
        Assert.Equal(new[] { second, third }, await _client.GetFromJsonAsync<User[]>("/api/users"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer wrong-token")]
    [InlineData("Basic wrong-token")]
    public async Task MissingOrInvalidAuthenticationRejectsEveryCrudOperation(string? header)
    {
        var existing = await CreateUser("Existing User", "existing@example.com");
        using var unauthorized = _factory.CreateClient();
        if (header is not null)
            unauthorized.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", header);

        var responses = new[]
        {
            await unauthorized.GetAsync("/api/users"),
            await unauthorized.GetAsync($"/api/users/{existing.Id}"),
            await unauthorized.PostAsJsonAsync("/api/users", new { name = "New User", email = "new@example.com" }),
            await unauthorized.PutAsJsonAsync($"/api/users/{existing.Id}", new { name = "Changed User", email = "changed@example.com" }),
            await unauthorized.DeleteAsync($"/api/users/{existing.Id}")
        };
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        }
        Assert.Equal(existing, Assert.Single((await _client.GetFromJsonAsync<User[]>("/api/users"))!));
    }

    [Fact]
    public async Task BearerSchemeIsCaseInsensitiveButTokenIsExact()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bEaReR", _factory.Token);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/users")).StatusCode);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _factory.Token + "x");
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task ConcurrentCreatesHaveUniqueIds()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(index =>
            _client.PostAsJsonAsync("/api/users", new { name = $"User {index}", email = $"user{index}@example.com" })));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var users = (await _client.GetFromJsonAsync<User[]>("/api/users"))!;
        Assert.Equal(20, users.Length);
        Assert.Equal(20, users.Select(user => user.Id).Distinct().Count());
    }

    [Fact]
    public async Task ConcurrentDuplicateEmailsCreateExactlyOneUser()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(index =>
            _client.PostAsJsonAsync("/api/users", new { name = $"User {index}", email = index % 2 == 0 ? "same@example.com" : "SAME@EXAMPLE.COM" })));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Equal(19, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        Assert.Single((await _client.GetFromJsonAsync<User[]>("/api/users"))!);
    }

    [Fact]
    public async Task LoggingMiddlewareRecordsFinalStatusWithoutTokenOrBody()
    {
        await _client.PostAsync("/api/users", new StringContent("{\"name\":", Encoding.UTF8, "application/json"));
        using var deniedClient = _factory.CreateClient();
        await deniedClient.GetAsync("/api/users");

        // OnCompleted runs after the response is sent; wait only for the expected log callback.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!_factory.Logs.Entries.Any(entry => entry.Message.StartsWith("POST /api/users -> 400 in ", StringComparison.Ordinal)))
            await Task.Delay(10, deadline.Token);

        var requestLogs = _factory.Logs.Entries.Where(entry =>
            entry.Message.StartsWith("POST /api/users ->", StringComparison.Ordinal) ||
            entry.Message.StartsWith("Denied GET /api/users", StringComparison.Ordinal)).ToArray();
        Assert.Contains(requestLogs, entry => entry.Message.StartsWith("POST /api/users -> 400 in ", StringComparison.Ordinal));
        Assert.Contains(requestLogs, entry => entry.Message == "Denied GET /api/users with 401");
        Assert.All(requestLogs, entry =>
        {
            Assert.DoesNotContain(_factory.Token, entry.Message);
            Assert.DoesNotContain("{\"name\":", entry.Message);
        });
    }

    private async Task<User> CreateUser(string name, string email)
    {
        var response = await _client.PostAsJsonAsync("/api/users", new { name, email });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<User>())!;
    }

    private static async Task AssertValidationProblem(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, document.RootElement.GetProperty("status").GetInt32());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty(field, out var errors));
        Assert.NotEqual(0, errors.GetArrayLength());
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}

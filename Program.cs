using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Microsoft.AspNetCore.Routing;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<UserStore>();
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);

var demoToken = builder.Configuration["DemoAuth:Token"];
if (string.IsNullOrWhiteSpace(demoToken))
    throw new InvalidOperationException("Set the DemoAuth__Token environment variable.");

var app = builder.Build();

// 1. Handle errors from everything further down the pipeline.
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (BadHttpRequestException ex) when (!context.Response.HasStarted)
    {
        await Results.Problem(title: "Invalid HTTP request", statusCode: ex.StatusCode)
            .ExecuteAsync(context);
    }
    catch (Exception ex) when (!context.Response.HasStarted)
    {
        app.Logger.LogError(ex, "Unhandled request error");
        await Results.Problem(title: "Unexpected server error", statusCode: 500)
            .ExecuteAsync(context);
    }
});

// 2. Teaching example only: compare a configured shared bearer token.
app.Use(async (context, next) =>
{
    const string prefix = "Bearer ";
    var authorization = context.Request.Headers.Authorization.ToString();
    if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(authorization[prefix.Length..], demoToken,
            StringComparison.Ordinal))
    {
        context.Response.Headers["WWW-Authenticate"] = "Bearer";
        app.Logger.LogWarning("Denied {Method} {Path} with 401",
            context.Request.Method, context.Request.Path.Value);
        await Results.Problem(
            title: "Valid bearer token required", statusCode: 401)
            .ExecuteAsync(context);
        return;
    }
    await next(context);
});

// 3. Log safe request metadata when the final response has completed.
app.Use(async (context, next) =>
{
    var watch = Stopwatch.StartNew();
    context.Response.OnCompleted(() =>
    {
        app.Logger.LogInformation("{Method} {Path} -> {Status} in {ElapsedMs} ms",
            context.Request.Method, context.Request.Path.Value,
            context.Response.StatusCode, watch.ElapsedMilliseconds);
        return Task.CompletedTask;
    });
    await next(context);
});

app.MapGet("/api/users", (UserStore store) => Results.Ok(store.All()));

app.MapGet("/api/users/{id:int}", (int id, UserStore store) =>
    store.Get(id) is User user
        ? Results.Ok(user)
        : Results.NotFound());

app.MapPost("/api/users", (UserInput input, UserStore store) =>
{
    var errors = Validate(input);
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    var user = store.Create(input.Name!.Trim(), input.Email!.Trim());
    return user is null
        ? Results.Conflict(new { error = "Email already exists." })
        : Results.Created($"/api/users/{user.Id}", user);
});

app.MapPut("/api/users/{id:int}", (int id, UserInput input, UserStore store) =>
{
    var errors = Validate(input);
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    var outcome = store.Update(id, input.Name!.Trim(), input.Email!.Trim());
    return outcome switch
    {
        UpdateOutcome.NotFound => Results.NotFound(),
        UpdateOutcome.EmailConflict => Results.Conflict(new { error = "Email already exists." }),
        _ => Results.NoContent()
    };
});

app.MapDelete("/api/users/{id:int}", (int id, UserStore store) =>
    store.Delete(id) ? Results.NoContent() : Results.NotFound());

app.Run();

static Dictionary<string, string[]> Validate(UserInput input)
{
    var errors = new Dictionary<string, string[]>();
    if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length is < 2 or > 100)
        errors["name"] = ["Name must contain 2 to 100 characters after trimming."];
    if (string.IsNullOrWhiteSpace(input.Email) || input.Email.Trim().Length > 254
        || !new EmailAddressAttribute().IsValid(input.Email.Trim()))
        errors["email"] = ["Enter a valid email address of at most 254 characters."];
    return errors;
}

public sealed record UserInput(string? Name, string? Email);
public sealed record User(int Id, string Name, string Email);
public enum UpdateOutcome { Updated, NotFound, EmailConflict }

// Immutable records and a lock make each store operation safe within one process.
public sealed class UserStore
{
    private readonly object _gate = new();
    private readonly Dictionary<int, User> _users = new();
    private int _nextId = 1;

    public User[] All()
    {
        lock (_gate) return _users.Values.OrderBy(user => user.Id).ToArray();
    }

    public User? Get(int id)
    {
        lock (_gate) return _users.GetValueOrDefault(id);
    }

    public User? Create(string name, string email)
    {
        lock (_gate)
        {
            if (_users.Values.Any(user => SameEmail(user.Email, email))) return null;
            var user = new User(_nextId++, name, email);
            _users.Add(user.Id, user);
            return user;
        }
    }

    public UpdateOutcome Update(int id, string name, string email)
    {
        lock (_gate)
        {
            if (!_users.ContainsKey(id)) return UpdateOutcome.NotFound;
            if (_users.Values.Any(user => user.Id != id && SameEmail(user.Email, email)))
                return UpdateOutcome.EmailConflict;
            _users[id] = new User(id, name, email);
            return UpdateOutcome.Updated;
        }
    }

    public bool Delete(int id)
    {
        lock (_gate) return _users.Remove(id);
    }

    private static bool SameEmail(string first, string second) =>
        string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
}

// Expose the application entry point to the integration test host.
public partial class Program { }

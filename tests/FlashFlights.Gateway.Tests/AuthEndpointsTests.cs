using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FlashFlights.Gateway.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FlashFlights.Gateway.Tests;

/// <summary>
/// Registering, signing in, and asking who the bearer of a token is — the three
/// things the gateway answers itself rather than proxying, because it hosts the
/// one shared user store.
///
/// Driven over HTTP against the wiring <c>Program.cs</c> uses, so a refusal is
/// the status a browser would actually receive.
/// </summary>
public class AuthEndpointsTests
{
    private const string Email = "buyer@flashflights.test";
    private const string Password = "Flash-Sale-2026";

    [Fact]
    public async Task Registering_signs_the_new_user_straight_in()
    {
        await using var gateway = await AuthTestHost.StartGatewayAsync();

        var session = await RegisterAsync(gateway, Email, Password);

        Assert.NotEqual(Guid.Empty, session.UserId);
        Assert.Equal(Email, session.Email);
        Assert.NotEmpty(session.Token);
        Assert.True(session.ExpiresAt > DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Identity reports a taken email as a taken username, twice, because email
    /// is what we register as. A form showing that twice reads like a bug.
    /// </summary>
    [Fact]
    public async Task Registering_an_email_twice_is_refused_once_against_the_email_field()
    {
        await using var gateway = await AuthTestHost.StartGatewayAsync();
        await RegisterAsync(gateway, Email, Password);

        var response = await PostAsync(gateway, "register", Email, Password);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            [IdentityErrorReport.EmailAlreadyRegistered],
            problem!.Errors[IdentityErrorReport.EmailField]);
    }

    [Fact]
    public async Task A_password_the_policy_rejects_is_reported_against_the_password_field()
    {
        await using var gateway = await AuthTestHost.StartGatewayAsync();

        var response = await PostAsync(gateway, "register", Email, "short");
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty(problem!.Errors[IdentityErrorReport.PasswordField]);
    }

    [Fact]
    public async Task An_email_the_policy_rejects_is_reported_against_the_email_field()
    {
        await using var gateway = await AuthTestHost.StartGatewayAsync();

        var response = await PostAsync(gateway, "register", "not-an-email", Password);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty(problem!.Errors[IdentityErrorReport.EmailField]);
    }

    [Fact]
    public async Task A_returning_user_signs_in_with_the_password_they_registered()
    {
        await using var gateway = await AuthTestHost.StartGatewayAsync();
        var registered = await RegisterAsync(gateway, Email, Password);

        var response = await PostAsync(gateway, "login", Email, Password);
        var session = await response.Content.ReadFromJsonAsync<SessionResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(registered.UserId, session!.UserId);
    }

    /// <summary>
    /// A wrong password and an unregistered email are answered identically:
    /// distinguishing them turns this endpoint into a way to find out which
    /// emails are registered.
    /// </summary>
    [Theory]
    [InlineData(Email, "Wrong-Password-1")]
    [InlineData("stranger@flashflights.test", Password)]
    public async Task A_failed_sign_in_never_says_which_half_was_wrong(string email, string password)
    {
        await using var gateway = await AuthTestHost.StartGatewayAsync();
        await RegisterAsync(gateway, Email, Password);

        var response = await PostAsync(gateway, "login", email, password);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(AuthEndpoints.IncorrectCredentials, problem!.Detail);
    }

    /// <summary>
    /// How a client with a stored token finds out whether the system still
    /// accepts it — the check that makes staying signed in across reloads
    /// something the gateway confirms rather than the browser assumes.
    /// </summary>
    [Fact]
    public async Task The_bearer_of_a_token_can_ask_who_it_says_they_are()
    {
        await using var gateway = await AuthTestHost.StartGatewayAsync();
        var session = await RegisterAsync(gateway, Email, Password);

        var request = new HttpRequestMessage(HttpMethod.Get, $"{AuthEndpoints.BasePath}/me");
        request.Headers.Authorization =
            new AuthenticationHeaderValue(JwtBearerDefaults.AuthenticationScheme, session.Token);

        var signedIn = await (await gateway.Client.SendAsync(request)).Content.ReadFromJsonAsync<SignedInUser>();

        Assert.Equal(session.UserId, signedIn!.UserId);
        Assert.Equal(Email, signedIn.Email);
    }

    [Fact]
    public async Task Asking_who_you_are_without_a_token_is_refused()
    {
        await using var gateway = await AuthTestHost.StartGatewayAsync();

        var response = await gateway.Client.GetAsync($"{AuthEndpoints.BasePath}/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<SessionResponse> RegisterAsync(AuthTestServer gateway, string email, string password)
    {
        var response = await PostAsync(gateway, "register", email, password);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<SessionResponse>())!;
    }

    /// <summary>
    /// Posts the plain JSON a browser would, rather than the server's own
    /// request records — so a rename on this side of the wire cannot quietly
    /// change what the SPA has to send.
    /// </summary>
    private static Task<HttpResponseMessage> PostAsync(
        AuthTestServer gateway, string endpoint, string email, string password) =>
        gateway.Client.PostAsJsonAsync($"{AuthEndpoints.BasePath}/{endpoint}", new { email, password });
}

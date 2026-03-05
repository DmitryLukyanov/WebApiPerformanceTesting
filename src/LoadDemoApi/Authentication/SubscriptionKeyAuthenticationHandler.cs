using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace LoadDemoApi.Authentication;

public sealed class SubscriptionKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = "SubscriptionKey";
    public const string HeaderName = "Ocp-Apim-Subscription-Key";

    public string? SubscriptionKey { get; set; }
}

public sealed class SubscriptionKeyAuthenticationHandler : AuthenticationHandler<SubscriptionKeyAuthenticationOptions>
{
    public SubscriptionKeyAuthenticationHandler(
        IOptionsMonitor<SubscriptionKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var expectedKey = Options.SubscriptionKey;

        // No key configured: allow (e.g. local dev without auth)
        if (string.IsNullOrEmpty(expectedKey))
        {
            var identity = new ClaimsIdentity(Array.Empty<Claim>(), Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        if (!Request.Headers.TryGetValue(SubscriptionKeyAuthenticationOptions.HeaderName, out var value) ||
            value != expectedKey)
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing or invalid subscription key."));
        }

        var claims = new[] { new Claim(ClaimTypes.Name, "SubscriptionKey") };
        var id = new ClaimsIdentity(claims, Scheme.Name);
        var p = new ClaimsPrincipal(id);
        var t = new AuthenticationTicket(p, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(t));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.ContentType = "application/json";
        await Response.WriteAsJsonAsync(new { error = "Missing or invalid subscription key. Use header: Ocp-Apim-Subscription-Key" });
    }
}

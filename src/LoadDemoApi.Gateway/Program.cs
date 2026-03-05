var builder = WebApplication.CreateBuilder(args);

var subscriptionKey = builder.Configuration["SubscriptionKey"] ?? "dev-key";

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// APIM-style auth: require Ocp-Apim-Subscription-Key before proxying
app.Use(async (context, next) =>
{
    if (string.IsNullOrEmpty(subscriptionKey))
    {
        await next();
        return;
    }
    const string headerName = "Ocp-Apim-Subscription-Key";
    if (!context.Request.Headers.TryGetValue(headerName, out var key) || key != subscriptionKey)
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid subscription key. Use header: Ocp-Apim-Subscription-Key" });
        return;
    }
    await next();
});

app.MapReverseProxy();

app.Run();

using LoadDemoApi.Authentication;

var builder = WebApplication.CreateBuilder(args);

var subscriptionKey = builder.Configuration["SubscriptionKey"] ?? "";

builder.Services.AddAuthentication(SubscriptionKeyAuthenticationOptions.DefaultScheme)
    .AddScheme<SubscriptionKeyAuthenticationOptions, SubscriptionKeyAuthenticationHandler>(
        SubscriptionKeyAuthenticationOptions.DefaultScheme,
        options => options.SubscriptionKey = subscriptionKey);

builder.Services.AddAuthorization();
builder.Services.AddControllers();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseHttpsRedirection();
app.MapControllers();

app.Run();

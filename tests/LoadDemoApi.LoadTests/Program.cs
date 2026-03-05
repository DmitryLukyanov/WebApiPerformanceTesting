using System.Net.Http;
using LoadDemoApi.LoadTests;
using NBomber.CSharp;

// Configuration: base URL (APIM or direct backend) and optional subscription key for APIM
var baseUrl = Environment.GetEnvironmentVariable("LOAD_BASE_URL") ?? "https://localhost:7001";
var subscriptionKey = Environment.GetEnvironmentVariable("APIM_SUBSCRIPTION_KEY");
var loadProfile = (Environment.GetEnvironmentVariable("LOAD_PROFILE") ?? "medium").ToLowerInvariant();

var baseLoadUrl = $"{baseUrl.TrimEnd('/')}/api/load";

// Profile-based timing: short for CI (low), longer for local/stress runs
var (warmUp, duration, soakDuration) = loadProfile switch
{
    "low" => (TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(15)),
    "high" or "spike" => (TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(60)),
    "soak" => (TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(120)),
    _ => (TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(40))
};

using var httpClient = new HttpClient();
httpClient.Timeout = TimeSpan.FromSeconds(30);
if (!string.IsNullOrEmpty(subscriptionKey))
    httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", subscriptionKey);

// ========== 1. User journey (multi-step, Closed model) ==========
// Simulates a user session: quick browse → default page → heavy page. Each step measured separately.
var scenarioUserJourney = Scenario.Create("user_journey", async context =>
{
    // Step 1: Light – quick "browse" (minimal delay/work)
    var step1 = await Step.Run("browse_light", context, async () =>
    {
        var url = $"{baseLoadUrl}?delayMs=0&workIterations=100";
        var response = await httpClient.GetAsync(url);
        return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
    });
    // Step 2: Medium – default API (typical page)
    var step2 = await Step.Run("page_default", context, async () =>
    {
        var response = await httpClient.GetAsync(baseLoadUrl);
        return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
    });

    // Step 3: Heavy – "heavy" page (more delay + CPU)
    var step3 = await Step.Run("page_heavy", context, async () =>
    {
        var url = $"{baseLoadUrl}?delayMs=80&workIterations=20000";
        var response = await httpClient.GetAsync(url);
        return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
    });

    // Scenario succeeds if all steps completed (step-level stats show per-step ok/fail)
    return Response.Ok();
})
.WithWarmUpDuration(warmUp)
.WithLoadSimulations(
    Simulation.RampingConstant(copies: 10, during: duration),
    Simulation.KeepConstant(copies: 10, during: duration));

// ========== 2. Mixed traffic (Open model) ==========
// Each iteration randomly picks light / medium / heavy – simulates unpredictable mix.
var scenarioMixedTraffic = Scenario.Create("mixed_traffic", async context =>
{
    var kind = Random.Shared.Next(0, 3);
    var url = kind switch
    {
        0 => $"{baseLoadUrl}?delayMs=0&workIterations=200",
        1 => baseLoadUrl,
        _ => $"{baseLoadUrl}?delayMs=60&workIterations=15000"
    };
    var response = await httpClient.GetAsync(url);
    return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
})
.WithWarmUpDuration(warmUp)
.WithLoadSimulations(
    Simulation.RampingInject(rate: 20, interval: TimeSpan.FromSeconds(1), during: duration),
    Simulation.InjectRandom(minRate: 15, maxRate: 35, interval: TimeSpan.FromSeconds(1), during: duration));

// ========== 3. Stress curve: ramp → sustain → spike → cool down (Open model) ==========
var scenarioStressCurve = Scenario.Create("stress_curve", async context =>
{
    var response = await httpClient.GetAsync(baseLoadUrl);
    return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
})
.WithWarmUpDuration(warmUp)
.WithLoadSimulations(
    Simulation.RampingInject(rate: 30, interval: TimeSpan.FromSeconds(1), during: duration),
    Simulation.Inject(rate: 30, interval: TimeSpan.FromSeconds(1), during: duration),
    Simulation.RampingInject(rate: 60, interval: TimeSpan.FromSeconds(1), during: duration),
    Simulation.Inject(rate: 60, interval: TimeSpan.FromSeconds(1), during: duration),
    Simulation.RampingInject(rate: 10, interval: TimeSpan.FromSeconds(1), during: duration));

// ========== 4. Soak / endurance (Closed model) ==========
// Constant concurrent users for extended period to detect leaks or degradation over time.
var scenarioSoak = Scenario.Create("soak", async context =>
{
    var response = await httpClient.GetAsync(baseLoadUrl);
    return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
})
.WithWarmUpDuration(warmUp)
.WithLoadSimulations(
    Simulation.RampingConstant(copies: 15, during: TimeSpan.FromSeconds(10)),
    Simulation.KeepConstant(copies: 15, during: soakDuration));

// ========== 5. Burst (Open model) ==========
// Short spike then sustain – e.g. flash sale or news spike.
var scenarioBurst = Scenario.Create("burst", async context =>
{
    var response = await httpClient.GetAsync(baseLoadUrl);
    return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
})
.WithWarmUpDuration(warmUp)
.WithLoadSimulations(
    Simulation.RampingInject(rate: 50, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(10)),
    Simulation.Inject(rate: 50, interval: TimeSpan.FromSeconds(1), during: duration));

// ========== 6. Heavy-only (Open model) ==========
// All requests are heavy – tests backend under sustained CPU + delay load.
var scenarioHeavyOnly = Scenario.Create("heavy_only", async context =>
{
    var url = $"{baseLoadUrl}?delayMs=100&workIterations=50000";
    var response = await httpClient.GetAsync(url);
    return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
})
.WithWarmUpDuration(warmUp)
.WithLoadSimulations(Simulation.Inject(rate: 8, interval: TimeSpan.FromSeconds(1), during: duration));

var result = NBomberRunner
    .RegisterScenarios(
        scenarioUserJourney,
        scenarioMixedTraffic,
        scenarioStressCurve,
        scenarioSoak,
        scenarioBurst,
        scenarioHeavyOnly)
    .WithReportFolder("nbomber_report")
    .Run();

LoadTestAnalysis.WriteReport(result, "nbomber_report", loadProfile);

using System.Net.Http;
using NBomber.CSharp;

// Configuration: base URL (APIM or direct backend) and optional subscription key for APIM
var baseUrl = Environment.GetEnvironmentVariable("LOAD_BASE_URL") ?? "https://localhost:7001";
var subscriptionKey = Environment.GetEnvironmentVariable("APIM_SUBSCRIPTION_KEY"); // required when targeting APIM
var loadProfile = (Environment.GetEnvironmentVariable("LOAD_PROFILE") ?? "medium").ToLowerInvariant();

var baseLoadUrl = $"{baseUrl.TrimEnd('/')}/api/load";

using var httpClient = new HttpClient();
httpClient.Timeout = TimeSpan.FromSeconds(30);
if (!string.IsNullOrEmpty(subscriptionKey))
    httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", subscriptionKey);

// Shared simulation for "default" single-scenario mode; ignored when multiple scenarios run
var defaultSimulation = loadProfile switch
{
    "low" => Simulation.Inject(rate: 5, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(20)),
    "medium" => Simulation.Inject(rate: 20, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30)),
    "high" => Simulation.Inject(rate: 50, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30)),
    "spike" => Simulation.RampingInject(rate: 100, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(60)),
    "constant" => Simulation.KeepConstant(copies: 15, during: TimeSpan.FromSeconds(30)),
    _ => Simulation.Inject(rate: 20, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30))
};

var warmUp = TimeSpan.FromSeconds(5);
var duration = TimeSpan.FromSeconds(25);

// Scenario 1: Light load – minimal delay/work, many fast requests
var scenarioLight = Scenario.Create("load_light", async context =>
{
    var url = $"{baseLoadUrl}?delayMs=0&workIterations=100";
    var response = await httpClient.GetAsync(url);
    return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
})
.WithWarmUpDuration(warmUp)
.WithLoadSimulations(Simulation.Inject(rate: 30, interval: TimeSpan.FromSeconds(1), during: duration));

// Scenario 2: Medium load – default API params
var scenarioMedium = Scenario.Create("load_medium", async context =>
{
    var response = await httpClient.GetAsync(baseLoadUrl);
    return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
})
.WithWarmUpDuration(warmUp)
.WithLoadSimulations(Simulation.Inject(rate: 15, interval: TimeSpan.FromSeconds(1), during: duration));

// Scenario 3: Heavy load – more delay and CPU work per request
var scenarioHeavy = Scenario.Create("load_heavy", async context =>
{
    var url = $"{baseLoadUrl}?delayMs=100&workIterations=50000";
    var response = await httpClient.GetAsync(url);
    return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
})
.WithWarmUpDuration(warmUp)
.WithLoadSimulations(Simulation.Inject(rate: 5, interval: TimeSpan.FromSeconds(1), during: duration));

// Scenario 4: Burst – short spike of requests (ramp up then sustain)
var scenarioBurst = Scenario.Create("load_burst", async context =>
{
    var response = await httpClient.GetAsync(baseLoadUrl);
    return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
})
.WithWarmUpDuration(warmUp)
.WithLoadSimulations(
    Simulation.RampingInject(rate: 50, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(10)),
    Simulation.Inject(rate: 50, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(15)));

// Run all scenarios in parallel (each has its own stats in the report)
NBomberRunner
    .RegisterScenarios(scenarioLight, scenarioMedium, scenarioHeavy, scenarioBurst)
    .WithReportFolder("nbomber_report")
    .Run();

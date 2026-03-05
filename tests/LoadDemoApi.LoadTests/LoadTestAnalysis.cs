using System.Text;
using System.Text.Json;

namespace LoadDemoApi.LoadTests;

/// <summary>
/// Generates an analysis report from NBomber run result: summary, per-scenario/step breakdown,
/// latency percentiles, failure rates, and simple pass/attention flags.
/// </summary>
public static class LoadTestAnalysis
{
    private static readonly string[] ScenarioNames =
    [
        "user_journey", "mixed_traffic", "stress_curve", "soak", "burst", "heavy_only"
    ];

    private const double FailPercentWarning = 5.0;
    private const double FailPercentCritical = 10.0;
    private const double P99MsWarning = 2000;
    private const double P99MsCritical = 5000;

    public static void WriteReport(object nodeStats, string reportFolder, string loadProfile)
    {
        try
        {
            dynamic stats = nodeStats;
            var scenarios = stats.ScenarioStats;
            var sb = new StringBuilder();
            var analysis = new AnalysisReport { Profile = loadProfile, GeneratedAt = DateTime.UtcNow };

            sb.AppendLine("# Load Test Analysis Report");
            sb.AppendLine();
            sb.AppendLine($"**Profile:** `{loadProfile}` | **Generated:** {analysis.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine();

            foreach (var scenarioName in ScenarioNames)
            {
                if (!Exists(scenarios, scenarioName)) continue;

                dynamic scn = scenarios.Get(scenarioName);
                var scenarioAnalysis = AnalyzeScenario(scenarioName, scn, sb);
                analysis.Scenarios.Add(scenarioAnalysis);
            }

            // Summary and recommendations
            sb.AppendLine("---");
            sb.AppendLine("## Summary & recommendations");
            sb.AppendLine();
            var (attention, critical) = Summarize(analysis.Scenarios, sb);
            analysis.HasAttention = attention;
            analysis.HasCritical = critical;
            sb.AppendLine();

            var mdPath = Path.Combine(reportFolder, "load_test_analysis.md");
            Directory.CreateDirectory(reportFolder);
            File.WriteAllText(mdPath, sb.ToString(), Encoding.UTF8);

            var jsonPath = Path.Combine(reportFolder, "load_test_analysis.json");
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(analysis, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LoadTestAnalysis] Could not write report: {ex.Message}");
        }
    }

    private static bool Exists(dynamic scenarios, string name)
    {
        try
        {
            return (bool)scenarios.Exists(name);
        }
        catch
        {
            return false;
        }
    }

    private static ScenarioAnalysis AnalyzeScenario(string scenarioName, dynamic scn, StringBuilder sb)
    {
        var sa = new ScenarioAnalysis { ScenarioName = scenarioName };

        try
        {
            double okRps = SafeGet(() => (double)scn.Ok.Request.RPS);
            double okPercent = SafeGet(() => (double)scn.Ok.Request.Percent);
            double failPercent = SafeGet(() => (double)scn.Fail.Request.Percent);
            double okCount = SafeGet(() => (double)scn.Ok.Request.Count);
            double failCount = SafeGet(() => (double)scn.Fail.Request.Count);

            double minMs = SafeGet(() => (double)scn.Ok.Latency.MinMs);
            double meanMs = SafeGet(() => (double)scn.Ok.Latency.MeanMs);
            double maxMs = SafeGet(() => (double)scn.Ok.Latency.MaxMs);
            double p50 = SafeGet(() => (double)scn.Ok.Latency.Percent50);
            double p75 = SafeGet(() => (double)scn.Ok.Latency.Percent75);
            double p95 = SafeGet(() => (double)scn.Ok.Latency.Percent95);
            double p99 = SafeGet(() => (double)scn.Ok.Latency.Percent99);

            sa.OkRps = okRps;
            sa.OkPercent = okPercent;
            sa.FailPercent = failPercent;
            sa.OkCount = (long)okCount;
            sa.FailCount = (long)failCount;
            sa.LatencyMinMs = minMs;
            sa.LatencyMeanMs = meanMs;
            sa.LatencyMaxMs = maxMs;
            sa.LatencyP50Ms = p50;
            sa.LatencyP75Ms = p75;
            sa.LatencyP95Ms = p95;
            sa.LatencyP99Ms = p99;

            sb.AppendLine($"## Scenario: `{scenarioName}`");
            sb.AppendLine();
            sb.AppendLine("| Metric | Value |");
            sb.AppendLine("|-------|-------|");
            sb.AppendLine($"| **OK RPS** | {okRps:F1} |");
            sb.AppendLine($"| **OK %** | {okPercent:F1}% |");
            sb.AppendLine($"| **Fail %** | {failPercent:F1}% |");
            sb.AppendLine($"| **OK count** | {okCount:F0} |");
            sb.AppendLine($"| **Fail count** | {failCount:F0} |");
            sb.AppendLine($"| **Latency min** | {minMs:F0} ms |");
            sb.AppendLine($"| **Latency mean** | {meanMs:F0} ms |");
            sb.AppendLine($"| **Latency max** | {maxMs:F0} ms |");
            sb.AppendLine($"| **P50** | {p50:F0} ms |");
            sb.AppendLine($"| **P75** | {p75:F0} ms |");
            sb.AppendLine($"| **P95** | {p95:F0} ms |");
            sb.AppendLine($"| **P99** | {p99:F0} ms |");
            sb.AppendLine();

            // Flags
            var flags = new List<string>();
            if (failPercent >= FailPercentCritical) { flags.Add("Critical: high fail %"); sa.Flags.Add("critical_fail_pct"); }
            else if (failPercent >= FailPercentWarning) { flags.Add("Attention: elevated fail %"); sa.Flags.Add("warning_fail_pct"); }
            if (p99 >= P99MsCritical) { flags.Add("Critical: high P99 latency"); sa.Flags.Add("critical_p99"); }
            else if (p99 >= P99MsWarning) { flags.Add("Attention: elevated P99 latency"); sa.Flags.Add("warning_p99"); }
            if (flags.Count > 0)
                sb.AppendLine("**Flags:** " + string.Join("; ", flags));
            sb.AppendLine();

            // Step-level stats if present (e.g. user_journey)
            try
            {
                dynamic stepStats = scn.StepStats;
                var stepNames = GetStepNames(stepStats);
                if (stepNames.Count > 0)
                {
                    sb.AppendLine("### Steps");
                    sb.AppendLine();
                    sb.AppendLine("| Step | RPS | OK % | Fail % | P50 ms | P99 ms |");
                    sb.AppendLine("|------|-----|------|--------|--------|--------|");
                    foreach (var stepName in stepNames)
                    {
                        try
                        {
                            dynamic step = stepStats.Get(stepName);
                            double sRps = SafeGet(() => (double)step.Ok.Request.RPS);
                            double sOkP = SafeGet(() => (double)step.Ok.Request.Percent);
                            double sFailP = SafeGet(() => (double)step.Fail.Request.Percent);
                            double sP50 = SafeGet(() => (double)step.Ok.Latency.Percent50);
                            double sP99 = SafeGet(() => (double)step.Ok.Latency.Percent99);
                            sb.AppendLine($"| {stepName} | {sRps:F1} | {sOkP:F1}% | {sFailP:F1}% | {sP50:F0} | {sP99:F0} |");
                        }
                        catch
                        {
                            sb.AppendLine($"| {stepName} | - | - | - | - | - |");
                        }
                    }
                    sb.AppendLine();
                }
            }
            catch
            {
                // No step stats
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"*Error analyzing scenario: {ex.Message}*");
            sb.AppendLine();
        }

        return sa;
    }

    private static List<string> GetStepNames(dynamic stepStats)
    {
        var names = new List<string>();
        try
        {
            foreach (var name in new[] { "browse_light", "page_default", "page_heavy" })
            {
                if ((bool)stepStats.Exists(name)) names.Add(name);
            }
        }
        catch { }
        return names;
    }

    private static (bool attention, bool critical) Summarize(List<ScenarioAnalysis> scenarios, StringBuilder sb)
    {
        bool attention = false, critical = false;
        foreach (var s in scenarios)
        {
            if (s.Flags.Contains("critical_fail_pct") || s.Flags.Contains("critical_p99")) critical = true;
            if (s.Flags.Contains("warning_fail_pct") || s.Flags.Contains("warning_p99")) attention = true;
        }
        if (critical) sb.AppendLine("- **Critical:** At least one scenario has high failure rate or very high P99 latency. Review logs and capacity.");
        if (attention && !critical) sb.AppendLine("- **Attention:** Some scenarios have elevated fail % or P99. Consider tuning or increasing resources.");
        if (!attention && !critical) sb.AppendLine("- **OK:** No critical or warning thresholds exceeded.");
        return (attention, critical);
    }

    private static double SafeGet(Func<double> getter)
    {
        try { return getter(); }
        catch { return 0; }
    }
}

internal record AnalysisReport
{
    public string Profile { get; init; } = "";
    public DateTime GeneratedAt { get; init; }
    public List<ScenarioAnalysis> Scenarios { get; } = new();
    public bool HasAttention { get; set; }
    public bool HasCritical { get; set; }
}

internal record ScenarioAnalysis
{
    public string ScenarioName { get; init; } = "";
    public double OkRps { get; set; }
    public double OkPercent { get; set; }
    public double FailPercent { get; set; }
    public long OkCount { get; set; }
    public long FailCount { get; set; }
    public double LatencyMinMs { get; set; }
    public double LatencyMeanMs { get; set; }
    public double LatencyMaxMs { get; set; }
    public double LatencyP50Ms { get; set; }
    public double LatencyP75Ms { get; set; }
    public double LatencyP95Ms { get; set; }
    public double LatencyP99Ms { get; set; }
    public List<string> Flags { get; } = new();
}

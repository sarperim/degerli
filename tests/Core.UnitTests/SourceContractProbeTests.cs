using System.Text.Json;
using Degerli.SourceProbes.Contracts;
using Degerli.SourceProbes.Policy;
using Degerli.SourceProbes.Probing;
using Degerli.SourceProbes.Reporting;
using Degerli.SourceProbes.Shapes;

namespace Degerli.Core.UnitTests;

/// <summary>
/// TKT-foundation-068 — source-contract probes: shape drift against the committed doubles.
/// </summary>
/// <remarks>
/// <para>
/// Every case here is a <b>doubles-only</b> case: the bodies are committed fixture shapes
/// (abbreviated where the double is long, shape-faithful where it is abbreviated), so this
/// suite is deterministic and never touches a network. The live run is an operator command
/// (<c>tools/source-probes</c>), deliberately not a CI gate — the harness exists because two
/// real classes reached production while CI was green: the İş Yatırım <c>HisseTekil</c>
/// envelope that answered <c>value: []</c> for a live session (2026-10-23), and the KAP VYK
/// gateway's <c>400 {"code":"ER005","message":"Bildirim bulunamadı."}</c> for stale cursor
/// indices (2026-10-23, six fail-stopped EOD chains until TKT-foundation-063).
/// </para>
/// <para>
/// Acceptance mapping: (i) shape-equal is clean and drift is named field-by-field — the
/// <c>AC68i_</c> cases; (ii) the harness is not wired into CI — <c>AC68ii_</c>; (iii) no
/// secret and no body value reaches a report — <c>AC68iii_</c>; (iv) the coverage set and
/// runbook are pinned — <c>AC68iv_</c> plus <c>docs/source-contract-probes.md</c>.
/// </para>
/// </remarks>
[Trait("Ticket", "TKT-foundation-068")]
public sealed class SourceContractProbeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    // Shape-faithful excerpt of the committed İş Yatırım EOD double (isyatirim-prices-ASELS,
    // 01 §10.9.1): the envelope fields plus a two-field row. Values are fixture values.
    private const string PricesDoubleJson =
        """{"ok":true,"errorCode":null,"errorDescription":null,"transactionId":"23c0bb08b7f14485b4a7dff0fed2727a","value":[{"HGDG_HS_KODU":"ASELS","HGDG_TARIH":"06-10-2026","HGDG_KAPANIS":376.0000,"HGDG_MIN":364.25,"HGDG_MAX":379.0,"HGDG_HACIM":13239396512.000000},{"HGDG_HS_KODU":"ASELS","HGDG_TARIH":"07-10-2026","HGDG_KAPANIS":348.7500,"HGDG_MIN":346.75,"HGDG_MAX":377.25,"HGDG_HACIM":17063642691.000000}]}""";

    // The same shape with different values: a healthy day whose prices moved.
    private const string PricesDoubleOtherValuesJson =
        """{"ok":true,"errorCode":null,"errorDescription":null,"transactionId":"0000000000000000000000000000000f","value":[{"HGDG_HS_KODU":"THYAO","HGDG_TARIH":"20-10-2026","HGDG_KAPANIS":291.5000,"HGDG_MIN":289.25,"HGDG_MAX":294.0,"HGDG_HACIM":1234.500000},{"HGDG_HS_KODU":"THYAO","HGDG_TARIH":"21-10-2026","HGDG_KAPANIS":295.0000,"HGDG_MIN":292.75,"HGDG_MAX":296.25,"HGDG_HACIM":9876.500000}]}""";

    // A list envelope: the KAP VYK disclosure list double's shape (kap-vyk-disclosures).
    private const string DisclosuresDoubleJson =
        """[{"companyId":"1001","disclosureIndex":"1226255","disclosureClass":"FR","disclosureType":"","title":"Finansal Rapor","acceptedDataFileTypes":["pdf"],"subReportIds":["1"]}]""";

    // ---- (i) shape equality and drift -------------------------------------------------

    /// <summary>AC68(i): a body whose shape matches the double is clean — no findings.</summary>
    [Fact]
    public void AC68i_ShapeEqual_ReportsNoDrift()
    {
        var contract = ContractFor(PricesDoubleJson);

        var result = Evaluate(contract, PricesDoubleJson);

        Assert.Equal(ProbeOutcomeKind.Clean, result.Outcome);
        Assert.Empty(result.Findings);
    }

    /// <summary>
    /// AC68(i): values change every day; the probe compares shape only, so a healthy day with
    /// different prices and a different ticker is clean.
    /// </summary>
    [Fact]
    public void AC68i_ValuesOnlyChanged_StillClean()
    {
        var contract = ContractFor(PricesDoubleJson);

        var result = Evaluate(contract, PricesDoubleOtherValuesJson);

        Assert.Equal(ProbeOutcomeKind.Clean, result.Outcome);
        Assert.Empty(result.Findings);
    }

    /// <summary>AC68(i): a field the double pins that the live body dropped is drift, path named.</summary>
    [Fact]
    public void AC68i_MissingKey_IsDriftWithThePathNamed()
    {
        var contract = ContractFor(DisclosuresDoubleJson);
        var drifted = """[{"companyId":"1001","disclosureIndex":"1226255","disclosureClass":"FR","disclosureType":"","acceptedDataFileTypes":["pdf"],"subReportIds":["1"]}]""";

        var result = Evaluate(contract, drifted);

        Assert.Equal(ProbeOutcomeKind.Drift, result.Outcome);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("$[].title", finding.Path);
        Assert.Equal(DriftKind.MissingKey, finding.Kind);
        Assert.Equal("string", finding.Expected);
        Assert.Equal(DriftSeverity.Breaking, finding.Severity);
    }

    /// <summary>AC68(i): an added field is drift too — additive, so it is reported as a warning.</summary>
    [Fact]
    public void AC68i_ExtraKey_IsDriftWithThePathNamed()
    {
        var contract = ContractFor(DisclosuresDoubleJson);
        var drifted =
            """[{"companyId":"1001","disclosureIndex":"1226255","disclosureClass":"FR","disclosureType":"","title":"Finansal Rapor","acceptedDataFileTypes":["pdf"],"subReportIds":["1"],"publishedAt":"2026-10-18"}]""";

        var result = Evaluate(contract, drifted);

        Assert.Equal(ProbeOutcomeKind.Drift, result.Outcome);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("$[].publishedAt", finding.Path);
        Assert.Equal(DriftKind.ExtraKey, finding.Kind);
        Assert.Equal(DriftSeverity.Warning, finding.Severity);
    }

    /// <summary>AC68(i): a type change is drift — a string that became a number breaks parsing.</summary>
    [Fact]
    public void AC68i_TypeChanged_StringToNumber_IsDrift()
    {
        var contract = ContractFor(DisclosuresDoubleJson);
        var drifted =
            """[{"companyId":"1001","disclosureIndex":1226255,"disclosureClass":"FR","disclosureType":"","title":"Finansal Rapor","acceptedDataFileTypes":["pdf"],"subReportIds":["1"]}]""";

        var result = Evaluate(contract, drifted);

        var finding = Assert.Single(result.Findings);
        Assert.Equal("$[].disclosureIndex", finding.Path);
        Assert.Equal(DriftKind.TypeChanged, finding.Kind);
        Assert.Equal("string", finding.Expected);
        Assert.Equal("number", finding.Observed);
    }

    /// <summary>
    /// AC68(i): drift inside an array-of-objects field is named at the element path — the
    /// nested case, where a row-level field changed kind.
    /// </summary>
    [Fact]
    public void AC68i_NestedArrayOfObjectsFieldDrift_IsNamed()
    {
        var contract = ContractFor(PricesDoubleJson);
        var drifted =
            """{"ok":true,"errorCode":null,"errorDescription":null,"transactionId":"23c0bb08b7f14485b4a7dff0fed2727a","value":[{"HGDG_HS_KODU":"ASELS","HGDG_TARIH":"06-10-2026","HGDG_KAPANIS":"376.0000","HGDG_MIN":364.25,"HGDG_MAX":379.0,"HGDG_HACIM":13239396512.000000}]}""";

        var result = Evaluate(contract, drifted);

        Assert.Equal(ProbeOutcomeKind.Drift, result.Outcome);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("$.value[].HGDG_KAPANIS", finding.Path);
        Assert.Equal(DriftKind.TypeChanged, finding.Kind);
        Assert.Equal("number", finding.Expected);
        Assert.Equal("string", finding.Observed);
    }

    /// <summary>
    /// AC68(i): a null where the double was never null is drift — a value-varying field that
    /// starts vanishing is a shape change, not a data change.
    /// </summary>
    [Fact]
    public void AC68i_NullabilityChanged_IsDrift()
    {
        var contract = ContractFor(PricesDoubleJson);
        var drifted =
            """{"ok":true,"errorCode":null,"errorDescription":null,"transactionId":"23c0bb08b7f14485b4a7dff0fed2727a","value":[{"HGDG_HS_KODU":"ASELS","HGDG_TARIH":"06-10-2026","HGDG_KAPANIS":null,"HGDG_MIN":364.25,"HGDG_MAX":379.0,"HGDG_HACIM":13239396512.000000}]}""";

        var result = Evaluate(contract, drifted);

        var finding = Assert.Single(result.Findings);
        Assert.Equal("$.value[].HGDG_KAPANIS", finding.Path);
        Assert.Equal(DriftKind.NullabilityChanged, finding.Kind);
        Assert.Equal(DriftSeverity.Warning, finding.Severity);
    }

    /// <summary>
    /// AC68(i), incident #1: the İş Yatırım `value: []` answer for a live session
    /// (2026-10-23, run 218 — every symbol honestly skipped while the chart feed carried the
    /// session). A well-formed envelope with an empty collection is <b>not</b> clean here: the
    /// element shape is unverifiable, and the report says exactly which collection was empty.
    /// </summary>
    [Fact]
    public void AC68i_EmptyCollection_IsNotClean_ItNamesTheUnverifiableShape()
    {
        var contract = ContractFor(PricesDoubleJson);
        var emptyEnvelope =
            """{"ok":true,"errorCode":null,"errorDescription":null,"transactionId":"23c0bb08b7f14485b4a7dff0fed2727a","value":[]}""";

        var result = Evaluate(contract, emptyEnvelope);

        Assert.Equal(ProbeOutcomeKind.Drift, result.Outcome);
        Assert.NotEqual(ProbeOutcomeKind.Clean, result.Outcome);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("$.value[]", finding.Path);
        Assert.Equal(DriftKind.ElementShapeMissing, finding.Kind);
        Assert.Equal("object", finding.Expected);
        Assert.Equal(DriftSeverity.Breaking, finding.Severity);
    }

    /// <summary>
    /// AC68(i), incident #2: the KAP VYK gateway's stale-cursor answer — HTTP 400
    /// `{"code":"ER005","message":"Bildirim bulunamadı."}` for indices that no longer exist
    /// (2026-10-23, six fail-stopped EOD chains until TKT-foundation-063). The doubles answer
    /// the requested indices normally, so only a shape probe sees this: the status is named,
    /// and the error envelope's own shape is reported.
    /// </summary>
    [Fact]
    public void AC68i_StatusChanged_NamesTheErrorEnvelope()
    {
        var contract = ContractFor(DisclosuresDoubleJson);
        const string ErrorEnvelope = """{"code":"ER005","message":"Bildirim bulunamadı."}""";

        var result = Evaluate(contract, ErrorEnvelope, statusCode: 400);

        Assert.Equal(ProbeOutcomeKind.Drift, result.Outcome);
        var status = Assert.Single(result.Findings, f => f.Kind == DriftKind.StatusChanged);
        Assert.Equal(ShapeComparer.StatusPath, status.Path);
        Assert.Equal("200", status.Expected);
        Assert.Equal("400", status.Observed);

        // The body is an object where the double serves an array, and its keys are named.
        Assert.Contains(result.Findings, f => f.Path == "$" && f.Kind == DriftKind.TypeChanged);
        Assert.Contains(result.Findings, f => f.Path == "$.code");
        Assert.Contains(result.Findings, f => f.Path == "$.message");
    }

    /// <summary>
    /// AC68(i)/(iii): a data-derived container's unseen keys are exempt only where the contract
    /// says so (EVDS series codes / transposed dates) — and no exemption elsewhere.
    /// </summary>
    [Fact]
    public void AC68i_DataDerivedKeys_AreExemptOnlyUnderADeclaredContainer()
    {
        const string EvdsDouble = """{"totalCount":1,"items":[{"Tarih":"15-10-2026","TP_DK_USD_S_YTL":"49.39"}],"seriesNames":{"TP_DK_USD_S_YTL":"USD/TRY"}}""";
        const string EvdsNextDay = """{"totalCount":1,"items":[{"Tarih":"16-10-2026","TP_DK_USD_S_YTL":"49.42","TP_DK_EUR_S_YTL":"55.41"}],"seriesNames":{"TP_DK_USD_S_YTL":"USD/TRY","TP_DK_EUR_S_YTL":"EUR/TRY"}}""";

        var strict = Evaluate(ContractFor(EvdsDouble), EvdsNextDay);
        Assert.Equal(ProbeOutcomeKind.Drift, strict.Outcome);

        var permissive = Evaluate(ContractFor(EvdsDouble, allowExtraUnder: ["$.items[]", "$.seriesNames"]), EvdsNextDay);
        Assert.Equal(ProbeOutcomeKind.Clean, permissive.Outcome);
    }

    // ---- (i)/(iii) unreachable is never clean ----------------------------------------

    /// <summary>AC68(i)/(iii): an empty body is "unreachable", never "no drift".</summary>
    [Fact]
    public void AC68iii_EmptyBody_IsUnreachable_NotClean()
    {
        var contract = ContractFor(PricesDoubleJson);

        var result = Evaluate(contract, "   ");

        Assert.Equal(ProbeOutcomeKind.Unreachable, result.Outcome);
        Assert.Empty(result.Findings);
        Assert.Contains("empty", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>AC68(iii): a non-JSON body (a proxy page, a truncated read) is unreachable.</summary>
    [Fact]
    public void AC68iii_NonJsonBody_IsUnreachable_NotClean()
    {
        var contract = ContractFor(PricesDoubleJson);

        var result = Evaluate(contract, "<html><body>gateway error</body></html>");

        Assert.Equal(ProbeOutcomeKind.Unreachable, result.Outcome);
        Assert.Empty(result.Findings);
    }

    /// <summary>AC68(iii): a transport failure is unreachable — the shape claim is simply not made.</summary>
    [Fact]
    public void AC68iii_TransportFailure_IsUnreachable_NotClean()
    {
        var result = ProbeResult.Unreachable(
            SourceProbeRegistry.IsYatirimPrices,
            "https://www.isyatirim.com.tr/path",
            "transport failure: connection refused",
            Now);

        Assert.Equal(ProbeOutcomeKind.Unreachable, result.Outcome);
        Assert.Empty(result.ObservedShape);
    }

    // ---- (iii) redaction and no retained bodies --------------------------------------

    /// <summary>AC68(iii): a secret query value never survives into the record.</summary>
    [Fact]
    public void AC68iii_Redactor_MasksSecretQueryParametersAndUserInfo()
    {
        const string Secret = "s3cr3t-evds-key";
        var url = $"https://evds3.tcmb.gov.tr/igmevdsms-dis/fe?series=TP.DK.USD.S.YTL&key={Secret}";

        var redacted = Redactor.RedactUrl(url, ["key"]);

        Assert.DoesNotContain(Secret, redacted, StringComparison.Ordinal);
        Assert.Contains("key=<redacted>", redacted, StringComparison.Ordinal);
        Assert.Contains("series=TP.DK.USD.S.YTL", redacted, StringComparison.Ordinal);
        Assert.Equal(
            "https://<redacted>@host.example/x",
            Redactor.RedactUrl("https://user:pass@host.example/x", []));
    }

    /// <summary>AC68(iii): a secret echoed by an exception message is scrubbed.</summary>
    [Fact]
    public void AC68iii_Redactor_ScrubsSecretValuesFromFreeText()
    {
        const string Secret = "abc123secret";
        var message = $"GET https://host/api?key={Secret} failed";

        var scrubbed = Redactor.RedactSecrets(message, [Secret]);

        Assert.DoesNotContain(Secret, scrubbed, StringComparison.Ordinal);
        Assert.Contains(Redactor.Mask, scrubbed, StringComparison.Ordinal);
        Assert.True(Redactor.IsSecretHeader("Authorization"));
        Assert.True(Redactor.IsSecretHeader("x-api-key"));
        Assert.False(Redactor.IsSecretHeader("Accept"));
    }

    /// <summary>
    /// AC68(iii): the report carries paths and kinds only — no body values, no transaction id,
    /// no secret. This is what makes a report safe to attach to a ticket.
    /// </summary>
    [Fact]
    public void AC68iii_Report_RetainsNoBodyValues()
    {
        var contract = ContractFor(PricesDoubleJson);
        var result = Evaluate(contract, PricesDoubleJson);
        var report = new DriftReport("live", Now, [result]);

        var json = report.ToJson();

        Assert.Contains("$.value[].HGDG_KAPANIS", json, StringComparison.Ordinal);
        Assert.Contains("number", json, StringComparison.Ordinal);
        Assert.DoesNotContain("376", json, StringComparison.Ordinal);
        Assert.DoesNotContain("23c0bb08b7f14485b4a7dff0fed2727a", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ASELS", json, StringComparison.Ordinal);
    }

    // ---- (iv) coverage set and politeness --------------------------------------------

    /// <summary>
    /// AC68(iv): the coverage set is the ticket's source list, and every probe honours the
    /// product's pacing posture — İş Yatırım ≥ 3 s, KAP ≥ 12 s, with a low declaration clamped up.
    /// </summary>
    [Fact]
    public void AC68iv_Registry_PinsTheCoverageSetAndPacingFloors()
    {
        var keys = SourceProbeRegistry.All.Select(p => p.Key).ToArray();
        Assert.Equal(
            new[]
            {
                SourceProbeRegistry.IsYatirimPrices,
                SourceProbeRegistry.YahooChart,
                SourceProbeRegistry.KapDisclosures,
                SourceProbeRegistry.KapDisclosureDetail,
                SourceProbeRegistry.KapMembers,
                SourceProbeRegistry.KapMemberSecurities,
                SourceProbeRegistry.EvdsMacro,
                SourceProbeRegistry.TefasFunds,
            },
            keys);

        Assert.All(
            SourceProbeRegistry.All.Where(p => p.SourceRef == "isyatirim"),
            p => Assert.True(p.EffectivePacingMs >= ProbePolicy.IsYatirimIntervalFloorMs, p.Key));
        Assert.All(
            SourceProbeRegistry.All.Where(p => p.SourceRef == "kap"),
            p => Assert.True(p.EffectivePacingMs >= ProbePolicy.KapIntervalFloorMs, p.Key));
        Assert.Equal(ProbePolicy.IsYatirimIntervalFloorMs, ProbePolicy.EffectiveInterval("isyatirim", 500));
        Assert.Equal(ProbePolicy.KapIntervalFloorMs, ProbePolicy.EffectiveInterval("kap", 1_000));
    }

    /// <summary>
    /// AC68(iv): no live probing inside a scheduled chain window, and live mode is opt-in.
    /// </summary>
    [Fact]
    public void AC68iv_LiveMode_IsOptInAndRefusedInChainWindows()
    {
        Assert.Throws<LiveProbeRefusedException>(() => ProbePolicy.EnsureLiveRequested(false));
        ProbePolicy.EnsureLiveRequested(true);

        var eod = new DateTimeOffset(2026, 10, 24, 17, 45, 0, TimeSpan.Zero);
        var morning = new DateTimeOffset(2026, 10, 24, 5, 30, 0, TimeSpan.Zero);
        var midday = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

        Assert.True(ProbePolicy.IsQuietWindow(eod));
        Assert.True(ProbePolicy.IsQuietWindow(morning));
        Assert.False(ProbePolicy.IsQuietWindow(midday));
        Assert.Throws<LiveProbeRefusedException>(() => ProbePolicy.EnsureNotInChainWindow(eod, force: false));
        ProbePolicy.EnsureNotInChainWindow(midday, force: false);
        ProbePolicy.EnsureNotInChainWindow(eod, force: true);
    }

    // ---- (ii) never a CI gate --------------------------------------------------------

    /// <summary>
    /// AC68(ii): the probe harness is <b>not</b> wired into any CI gate. No workflow, compose
    /// file or CI image invokes it; a live source can be slow, rate-limited or down, so gating a
    /// PR on it would only make CI flaky (01 §6.1). The run is an operator command.
    /// </summary>
    [Fact]
    public void AC68ii_ProbeHarness_IsNotWiredIntoCi()
    {
        var root = RepoRoot();
        var ciFiles = Directory
            .EnumerateFiles(Path.Combine(root.FullName, ".github", "workflows"), "*.yml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(root.FullName, "compose.*.yml", SearchOption.TopDirectoryOnly))
            .Concat(Directory.EnumerateFiles(root.FullName, "Dockerfile*", SearchOption.TopDirectoryOnly))
            .ToArray();

        Assert.NotEmpty(ciFiles);
        foreach (var file in ciFiles)
        {
            var text = File.ReadAllText(file);
            foreach (var marker in new[] { "source-probes", "SourceProbes", "source_contract_probe" })
            {
                Assert.False(
                    text.Contains(marker, StringComparison.OrdinalIgnoreCase),
                    $"{Path.GetFileName(file)} references '{marker}': the probe harness must never gate CI.");
            }
        }
    }

    /// <summary>
    /// AC68(iv): every registered probe has a checked-in contract that parses and pins a double
    /// — the coverage set cannot silently lose a source.
    /// </summary>
    [Fact]
    public void AC68iv_EveryRegisteredProbeHasACheckedInContract()
    {
        var root = RepoRoot();
        var directory = Path.Combine(root.FullName, "tools", "source-probes", "contracts");

        foreach (var probe in SourceProbeRegistry.All)
        {
            var file = Path.Combine(directory, probe.ContractFileName);
            Assert.True(File.Exists(file), $"missing contract for {probe.Key}: {file}");

            var contract = ProbeContractJson.Read(File.ReadAllText(file));
            Assert.Equal(probe.Key, contract.Source);
            Assert.Equal(probe.DoubleCatalogEntry, contract.Double.CatalogEntry);
            Assert.False(contract.Shape.IsEmpty, $"{probe.Key} carries no expected shape");
            Assert.False(string.IsNullOrWhiteSpace(contract.DerivedAt), $"{probe.Key} does not date its derivation");
        }
    }

    // ---- helpers ---------------------------------------------------------------------

    private static ProbeContract ContractFor(
        string body,
        int expectedStatus = 200,
        IReadOnlyList<string>? allowExtraUnder = null) =>
        new()
        {
            Source = "test-source",
            Endpoint = "https://example.test/resource",
            Double = new DoubleReference(
                "test-double",
                "fixtures/test.cs",
                DoubleProvenance.Constructed,
                "unit-test double"),
            DerivedAt = "2026-10-24",
            ExpectedStatus = expectedStatus,
            AllowExtraUnder = allowExtraUnder ?? [],
            Shape = JsonShapeExtractor.Extract(body),
        };

    private static ProbeResult Evaluate(ProbeContract contract, string body, int? statusCode = null) =>
        ProbeRunner.Evaluate(
            contract.Source,
            contract.Endpoint,
            contract,
            body,
            ProbeMode.SavedBody,
            statusCode,
            "unit-test comparison",
            Now);

    private static DirectoryInfo RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Degerli.sln")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, "the repository root (Degerli.sln) was not found above the test output");
        return directory!;
    }
}

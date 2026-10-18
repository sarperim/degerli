using System.Globalization;
using System.Text.Json;

namespace Degerli.Ingestion.Statements;

/// <summary>
/// Decodes the <b>real</b> KAP VYK disclosure-detail envelope (01 v1.5 §10.9.11, FR-MDF-002):
/// the <c>DisclosureByIdResponse</c> returned by <c>GET /disclosureDetail/{disclosureIndex}
/// ?fileType=data</c>. It joins the §10.9.5 fact-envelope family as a first-class production
/// shape: fiscal coordinates come from the detail fields (<c>year</c>, <c>period</c>) and each
/// sub-report's structured <c>presentation</c> tree collapses onto the canonical chart of
/// accounts (<c>02</c> §6.1) through the shared
/// <see cref="KapXbrlStatementDecoder.ConceptMap"/> and <see cref="StatementMapper"/> — the
/// period/statement/version grouping and the duplicate-concept rule (equal collapses, unequal
/// quarantines) carry over verbatim, and provenance stays <c>kap</c>.
/// <para>
/// <b>Live-verified shape (2026-10-18).</b> The gateway does <b>not</b> return the OpenAPI's
/// declared <c>flatData</c> for <c>fileType=data</c> (that member is absent from the real
/// response, and <c>fileType=html</c> returns <c>htmlMessages</c>). The structured data is the
/// XBRL-style <c>presentation</c> array: one entry per sub-report (<c>content.ContextList</c>
/// binds context ids — keyed <c>CURR</c>/<c>PREV</c>/<c>CURR3</c>/<c>PREV3</c> — to periods, and
/// <c>content.ReportItem</c> is a tree of report items whose leaves carry a bare IFRS taxonomy
/// <c>name</c> and a <c>Values.Value[]</c> list of <c>{contextId, currency, rounding, value}</c>).
/// The decoder selects the <c>CURR</c> context's value per leaf. This shape was pinned by a
/// live capture (<c>RealSourceCatalog</c>'s bounded <c>kap-vyk-disclosure-detail</c> entry); the
/// CI doubles are shape-faithful bodies of the same shape.
/// </para>
/// </summary>
public static class VykStatementDecoder
{
    /// <summary>Default unit scale when the detail declares none.</summary>
    public const string DefaultUnitScale = "units";

    /// <summary>Default reporting currency when the detail declares none (KAP filings are TRY-denominated).</summary>
    public const string DefaultCurrency = "TRY";

    /// <summary>Default version when a sub-report declares none.</summary>
    public const string DefaultVersion = "as_reported";

    /// <summary>The context key whose value is the current reporting period.</summary>
    public const string CurrentContextKey = "CURR";

    /// <summary>
    /// True when the document is the real VYK disclosure-detail envelope: an object carrying a
    /// <c>presentation</c> array (the canonical/XBRL doubles carry <c>statements</c>/<c>facts</c>
    /// instead).
    /// </summary>
    public static bool IsEnvelope(JsonDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.RootElement;
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("presentation", out var presentation)
            && presentation.ValueKind == JsonValueKind.Array;
    }

    /// <summary>
    /// Decodes a VYK disclosure detail into the canonical <see cref="StatementsPayload"/> the
    /// store persists. <paramref name="symbol"/> is the requested (bound) BIST symbol, so the
    /// facts are written under the instrument the loop asked for (the identity guard has
    /// already discarded bodies naming another company).
    /// </summary>
    public static StatementsPayload Decode(JsonDocument document, string symbol)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        var root = document.RootElement;
        var fiscalYear = ReadYear(root);
        var (periodType, periodEndDate) = ResolvePeriod(root, fiscalYear);
        var currency = ReadEnvelopeCurrency(root) ?? DefaultCurrency;

        var groups = new List<Group>();
        var byKey = new Dictionary<string, Group>(StringComparer.Ordinal);

        if (root.TryGetProperty("presentation", out var presentation) && presentation.ValueKind == JsonValueKind.Array)
        {
            foreach (var subReport in presentation.EnumerateArray())
            {
                if (subReport.ValueKind != JsonValueKind.Object
                    || !subReport.TryGetProperty("content", out var content)
                    || content.ValueKind != JsonValueKind.Object
                    || !content.TryGetProperty("ReportItem", out var reportItem)
                    || reportItem.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var currentContextId = ReadCurrentContextId(content);
                if (currentContextId is null)
                {
                    continue;
                }

                var statementType = InferStatementType(ReadOptionalString(reportItem, "name"));
                var key = string.Join('|', periodType, periodEndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), statementType);
                if (!byKey.TryGetValue(key, out var group))
                {
                    group = new Group(periodType, periodEndDate, fiscalYear, statementType, DefaultVersion);
                    byKey[key] = group;
                    groups.Add(group);
                }

                WalkReportItems(reportItem, currentContextId, group);
            }
        }

        return new StatementsPayload(
            KapXbrlStatementDecoder.SourceRef,
            symbol,
            DefaultUnitScale,
            currency,
            groups.Select(g => g.ToFact()).ToList());
    }

    /// <summary>
    /// Recursively reads a report-item tree, collapsing every non-abstract leaf's <c>CURR</c>
    /// value onto its canonical item code. A leaf is a node carrying a <c>Values.Value[]</c>;
    /// children live under a nested <c>ReportItem</c> (object or array).
    /// </summary>
    private static void WalkReportItems(JsonElement node, string currentContextId, Group group)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var concept = ReadOptionalString(node, "name");
        if (!string.IsNullOrWhiteSpace(concept)
            && node.TryGetProperty("Values", out var values)
            && values.ValueKind == JsonValueKind.Object
            && TryReadCurrentValue(values, currentContextId, out var value))
        {
            var code = KapXbrlStatementDecoder.ConceptMap.TryGetValue(concept, out var mapped)
                ? mapped
                : $"OTHER_{concept}";
            group.Add(code, value);
        }

        if (!node.TryGetProperty("ReportItem", out var children))
        {
            return;
        }

        switch (children.ValueKind)
        {
            case JsonValueKind.Object:
                WalkReportItems(children, currentContextId, group);
                break;
            case JsonValueKind.Array:
                foreach (var child in children.EnumerateArray())
                {
                    WalkReportItems(child, currentContextId, group);
                }

                break;
        }
    }

    /// <summary>The context id whose key is <c>CURR</c> (the current reporting period).</summary>
    private static string? ReadCurrentContextId(JsonElement content)
    {
        if (!content.TryGetProperty("ContextList", out var contextList)
            || contextList.ValueKind != JsonValueKind.Object
            || !contextList.TryGetProperty("Context", out var contexts)
            || contexts.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var context in contexts.EnumerateArray())
        {
            if (context.ValueKind == JsonValueKind.Object
                && string.Equals(ReadOptionalString(context, "key"), CurrentContextKey, StringComparison.Ordinal))
            {
                return ReadOptionalString(context, "id");
            }
        }

        return null;
    }

    private static bool TryReadCurrentValue(JsonElement values, string currentContextId, out decimal value)
    {
        value = 0m;
        if (!values.TryGetProperty("Value", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !string.Equals(ReadOptionalString(entry, "contextId"), currentContextId, StringComparison.Ordinal)
                || !entry.TryGetProperty("value", out var element))
            {
                continue;
            }

            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    var text = element.GetString();
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return false;
                    }

                    value = KapXbrlStatementDecoder.ParseTurkishNumber(text);
                    return true;
                case JsonValueKind.Number:
                    value = element.GetDecimal();
                    return true;
                default:
                    return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Maps a sub-report's root report-item name to the canonical statement-type code:
    /// balance sheet <c>BS</c>, income statement <c>IS</c>, cash flow <c>CF</c>, changes in
    /// equity <c>EQ</c>; anything unrecognised falls back to <c>IS</c>.
    /// </summary>
    private static string InferStatementType(string? rootName)
    {
        if (string.IsNullOrEmpty(rootName))
        {
            return "IS";
        }

        if (rootName.Contains("BalanceSheet", StringComparison.Ordinal))
        {
            return "BS";
        }

        if (rootName.Contains("CashFlows", StringComparison.Ordinal))
        {
            return "CF";
        }

        if (rootName.Contains("ChangesInEquity", StringComparison.Ordinal))
        {
            return "EQ";
        }

        return "IS";
    }

    /// <summary>The first declared value currency in the detail (all values share it), or null.</summary>
    private static string? ReadEnvelopeCurrency(JsonElement root)
    {
        if (!root.TryGetProperty("presentation", out var presentation) || presentation.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var subReport in presentation.EnumerateArray())
        {
            if (!subReport.TryGetProperty("content", out var content) || !content.TryGetProperty("ReportItem", out var reportItem))
            {
                continue;
            }

            var currency = FindCurrency(reportItem);
            if (currency is not null)
            {
                return currency;
            }
        }

        return null;
    }

    private static string? FindCurrency(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (node.TryGetProperty("Values", out var values)
            && values.ValueKind == JsonValueKind.Object
            && values.TryGetProperty("Value", out var entries)
            && entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in entries.EnumerateArray())
            {
                var currency = ReadOptionalString(entry, "currency");
                if (!string.IsNullOrWhiteSpace(currency))
                {
                    return currency;
                }
            }
        }

        if (node.TryGetProperty("ReportItem", out var children))
        {
            if (children.ValueKind == JsonValueKind.Object)
            {
                var found = FindCurrency(children);
                if (found is not null)
                {
                    return found;
                }
            }
            else if (children.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in children.EnumerateArray())
                {
                    var found = FindCurrency(child);
                    if (found is not null)
                    {
                        return found;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The fiscal year from the detail's <c>year</c> field (a string in the VYK shape), or
    /// <c>null</c> when absent/unparseable (the group simply carries no fiscal year).
    /// </summary>
    private static int? ReadYear(JsonElement root) =>
        root.TryGetProperty("year", out var year) && year.ValueKind == JsonValueKind.String
            && int.TryParse(year.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;

    /// <summary>
    /// Resolves (periodType, periodEndDate) from the detail's <c>period</c> object
    /// (<c>{tr, en}</c>). KAP terms enumerate months; a 3/6/9-month term is a quarter, anything
    /// else (Yıllık/Annual) is a full year. A missing/unparseable <c>year</c> makes the period
    /// unresolvable, which is a malformed body (<see cref="JsonException"/> the adapter
    /// surfaces as <c>UNPARSEABLE_PAYLOAD</c>).
    /// </summary>
    private static (string PeriodType, DateOnly PeriodEndDate) ResolvePeriod(JsonElement root, int? year)
    {
        if (year is null)
        {
            throw new JsonException("VYK disclosure detail is missing a parseable 'year'.");
        }

        var term = ReadPeriodTerm(root) ?? string.Empty;
        var months = ExtractMonths(term);
        return months switch
        {
            3 => ("Q", new DateOnly(year.Value, 3, 31)),
            6 => ("Q", new DateOnly(year.Value, 6, 30)),
            9 => ("Q", new DateOnly(year.Value, 9, 30)),
            _ => ("FY", new DateOnly(year.Value, 12, 31)),
        };
    }

    private static string? ReadPeriodTerm(JsonElement root)
    {
        if (!root.TryGetProperty("period", out var period) || period.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadOptionalString(period, "tr") ?? ReadOptionalString(period, "en");
    }

    /// <summary>Extracts the month count from a KAP period term ("9 Aylık" → 9, "Yıllık" → 12).</summary>
    private static int ExtractMonths(string term)
    {
        if (term.Contains("Yıllık", StringComparison.OrdinalIgnoreCase)
            || term.Contains("Annual", StringComparison.OrdinalIgnoreCase)
            || term.Contains("Year", StringComparison.OrdinalIgnoreCase))
        {
            return 12;
        }

        foreach (var ch in term)
        {
            if (char.IsDigit(ch))
            {
                return ch - '0';
            }
        }

        return 12;
    }

    private static string? ReadOptionalString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    /// <summary>
    /// One statement group under (periodType, periodEndDate, statementType, version).
    /// Accumulates canonical lines with the §10.9.5 duplicate-concept rule: an equal re-report
    /// collapses idempotently, an unequal one is flagged as a conflict (the store quarantines
    /// it as <c>CONFLICTING_VALUE</c>, never a silent latest-wins).
    /// </summary>
    private sealed class Group
    {
        public Group(string periodType, DateOnly periodEndDate, int? fiscalYear, string statementType, string version)
        {
            PeriodType = periodType;
            PeriodEndDate = periodEndDate;
            FiscalYear = fiscalYear;
            StatementType = statementType;
            Version = version;
        }

        public string PeriodType { get; }

        public DateOnly PeriodEndDate { get; }

        public int? FiscalYear { get; }

        public string StatementType { get; }

        public string Version { get; }

        public Dictionary<string, decimal> Lines { get; } = new(StringComparer.Ordinal);

        public List<string> Conflicts { get; } = [];

        public void Add(string code, decimal value)
        {
            if (Lines.TryGetValue(code, out var existing))
            {
                if (existing != value && !Conflicts.Contains(code, StringComparer.Ordinal))
                {
                    Conflicts.Add(code);
                }

                return;
            }

            Lines[code] = value;
        }

        public StatementFact ToFact() => new(
            PeriodType,
            PeriodEndDate,
            FiscalYear,
            StatementType,
            Version,
            RestatementDate: null,
            PublishedAt: null,
            StatementMapper.Map(Lines),
            Conflicts.Count == 0 ? null : Conflicts.ToList());
    }
}

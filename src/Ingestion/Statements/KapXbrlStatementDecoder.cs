using System.Globalization;
using System.Text.Json;

namespace Degerli.Ingestion.Statements;

/// <summary>
/// Decodes the <b>real</b> KAP XBRL financial-statement fact envelope (01 v1.5 §10.9.5,
/// FR-MDF-002): a per-company fact list where every fact carries a <c>concept</c> (XBRL
/// tag), a <c>value</c> (a Turkish-locale number string) and its period/statement/version
/// coordinates (<c>fiscalYear</c>, <c>periodType</c>, <c>periodEndDate</c>,
/// <c>statementType</c>, <c>version</c>, <c>publishedAt</c>), plus envelope-level
/// <c>unitScale</c>/<c>currency</c> that feed the v1.4 normalization (<c>02</c> §1.8/§3.1).
///
/// <para>
/// This is the source-translation half of the statements ETL. It (a) collapses the
/// inconsistent IFRS vs Turkish-taxonomy vs KAP tag spellings for the same line onto
/// <b>one</b> canonical chart-of-accounts item code (<c>02</c> §6.1); (b) parses the
/// values <b>culture-free</b>; (c) groups facts by
/// (periodType, periodEndDate, statementType, version) so the store can assign the integer
/// version sequence (<c>02</c> §1.5); and (d) applies the duplicate-concept rule — within
/// one group, the same canonical code appearing twice with <b>equal</b> values collapses
/// idempotently, with <b>unequal</b> values it is a validation event (the store
/// quarantines the fact as <c>CONFLICTING_VALUE</c>, both values retained — never a silent
/// latest-wins; §10.9.5, DR-05 overturned).
/// </para>
/// </summary>
public static class KapXbrlStatementDecoder
{
    /// <summary>The concrete provenance stamped onto every decoded statement (FR-MDF-008).</summary>
    public const string SourceRef = "kap";

    /// <summary>The ISO date format of the envelope's period/statement coordinates.</summary>
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    /// The real KAP XBRL concept → canonical chart-of-accounts item code map
    /// (<c>02</c> §6.1). Inconsistent IFRS / Turkish-taxonomy / KAP spellings for the same
    /// line deliberately share one canonical target (e.g. <c>ifrs-full:Revenue</c> /
    /// <c>tr:Hasilat</c> / <c>kap:NetSatisTutari</c> all → <c>REV</c>). Unmapped concepts
    /// are not dropped — they pass through as <c>OTHER_*</c> lines (recorded, ignored by
    /// metrics; an honest gap).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ConceptMap =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ifrs-full:Revenue"] = "REV",
            ["tr:Hasilat"] = "REV",
            ["kap:NetSatisTutari"] = "REV",
            ["ifrs-full:CostOfSales"] = "COGS",
            ["ifrs-full:GrossProfit"] = "GROSS_PROFIT",
            ["ifrs-full:ProfitLoss"] = "NI",
            ["ifrs-full:Equity"] = "TOTAL_EQUITY",
            ["ifrs-full:NumberOfShares"] = "SHARES_DILUTED",
            ["ifrs-full:CashAndCashEquivalents"] = "CASH",
        };

    /// <summary>
    /// True when the document is the real KAP XBRL fact envelope: an object carrying a
    /// <c>facts</c> array (the canonical double uses <c>statements</c> instead).
    /// </summary>
    public static bool IsEnvelope(JsonDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.RootElement;
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("facts", out var facts)
            && facts.ValueKind == JsonValueKind.Array;
    }

    /// <summary>
    /// Decodes the fact envelope into the canonical <see cref="StatementsPayload"/> the
    /// store persists. The symbol is the envelope's <c>companyCode</c>; the declared
    /// <c>unitScale</c>/<c>currency</c> are carried through for the v1.4 normalization path;
    /// the provenance is <see cref="SourceRef"/>.
    /// </summary>
    public static StatementsPayload Decode(JsonDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.RootElement;
        var symbol = ReadRequiredString(root, "companyCode");
        var unitScale = ReadOptionalString(root, "unitScale");
        var currency = ReadOptionalString(root, "currency");
        var facts = root.GetProperty("facts");

        var groups = new List<Group>();
        var byKey = new Dictionary<string, Group>(StringComparer.Ordinal);

        foreach (var fact in facts.EnumerateArray())
        {
            if (fact.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var periodType = ReadRequiredString(fact, "periodType");
            var periodEndDate = DateOnly.ParseExact(
                ReadRequiredString(fact, "periodEndDate"), DateFormat, CultureInfo.InvariantCulture);
            var statementType = ReadRequiredString(fact, "statementType");
            var version = ReadRequiredString(fact, "version");

            var key = string.Join('|', periodType, periodEndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), statementType, version);
            if (!byKey.TryGetValue(key, out var group))
            {
                group = new Group(periodType, periodEndDate, statementType, version, fact);
                byKey[key] = group;
                groups.Add(group);
            }

            var concept = ReadRequiredString(fact, "concept");
            var code = ConceptMap.TryGetValue(concept, out var mapped) ? mapped : $"OTHER_{concept}";
            group.Add(code, ReadValue(fact));
        }

        return new StatementsPayload(
            SourceRef,
            symbol,
            unitScale,
            currency,
            groups.Select(g => g.ToFact()).ToList());
    }

    /// <summary>
    /// Parses a Turkish-formatted number (<c>1.234.567,89</c>): <c>.</c> is the thousands
    /// separator, <c>,</c> the decimal separator. Tolerates surrounding whitespace, a
    /// leading minus (hyphen, en-dash <c>–</c> or minus sign <c>−</c>) or accounting
    /// parentheses, a trailing <c>%</c> and NBSP thousand-groupings. <b>Culture-free</b> —
    /// the host builds under <c>InvariantGlobalization</c>, so the separators are handled
    /// explicitly, never through the ambient culture.
    /// </summary>
    public static decimal ParseTurkishNumber(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var value = text.Trim();
        var negative = false;

        if (value.StartsWith('(') && value.EndsWith(')'))
        {
            negative = true;
            value = value[1..^1].Trim();
        }

        if (value.Length > 0 && IsMinus(value[0]))
        {
            negative = true;
            value = value[1..].Trim();
        }

        if (value.EndsWith('%'))
        {
            value = value[..^1].Trim();
        }

        value = value
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\u00A0", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace(',', '.');

        var parsed = decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        return negative ? -parsed : parsed;
    }

    private static bool IsMinus(char c) =>
        c is '-' or '\u2013' or '\u2014' or '\u2212';

    private static decimal ReadValue(JsonElement fact)
    {
        if (!fact.TryGetProperty("value", out var value))
        {
            throw new JsonException("KAP fact carries no 'value'.");
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => ParseTurkishNumber(value.GetString()!),
            JsonValueKind.Number => value.GetDecimal(),
            _ => throw new JsonException($"KAP fact value has unsupported kind '{value.ValueKind}'."),
        };
    }

    private static string ReadRequiredString(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        throw new JsonException($"KAP envelope fact is missing required string '{name}'.");
    }

    private static string? ReadOptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateOnly? ReadOptionalDate(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? DateOnly.ParseExact(value.GetString()!, DateFormat, CultureInfo.InvariantCulture)
            : null;

    /// <summary>
    /// One statement group under (periodType, periodEndDate, statementType, version).
    /// Accumulates the canonical lines as its facts are read: the same code appearing with
    /// an <b>equal</b> value is an idempotent collapse; with an <b>unequal</b> value the
    /// code is flagged as conflicting (never overwritten — §10.9.5).
    /// </summary>
    private sealed class Group
    {
        private readonly JsonElement _firstFact;

        public Group(string periodType, DateOnly periodEndDate, string statementType, string version, JsonElement firstFact)
        {
            PeriodType = periodType;
            PeriodEndDate = periodEndDate;
            StatementType = statementType;
            Version = version;
            _firstFact = firstFact;
        }

        public string PeriodType { get; }

        public DateOnly PeriodEndDate { get; }

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

        public StatementFact ToFact()
        {
            var fiscalYear = _firstFact.TryGetProperty("fiscalYear", out var fy) && fy.ValueKind == JsonValueKind.Number
                ? fy.GetInt32()
                : (int?)null;

            return new StatementFact(
                PeriodType,
                PeriodEndDate,
                fiscalYear,
                StatementType,
                Version,
                ReadOptionalDate(_firstFact, "restatementDate"),
                ReadOptionalDate(_firstFact, "publishedAt"),
                StatementMapper.Map(Lines),
                Conflicts.Count == 0 ? null : Conflicts.ToList());
        }
    }
}

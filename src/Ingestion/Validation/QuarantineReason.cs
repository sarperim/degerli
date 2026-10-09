namespace Degerli.Ingestion.Validation;

/// <summary>
/// The validation-failure class catalog (FU §2; FR-MDF-012). Every fact refused by
/// ingestion carries exactly one of these stable reason codes onto its
/// <c>quarantined_facts</c> row, so the review queue and the alert can name why a fact
/// was rejected and a future re-ingestion can target the same class.
/// </summary>
public static class QuarantineReason
{
    /// <summary>A price value below zero is not a real market price.</summary>
    public const string NegativePrice = "NEGATIVE_PRICE";

    /// <summary>The fact carries no <c>source_ref</c> (FR-MDF-008, NFR-MDF-004).</summary>
    public const string MissingProvenance = "MISSING_PROVENANCE";

    /// <summary>A required field is absent or not of the expected type.</summary>
    public const string SchemaMismatch = "SCHEMA_MISMATCH";

    /// <summary>The source body could not be parsed at all.</summary>
    public const string UnparseablePayload = "UNPARSEABLE_PAYLOAD";

    /// <summary>An instrument arrived with no sector classification (UC-MDF-003 alt a).</summary>
    public const string MissingClassification = "MISSING_CLASSIFICATION";

    /// <summary>A valid fact conflicts with an already-stored fact for the same key
    /// (Q1, 2026-10-06); the stored fact is kept, never overwritten.</summary>
    public const string ConflictingValue = "CONFLICTING_VALUE";

    /// <summary>Every reason code, in catalog order (FU §2).</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        NegativePrice,
        MissingProvenance,
        SchemaMismatch,
        UnparseablePayload,
        MissingClassification,
        ConflictingValue,
    ];
}

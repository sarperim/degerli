namespace Degerli.Ingestion.KapPublic;

/// <summary>
/// The KAP <b>public site's</b> opaque company oid — one of the three identifier spaces the
/// lane touches (01 v1.5.54 §10.9.19, TKT-foundation-080). ASELS answers
/// <c>4028e4a1413b7ef401413bc2251e0047</c> from <c>POST /tr/api/search/combined</c>; that value
/// is <b>not</b> the VYK gateway's numeric <c>companyId</c> (ASELS <c>866</c>) and <b>not</b> our
/// <see cref="Persistence.Entities.Instrument.Id"/>.
/// <para>
/// The type exists so the spaces cannot be swapped by accident: it has <b>no</b> conversion to or
/// from <see cref="VykCompanyId"/>, and its only constructor path is a shape-checking parse (32
/// lowercase hex characters). A numeric companyId is not a valid <see cref="PublicCompanyOid"/>
/// and an oid is not a valid <see cref="VykCompanyId"/> — by construction, not by convention.
/// </para>
/// </summary>
public readonly record struct PublicCompanyOid
{
    /// <summary>The exact length of every public-site company oid the site serves.</summary>
    public const int Length = 32;

    private PublicCompanyOid(string value) => Value = value;

    /// <summary>The oid as the site spells it (lowercase hex).</summary>
    public string Value { get; }

    /// <summary>
    /// Parses a public-site company oid, rejecting anything that is not 32 hex characters. Uppercase
    /// input is normalized to the site's lowercase spelling so one oid has exactly one stored form.
    /// </summary>
    /// <exception cref="FormatException">The value is not a public-site company oid shape.</exception>
    public static PublicCompanyOid Parse(string? value)
    {
        if (!TryParse(value, out var oid))
        {
            throw new FormatException(
                $"'{value}' is not a KAP public-site company oid (expected {Length} hex characters). " +
                "A VYK numeric companyId is a different identifier space and is never accepted here.");
        }

        return oid;
    }

    /// <summary>Shape check + parse (see <see cref="Parse"/>).</summary>
    public static bool TryParse(string? value, out PublicCompanyOid oid)
    {
        oid = default;
        if (value is null || value.Length != Length)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        oid = new PublicCompanyOid(value.ToLowerInvariant());
        return true;
    }

    /// <summary>The stored spelling of the oid.</summary>
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>
/// The <b>VYK gateway's</b> numeric <c>companyId</c> (01 v1.5 §10.9.11) — a different identifier
/// space from both our instrument id and the public site's oid. It is recorded beside a mapping
/// only as its own space's fact (ASELS <c>866</c>), never as a translation of the public oid: a
/// <see cref="VykCompanyId"/> and a <see cref="PublicCompanyOid"/> are distinct types with no
/// conversion between them, and each has its own stored column and its own provenance.
/// </summary>
public readonly record struct VykCompanyId
{
    /// <summary>The widest numeric companyId the gateway is known to use.</summary>
    public const int MaxLength = 12;

    private VykCompanyId(string value) => Value = value;

    /// <summary>The id as the gateway spells it (decimal digits).</summary>
    public string Value { get; }

    /// <summary>
    /// Parses a VYK numeric <c>companyId</c>, rejecting anything that is not decimal digits (a
    /// public-site oid is not one).
    /// </summary>
    /// <exception cref="FormatException">The value is not a numeric companyId.</exception>
    public static VykCompanyId Parse(string? value)
    {
        if (!TryParse(value, out var id))
        {
            throw new FormatException(
                $"'{value}' is not a VYK numeric companyId (expected 1–{MaxLength} decimal digits). " +
                "A public-site company oid is a different identifier space and is never accepted here.");
        }

        return id;
    }

    /// <summary>Shape check + parse (see <see cref="Parse"/>).</summary>
    public static bool TryParse(string? value, out VykCompanyId id)
    {
        id = default;
        if (value is null || value.Length == 0 || value.Length > MaxLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        id = new VykCompanyId(value);
        return true;
    }

    /// <summary>The stored spelling of the id.</summary>
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>
/// Where one identifier came from and as of when (FR-MDF-008) — recorded per space, so a mapping
/// row always answers "which body, which date" for the oid and, when present, for the VYK id
/// separately. Neither space's evidence ever stands in for the other's.
/// </summary>
/// <param name="Source">The resolution body/route that produced the identifier (e.g. the paced
/// <c>POST /tr/api/search/combined</c> with its keyword).</param>
/// <param name="AsOf">The evidence date of the resolution.</param>
public sealed record IdentityEvidence(string Source, DateOnly AsOf);

/// <summary>
/// A VYK-space observation supplied beside a public-site resolution: the numeric
/// <c>companyId</c> this instrument has in the <b>other</b> source, with its own provenance. It is
/// recorded in its own column, and nothing derives it from the public oid (or vice versa) — the
/// two coexist as facts about different spaces.
/// </summary>
/// <param name="CompanyId">The VYK numeric companyId.</param>
/// <param name="Evidence">Where that id came from and as of when.</param>
public sealed record KapPublicVykIdentity(VykCompanyId CompanyId, IdentityEvidence Evidence);

/// <summary>
/// One bound instrument to resolve into the public site's identifier space: our instrument id,
/// the symbol that is searched for, and — when the run also holds VYK evidence — the VYK-space
/// observation recorded beside the resolution.
/// </summary>
/// <param name="InstrumentId">Our <c>instruments.id</c> (the mapping's key).</param>
/// <param name="Symbol">The BIST ticker searched on the public site.</param>
/// <param name="Vyk">The optional VYK-space fact recorded beside the public oid.</param>
public sealed record KapPublicInstrumentBinding(
    long InstrumentId,
    string Symbol,
    KapPublicVykIdentity? Vyk = null);

/// <summary>
/// One resolved mapping ready for the store: the instrument, the public oid with its evidence, and
/// the optional VYK fact with its own evidence. The store writes each space to its own column.
/// </summary>
/// <param name="InstrumentId">Our instrument id.</param>
/// <param name="PublicOid">The public-site company oid.</param>
/// <param name="PublicEvidence">Provenance of the oid.</param>
/// <param name="VykCompanyId">The VYK numeric companyId, when the run holds such a fact.</param>
/// <param name="VykEvidence">Provenance of the VYK id.</param>
public sealed record KapPublicCompanyMapping(
    long InstrumentId,
    PublicCompanyOid PublicOid,
    IdentityEvidence PublicEvidence,
    VykCompanyId? VykCompanyId = null,
    IdentityEvidence? VykEvidence = null);

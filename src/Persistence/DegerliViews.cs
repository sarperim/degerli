namespace Degerli.Persistence;

/// <summary>
/// Read-only views defined by the data model (02 §3.1, §3.2). They are created by
/// the initial migration via raw SQL because EF does not model views natively.
/// </summary>
public static class DegerliViews
{
    /// <summary>Current XU100 universe — membership with no end date (FR-MDF-007).</summary>
    public const string CurrentUniverse = """
        CREATE VIEW v_current_universe AS
        SELECT i.id AS instrument_id,
               i.symbol,
               i.name,
               i.sector_id,
               i.listing_date,
               i.status
        FROM instruments i
        JOIN index_constituents ic ON ic.instrument_id = i.id
        JOIN indices idx ON idx.id = ic.index_id
        WHERE idx.code = 'XU100'
          AND ic.effective_to IS NULL;
        """;

    /// <summary>Last success/failure per job code, backing stale marking (FR-MDF-016).</summary>
    public const string DataFreshness = """
        CREATE VIEW v_data_freshness AS
        SELECT job_code,
               max(finished_at) FILTER (WHERE status = 'succeeded') AS last_success_at,
               max(finished_at) FILTER (WHERE status = 'failed') AS last_failure_at,
               max(finished_at) AS last_finished_at
        FROM ingest_runs
        GROUP BY job_code;
        """;
}

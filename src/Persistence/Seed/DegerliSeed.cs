using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Identity;

namespace Degerli.Persistence.Seed;

/// <summary>
/// Checked-in, idempotent reference/seed data (02 §8): the 18-row screener metric
/// catalog, the 6 macro series, and the <c>builder</c> role + bootstrap account.
/// Every statement is written so that running it again is a no-op; the same
/// statement list is used by the initial migration and by the re-runnable seeder.
/// </summary>
public static class DegerliSeed
{
    /// <summary>Bootstrap builder e-mail. Overridable is a deployment concern; the seed default is local/CI.</summary>
    public const string BuilderEmail = "builder@degerli.local";

    /// <summary>
    /// Default bootstrap builder password, used only for local development and CI.
    /// Outside Development/Test the checked-in value is deliberately refused and the
    /// deployed environment must supply <see cref="BuilderPasswordEnvironmentKey"/>
    /// (CWE-798; 02 §8 bootstrap account).
    /// </summary>
    public const string BuilderPassword = "Degerli-Builder-2026";

    /// <summary>Environment variable that supplies the bootstrap builder password in any environment.</summary>
    public const string BuilderPasswordEnvironmentKey = "DEGERLI_BUILDER_PASSWORD";

    private const string NormalizedBuilderEmail = "BUILDER@DEGERLI.LOCAL";

    private const string MetricCatalogSql = """
        INSERT INTO metric_catalog
            (metric_code, family, unit, label_tr, label_en, description_tr, description_en, is_screenable, is_growth_cagr, sort_order)
        VALUES
            ('pe', 'valuation', 'x', 'F/K', 'P/E', 'Fiyatın hisse başına kâra oranı', 'Price to earnings ratio', true, false, 1),
            ('pb', 'valuation', 'x', 'PD/DD', 'P/B', 'Piyasa değerinin defter değerine oranı', 'Price to book ratio', true, false, 2),
            ('ev_ebitda', 'valuation', 'x', 'FD/FAVÖK', 'EV/EBITDA', 'Firma değerinin FAVÖK''e oranı', 'Enterprise value to EBITDA', true, false, 3),
            ('ev_fcf', 'valuation', 'x', 'FD/Serbest Nakit Akışı', 'EV/FCF', 'Firma değerinin serbest nakit akışına oranı', 'Enterprise value to free cash flow', true, false, 4),
            ('fcf_yield', 'valuation', '%', 'Serbest Nakit Akışı Getirisi', 'FCF Yield', 'Serbest nakit akışının piyasa değerine oranı', 'Free cash flow to market cap', true, false, 5),
            ('roic', 'quality', '%', 'Yatırılan Sermaye Getirisi', 'ROIC', 'Vergi sonrası faaliyet kârının yatırılan sermayeye oranı', 'Return on invested capital', true, false, 6),
            ('roe', 'quality', '%', 'Özkaynak Kârlılığı', 'ROE', 'Net kârın özkaynağa oranı', 'Return on equity', true, false, 7),
            ('gross_margin', 'quality', '%', 'Brüt Kâr Marjı', 'Gross Margin', 'Brüt kârın hasılata oranı', 'Gross profit to revenue', true, false, 8),
            ('operating_margin', 'quality', '%', 'Faaliyet Kâr Marjı', 'Operating Margin', 'Faaliyet kârının hasılata oranı', 'Operating income to revenue', true, false, 9),
            ('rev_cagr', 'growth', '%', 'Hasılat Bileşik Büyüme', 'Revenue CAGR', 'Hasılatın seçilen dönemdeki bileşik büyümesi', 'Compound annual growth of revenue over the chosen window', true, true, 10),
            ('eps_cagr', 'growth', '%', 'HBK Bileşik Büyüme', 'EPS CAGR', 'Hisse başına kârın seçilen dönemdeki bileşik büyümesi', 'Compound annual growth of EPS over the chosen window', true, true, 11),
            ('fcf_cagr', 'growth', '%', 'Serbest Nakit Akışı Bileşik Büyüme', 'FCF CAGR', 'Serbest nakit akışının seçilen dönemdeki bileşik büyümesi', 'Compound annual growth of free cash flow over the chosen window', true, true, 12),
            ('net_debt_ebitda', 'financial_health', 'x', 'Net Borç/FAVÖK', 'Net Debt/EBITDA', 'Net borcun FAVÖK''e oranı', 'Net debt to EBITDA', true, false, 13),
            ('interest_coverage', 'financial_health', 'x', 'Faiz Karşılama Oranı', 'Interest Coverage', 'Faaliyet kârının finansman giderine oranı', 'Operating income to finance expense', true, false, 14),
            ('current_ratio', 'financial_health', 'x', 'Cari Oran', 'Current Ratio', 'Dönen varlıkların kısa vadeli borçlara oranı', 'Current assets to current liabilities', true, false, 15),
            ('div_yield', 'dividends', '%', 'Temettü Verimi', 'Dividend Yield', 'Son 12 ay temettüsünün fiyata oranı', 'Trailing twelve month dividend to price', true, false, 16),
            ('div_cagr', 'dividends', '%', 'Temettü Bileşik Büyüme', 'Dividend CAGR', 'Temettünün seçilen dönemdeki bileşik büyümesi', 'Compound annual growth of dividends over the chosen window', true, true, 17),
            ('payout_ratio', 'dividends', '%', 'Dağıtım Oranı', 'Payout Ratio', 'Temettünün net kâra oranı', 'Dividends to net income', true, false, 18)
        ON CONFLICT (metric_code) DO NOTHING;
        """;

    private const string MacroSeriesSql = """
        INSERT INTO macro_series (code, name_tr, name_en, unit, source_name, cadence)
        VALUES
            ('TUIK_CPI', 'TÜİK TÜFE (Yıllık)', 'TÜİK CPI (YoY)', '% YoY', 'TÜİK', 'monthly'),
            ('INDEP_CPI', 'Bağımsız TÜFE (Yıllık)', 'Independent CPI (YoY)', '% YoY', 'ENAG', 'monthly'),
            ('CBRT_REPO', 'TCMB Politika Faizi', 'CBRT Policy Rate', '%', 'TCMB', 'per_release'),
            ('USD_TRY', 'ABD Doları/TL', 'USD/TRY', 'TRY', 'FX', 'daily'),
            ('EUR_TRY', 'Euro/TL', 'EUR/TRY', 'TRY', 'FX', 'daily'),
            ('GOLD', 'Altın (ons)', 'Gold (oz)', 'USD/oz', 'Gold', 'daily')
        ON CONFLICT (code) DO NOTHING;
        """;

    private const string BuilderRoleSql = """
        INSERT INTO asp_net_roles (name, normalized_name, concurrency_stamp)
        SELECT 'builder', 'BUILDER', gen_random_uuid()::text
        WHERE NOT EXISTS (SELECT 1 FROM asp_net_roles WHERE normalized_name = 'BUILDER');
        """;

    /// <summary>
    /// Account bootstrap statement. The password hash is hex-encoded and decoded inside
    /// SQL, so the rendered statement cannot break out of its literal regardless of the
    /// input — there is no string-interpolation injection sink (CWE-89).
    /// </summary>
    private static string BuilderAccountSql(string passwordHash)
    {
        var hexHash = Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(passwordHash));
        return $@"
        INSERT INTO asp_net_users
            (email, normalized_email, user_name, normalized_user_name, email_confirmed, password_hash,
             security_stamp, concurrency_stamp, phone_number_confirmed, two_factor_enabled,
             lockout_enabled, access_failed_count, language_pref)
        SELECT '{BuilderEmail}', '{NormalizedBuilderEmail}', '{BuilderEmail}', '{NormalizedBuilderEmail}',
               true, convert_from(decode('{hexHash}', 'hex'), 'UTF8'), 'SEEDED-SECURITY-STAMP', gen_random_uuid()::text,
               false, false, true, 0, 'tr'
        WHERE NOT EXISTS (SELECT 1 FROM asp_net_users WHERE normalized_email = '{NormalizedBuilderEmail}');
        ";
    }

    private const string BuilderUserRoleSql = """
        INSERT INTO asp_net_user_roles (user_id, role_id)
        SELECT u.id, r.id
        FROM asp_net_users u
        CROSS JOIN asp_net_roles r
        WHERE u.normalized_email = 'BUILDER@DEGERLI.LOCAL'
          AND r.normalized_name = 'BUILDER'
          AND NOT EXISTS (
              SELECT 1 FROM asp_net_user_roles ur WHERE ur.user_id = u.id AND ur.role_id = r.id
          );
        """;

    /// <summary>
    /// Resolves the bootstrap builder password: the <see cref="BuilderPasswordEnvironmentKey"/>
    /// environment value when supplied, otherwise the checked-in dev/CI default. The
    /// default is refused in an explicitly deployed environment (anything other than
    /// Development/Test) so a repository-known credential can never reach production.
    /// </summary>
    public static string ResolveBuilderPassword()
    {
        var configured = Environment.GetEnvironmentVariable(BuilderPasswordEnvironmentKey);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        if (IsDeployedEnvironment())
        {
            throw new InvalidOperationException(
                $"{BuilderPasswordEnvironmentKey} must be supplied outside Development/Test: the " +
                "checked-in builder password is a dev/CI-only default (CWE-798, 02 §8).");
        }

        return BuilderPassword;
    }

    /// <summary>
    /// Hashes the resolved bootstrap builder password with Identity's password hasher
    /// so the stored value verifies against <see cref="ResolveBuilderPassword"/>.
    /// </summary>
    public static string HashBuilderPassword()
    {
        var hasher = new PasswordHasher<ApplicationUser>();
        return hasher.HashPassword(
            new ApplicationUser { UserName = BuilderEmail, Email = BuilderEmail },
            ResolveBuilderPassword());
    }

    private static bool IsDeployedEnvironment()
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");

        return !string.IsNullOrWhiteSpace(environment)
            && !environment.Equals("Development", StringComparison.OrdinalIgnoreCase)
            && !environment.Equals("Test", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// All seed statements in application order. The builder password is resolved and
    /// hashed internally — no caller-supplied string is ever placed into SQL text.
    /// </summary>
    public static IReadOnlyList<string> Statements() =>
    [
        MetricCatalogSql,
        MacroSeriesSql,
        BuilderRoleSql,
        BuilderAccountSql(HashBuilderPassword()),
        BuilderUserRoleSql,
    ];
}

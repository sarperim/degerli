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
    /// It is refused unless the process explicitly declares a Development or Test
    /// environment; any other or unset environment must supply
    /// <see cref="BuilderPasswordEnvironmentKey"/> (CWE-798; 02 §8 bootstrap account).
    /// </summary>
    public const string BuilderPassword = "Degerli-Builder-2026";

    /// <summary>Environment variable that supplies the bootstrap builder password in any environment.</summary>
    public const string BuilderPasswordEnvironmentKey = "DEGERLI_BUILDER_PASSWORD";

    /// <summary>Minimum length accepted for a caller-supplied bootstrap builder password.</summary>
    public const int MinimumBuilderPasswordLength = 12;

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
            ('payout_ratio', 'dividends', '%', 'Dağıtım Oranı', 'Payout Ratio', 'Temettünün net kâra oranı', 'Dividends to net income', true, false, 18),
            -- The key-value & moat panel's codes (TKT-foundation-096, 02 §6.2 v2). Seeded with
            -- is_screenable = false: the catalog row exists so the code has a name, a family and
            -- a unit, while making it a screener criterion stays the builder's later toggle
            -- (FR-SCR-017 — a flag flip, no code change).
            ('roa', 'quality', '%', 'Aktif Kârlılığı', 'ROA', 'Net kârın toplam aktiflere oranı', 'Net income to total assets', false, false, 19),
            ('croic', 'quality', '%', 'Nakit Yatırılan Sermaye Getirisi', 'CROIC', 'İşletme nakit akışının yatırılan sermayeye oranı', 'Operating cash flow to invested capital', false, false, 20),
            ('ev_ebit', 'valuation', 'x', 'FD/FVÖK', 'EV/EBIT', 'Firma değerinin faaliyet kârına oranı', 'Enterprise value to operating income', false, false, 21),
            ('p_fcf', 'valuation', 'x', 'PD/Serbest Nakit Akışı', 'P/FCF', 'Piyasa değerinin serbest nakit akışına oranı', 'Market cap to free cash flow', false, false, 22),
            ('p_tbv', 'valuation', 'x', 'PD/Maddi Defter Değeri', 'P/TBV', 'Piyasa değerinin maddi defter değerine oranı', 'Market cap to tangible book value', false, false, 23),
            ('fcf_ni', 'quality', 'x', 'Serbest Nakit Akışı/Net Kâr', 'FCF/NI', 'Serbest nakit akışının net kâra oranı', 'Free cash flow to net income', false, false, 24),
            ('fcf_ni_3y', 'quality', 'x', 'Serbest Nakit Akışı/Net Kâr (3Y Ort.)', 'FCF/NI (3Y Avg)', 'Son üç yılın serbest nakit akışı/net kâr oranı ortalaması', 'Three-year average of free cash flow to net income', false, false, 25),
            ('cfo_ebit', 'quality', 'x', 'İşletme Nakit Akışı/FVÖK', 'CFO/EBIT', 'İşletme nakit akışının faaliyet kârına oranı', 'Operating cash flow to operating income', false, false, 26),
            ('monetary_gain_pretax', 'quality', '%', 'Parasal Kazanç/Vergi Öncesi Kâr', 'Monetary Gain/Pretax', 'TMS 29 parasal pozisyon kazancının vergi öncesi kâra oranı', 'TMS 29 net monetary gain to pretax income', false, false, 27),
            ('cfo_interest', 'financial_health', 'x', 'İşletme Nakit Akışı/Nakit Faiz', 'CFO/Cash Interest', 'İşletme nakit akışının ödenen nakit faize oranı', 'Operating cash flow to cash interest paid', false, false, 28),
            ('net_debt_fcf', 'financial_health', 'x', 'Net Borç/Serbest Nakit Akışı', 'Net Debt/FCF', 'Net borcun serbest nakit akışına oranı', 'Net debt to free cash flow', false, false, 29),
            ('ebit_cagr', 'growth', '%', 'FVÖK Bileşik Büyüme', 'EBIT CAGR', 'Faaliyet kârının seçilen dönemdeki bileşik büyümesi', 'Compound annual growth of operating income over the chosen window', false, true, 30),
            ('bvps_cagr', 'growth', '%', 'HBDA Bileşik Büyüme', 'BVPS CAGR', 'Hisse başına defter değerinin seçilen dönemdeki bileşik büyümesi', 'Compound annual growth of book value per share over the chosen window', false, true, 31),
            ('fcf_ps_cagr', 'growth', '%', 'Hisse Başına Serbest Nakit Akışı Bileşik Büyüme', 'FCF/Share CAGR', 'Hisse başına serbest nakit akışının seçilen dönemdeki bileşik büyümesi', 'Compound annual growth of free cash flow per share over the chosen window', false, true, 32)
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
    /// guard fails closed: the default is refused unless the process explicitly declares
    /// a Development or Test environment (an unset/empty environment is treated as
    /// deployed), so the repository-known credential cannot be selected wherever the
    /// environment is not explicitly dev/CI (CWE-798; 02 §8 bootstrap account).
    /// </summary>
    public static string ResolveBuilderPassword()
    {
        var configured = Environment.GetEnvironmentVariable(BuilderPasswordEnvironmentKey);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (configured.Length < MinimumBuilderPasswordLength)
            {
                throw new InvalidOperationException(
                    $"{BuilderPasswordEnvironmentKey} must be at least " +
                    $"{MinimumBuilderPasswordLength} characters.");
            }

            return configured;
        }

        if (IsDeployedEnvironment())
        {
            throw new InvalidOperationException(
                $"{BuilderPasswordEnvironmentKey} must be supplied unless the environment is " +
                "explicitly Development or Test: the checked-in builder password is a dev/CI-only " +
                "default (CWE-798, 02 §8).");
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

    /// <summary>
    /// Fails closed: returns <c>true</c> (deployed) unless the process explicitly declares
    /// a Development or Test environment. An unset or empty environment is treated as
    /// deployed so the checked-in default is never used by default. This preserves
    /// ASP.NET Core's host precedence — <c>ASPNETCORE_ENVIRONMENT</c> over
    /// <c>DOTNET_ENVIRONMENT</c>.
    /// </summary>
    private static bool IsDeployedEnvironment()
    {
        var aspNetCoreEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var dotNetEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        var environment = !string.IsNullOrWhiteSpace(aspNetCoreEnvironment)
            ? aspNetCoreEnvironment
            : dotNetEnvironment;

        if (string.IsNullOrWhiteSpace(environment))
        {
            return true;
        }

        return !environment.Equals("Development", StringComparison.OrdinalIgnoreCase)
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

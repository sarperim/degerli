using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Persistence;

/// <summary>
/// The single EF Core model for the platform (C5). Every table in
/// <c>02-data-model.md</c> §3 is mapped here; identity uses ASP.NET Core Identity's
/// standard schema with <c>long</c> keys (02 §1.7). Naming is snake_case via the
/// naming convention configured on the context options.
/// </summary>
public class DegerliDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, long>
{
    public DegerliDbContext(DbContextOptions<DegerliDbContext> options) : base(options) { }

    // MDF
    public DbSet<Sector> Sectors => Set<Sector>();
    public DbSet<Instrument> Instruments => Set<Instrument>();
    public DbSet<MarketIndex> Indices => Set<MarketIndex>();
    public DbSet<IndexConstituent> IndexConstituents => Set<IndexConstituent>();
    public DbSet<DailyPrice> DailyPrices => Set<DailyPrice>();
    public DbSet<IndexLevel> IndexLevels => Set<IndexLevel>();
    public DbSet<FinancialStatement> FinancialStatements => Set<FinancialStatement>();
    public DbSet<FinLineItem> FinLineItems => Set<FinLineItem>();
    public DbSet<Dividend> Dividends => Set<Dividend>();
    public DbSet<CorporateAction> CorporateActions => Set<CorporateAction>();
    public DbSet<KapDisclosure> KapDisclosures => Set<KapDisclosure>();
    public DbSet<DerivedMetric> DerivedMetrics => Set<DerivedMetric>();
    public DbSet<CoverageMetadata> CoverageMetadata => Set<CoverageMetadata>();
    public DbSet<IngestRun> IngestRuns => Set<IngestRun>();
    public DbSet<QuarantinedFact> QuarantinedFacts => Set<QuarantinedFact>();

    // MOV
    public DbSet<MacroSeries> MacroSeries => Set<MacroSeries>();
    public DbSet<MacroValue> MacroValues => Set<MacroValue>();
    public DbSet<MarketSnapshot> MarketSnapshots => Set<MarketSnapshot>();

    // SCR
    public DbSet<MetricCatalogEntry> MetricCatalog => Set<MetricCatalogEntry>();
    public DbSet<SavedScreen> SavedScreens => Set<SavedScreen>();

    // RES
    public DbSet<BusinessDescription> BusinessDescriptions => Set<BusinessDescription>();
    public DbSet<SectorMetricMedian> SectorMetricMedians => Set<SectorMetricMedian>();

    // VAL
    public DbSet<DcfBaseline> DcfBaselines => Set<DcfBaseline>();
    public DbSet<DcfScenario> DcfScenarios => Set<DcfScenario>();

    // ACC
    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();

    // FDF
    public DbSet<Fund> Funds => Set<Fund>();
    public DbSet<FundNav> FundNavs => Set<FundNav>();
    public DbSet<FundPerformance> FundPerformances => Set<FundPerformance>();
    public DbSet<FundHolding> FundHoldings => Set<FundHolding>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // 02 header convention: monetary values numeric(18,4), dates date, all
        // timestamps timestamptz (UTC). Ratio metrics widen to (18,6) below.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
        configurationBuilder.Properties<DateOnly>().HaveColumnType("date");
        configurationBuilder.Properties<DateTimeOffset>().HaveColumnType("timestamptz");
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        ConfigureIdentity(builder);
        ConfigureMarketData(builder);
        ConfigureMarketOverview(builder);
        ConfigureScreening(builder);
        ConfigureResearch(builder);
        ConfigureValuation(builder);
        ConfigureAccounts(builder);
        ConfigureFunds(builder);
    }

    private static void ConfigureIdentity(ModelBuilder builder)
    {
        // 02 §1.7 / §3.6 — Identity's standard schema prefixed `asp_net_*` with
        // `language_pref` extension. Email verification is Identity's EmailConfirmed.
        builder.Entity<ApplicationUser>(b =>
        {
            b.ToTable("asp_net_users");
            b.Property(u => u.LanguagePref).HasColumnName("language_pref").HasDefaultValue("tr").IsRequired();
        });
        builder.Entity<ApplicationRole>().ToTable("asp_net_roles");
        builder.Entity<IdentityUserRole<long>>().ToTable("asp_net_user_roles");
        builder.Entity<IdentityUserClaim<long>>().ToTable("asp_net_user_claims");
        builder.Entity<IdentityUserLogin<long>>().ToTable("asp_net_user_logins");
        builder.Entity<IdentityUserToken<long>>().ToTable("asp_net_user_tokens");
        builder.Entity<IdentityRoleClaim<long>>().ToTable("asp_net_role_claims");
    }

    private static void ConfigureMarketData(ModelBuilder builder)
    {
        builder.Entity<Sector>(b =>
        {
            b.ToTable("sectors");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.Code).IsRequired();
            b.Property(x => x.NameTr).IsRequired();
            b.Property(x => x.NameEn).IsRequired();
            b.HasIndex(x => x.Code).IsUnique();
            b.HasOne<Sector>().WithMany().HasForeignKey(x => x.ParentSectorId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Instrument>(b =>
        {
            b.ToTable("instruments");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.Symbol).IsRequired();
            b.Property(x => x.Name).IsRequired();
            b.Property(x => x.SourceRef).IsRequired();
            b.HasIndex(x => x.Symbol).IsUnique();
            b.HasOne<Sector>().WithMany().HasForeignKey(x => x.SectorId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<MarketIndex>(b =>
        {
            b.ToTable("indices");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.Code).IsRequired();
            b.Property(x => x.NameTr).IsRequired();
            b.Property(x => x.NameEn).IsRequired();
            b.HasIndex(x => x.Code).IsUnique();
        });

        builder.Entity<IndexConstituent>(b =>
        {
            b.ToTable("index_constituents");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.SourceRef).IsRequired();
            b.HasOne<MarketIndex>().WithMany().HasForeignKey(x => x.IndexId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Instrument>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<DailyPrice>(b =>
        {
            b.ToTable("daily_prices");
            b.HasKey(x => new { x.InstrumentId, x.PriceDate });
            b.Property(x => x.SourceRef).IsRequired();
            b.Property(x => x.CloseRaw).IsRequired();
            b.HasOne<Instrument>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<IndexLevel>(b =>
        {
            b.ToTable("index_levels");
            b.HasKey(x => new { x.IndexId, x.LevelDate });
            b.Property(x => x.SourceRef).IsRequired();
            b.HasOne<MarketIndex>().WithMany().HasForeignKey(x => x.IndexId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<FinancialStatement>(b =>
        {
            b.ToTable("financial_statements");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.PeriodType).IsRequired();
            b.Property(x => x.StatementType).IsRequired();
            b.Property(x => x.Version).IsRequired();
            b.Property(x => x.SourceRef).IsRequired();
            b.HasIndex(x => new { x.InstrumentId, x.PeriodType, x.PeriodEndDate, x.StatementType, x.Version }).IsUnique();
            b.HasOne<Instrument>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<FinLineItem>(b =>
        {
            b.ToTable("fin_line_items");
            b.HasKey(x => new { x.StatementId, x.ItemCode });
            b.Property(x => x.ItemCode).IsRequired();
            b.HasOne<FinancialStatement>().WithMany().HasForeignKey(x => x.StatementId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Dividend>(b =>
        {
            b.ToTable("dividends");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.SourceRef).IsRequired();
            b.HasOne<Instrument>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<CorporateAction>(b =>
        {
            b.ToTable("corporate_actions");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.ActionType).IsRequired();
            b.Property(x => x.SourceRef).IsRequired();
            b.Property(x => x.TermsJson).HasColumnType("jsonb");
            b.HasOne<Instrument>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<KapDisclosure>(b =>
        {
            b.ToTable("kap_disclosures");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.SourceRef).IsRequired();
            b.HasOne<Instrument>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<DerivedMetric>(b =>
        {
            b.ToTable("derived_metrics");
            b.HasKey(x => new { x.InstrumentId, x.MetricCode, x.AsOfDate });
            b.Property(x => x.MetricCode).IsRequired();
            b.Property(x => x.Value).HasPrecision(18, 6);
            b.HasOne<Instrument>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<CoverageMetadata>(b =>
        {
            b.ToTable("coverage_metadata");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.Scope).IsRequired();
            b.Property(x => x.DataType).IsRequired();
        });

        builder.Entity<IngestRun>(b =>
        {
            b.ToTable("ingest_runs");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.JobCode).IsRequired();
            b.Property(x => x.StatsJson).HasColumnType("jsonb");
        });

        builder.Entity<QuarantinedFact>(b =>
        {
            b.ToTable("quarantined_facts");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.JobCode).IsRequired();
            b.Property(x => x.ReasonCode).IsRequired();
            b.Property(x => x.Status).IsRequired();
            b.Property(x => x.PayloadJson).HasColumnType("jsonb");
        });
    }

    private static void ConfigureMarketOverview(ModelBuilder builder)
    {
        builder.Entity<MacroSeries>(b =>
        {
            b.ToTable("macro_series");
            b.HasKey(x => x.Code);
            b.Property(x => x.Code).IsRequired();
            b.Property(x => x.NameTr).IsRequired();
            b.Property(x => x.NameEn).IsRequired();
        });

        builder.Entity<MacroValue>(b =>
        {
            b.ToTable("macro_values");
            b.HasKey(x => new { x.SeriesCode, x.ValueDate, x.RecordedAt });
            b.Property(x => x.SourceRef).IsRequired();
            b.Property(x => x.Value).HasPrecision(18, 6);
            b.HasOne<MacroSeries>().WithMany().HasForeignKey(x => x.SeriesCode).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<MarketSnapshot>(b =>
        {
            b.ToTable("market_snapshots");
            b.HasKey(x => x.SnapshotDate);
            b.Property(x => x.Xu100ChangePct).HasPrecision(18, 6);
            b.Property(x => x.Xu30ChangePct).HasPrecision(18, 6);
            b.Property(x => x.MarketPe).HasPrecision(18, 6);
            b.Property(x => x.MarketDivYield).HasPrecision(18, 6);
            b.Property(x => x.GainersJson).HasColumnType("jsonb");
            b.Property(x => x.LosersJson).HasColumnType("jsonb");
            b.Property(x => x.SectorPerfJson).HasColumnType("jsonb");
        });
    }

    private static void ConfigureScreening(ModelBuilder builder)
    {
        builder.Entity<MetricCatalogEntry>(b =>
        {
            b.ToTable("metric_catalog");
            b.HasKey(x => x.MetricCode);
            b.Property(x => x.MetricCode).IsRequired();
            b.Property(x => x.Family).IsRequired();
            b.Property(x => x.LabelTr).IsRequired();
            b.Property(x => x.LabelEn).IsRequired();
        });

        builder.Entity<SavedScreen>(b =>
        {
            b.ToTable("saved_screens");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.Name).IsRequired();
            b.Property(x => x.CriteriaJson).HasColumnType("jsonb").IsRequired();
            b.HasIndex(x => new { x.UserId, x.Name }).IsUnique();
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureResearch(ModelBuilder builder)
    {
        builder.Entity<BusinessDescription>(b =>
        {
            b.ToTable("business_descriptions", t =>
                t.HasCheckConstraint(
                    "ck_business_descriptions_published_texts",
                    "status <> 'published' OR (text_tr IS NOT NULL AND text_en IS NOT NULL)"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.Status).IsRequired();
            b.Property(x => x.SourceRefsJson).HasColumnType("jsonb");
            b.HasOne<Instrument>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SectorMetricMedian>(b =>
        {
            b.ToTable("sector_metric_medians");
            b.HasKey(x => new { x.SectorId, x.MetricCode, x.AsOfDate });
            b.Property(x => x.MetricCode).IsRequired();
            b.Property(x => x.MedianValue).HasPrecision(18, 6);
            b.HasOne<Sector>().WithMany().HasForeignKey(x => x.SectorId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureValuation(ModelBuilder builder)
    {
        builder.Entity<DcfBaseline>(b =>
        {
            b.ToTable("dcf_baselines");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.ParamsJson).HasColumnType("jsonb").IsRequired();
            b.HasOne<Instrument>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<DcfScenario>(b =>
        {
            b.ToTable("dcf_scenarios");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.Name).IsRequired();
            b.Property(x => x.ParamsJson).HasColumnType("jsonb").IsRequired();
            b.HasIndex(x => new { x.UserId, x.InstrumentId, x.Name }).IsUnique();
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Instrument>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureAccounts(ModelBuilder builder)
    {
        builder.Entity<ConsentRecord>(b =>
        {
            b.ToTable("consent_records");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.UserRefHash).IsRequired();
            b.Property(x => x.NoticeVersion).IsRequired();
            b.Property(x => x.Action).IsRequired();
            // FK without cascade: deletion nulls user_id, the evidence row survives (02 §5.4).
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureFunds(ModelBuilder builder)
    {
        builder.Entity<Fund>(b =>
        {
            b.ToTable("funds");
            b.HasKey(x => x.Code);
            b.Property(x => x.Code).IsRequired();
            b.Property(x => x.Name).IsRequired();
            b.Property(x => x.SourceRef).IsRequired();
        });

        builder.Entity<FundNav>(b =>
        {
            b.ToTable("fund_navs");
            b.HasKey(x => new { x.FundId, x.NavDate });
            b.Property(x => x.SourceRef).IsRequired();
            b.HasOne<Fund>().WithMany().HasForeignKey(x => x.FundId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<FundPerformance>(b =>
        {
            b.ToTable("fund_performances");
            b.HasKey(x => new { x.FundId, x.Period, x.AsOfDate });
            b.Property(x => x.Period).IsRequired();
            b.Property(x => x.SourceRef).IsRequired();
            b.Property(x => x.ReturnValue).HasPrecision(18, 6);
            b.HasOne<Fund>().WithMany().HasForeignKey(x => x.FundId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<FundHolding>(b =>
        {
            b.ToTable("fund_holdings");
            b.HasKey(x => new { x.FundId, x.AsOfDate, x.LineNo });
            b.Property(x => x.SourceRef).IsRequired();
            b.HasOne<Fund>().WithMany().HasForeignKey(x => x.FundId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Instrument>().WithMany().HasForeignKey(x => x.InstrumentId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}

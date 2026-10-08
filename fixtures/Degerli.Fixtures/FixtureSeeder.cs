using Degerli.Persistence;
using Degerli.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Degerli.Fixtures;

/// <summary>
/// Persists a <see cref="FixtureSet"/> into a migrated database. Every statement is
/// written so that re-running is a no-op: rows are matched on their natural keys and
/// only the missing ones are inserted (test strategy §8.3 — idempotent and
/// re-runnable). Both the L2 and L4 variants go through this one entry point.
/// </summary>
public static class FixtureSeeder
{
    private const string SourceRef = "fixture:universe";

    /// <summary>Seeds (or tops up) the fixture universe for <paramref name="set"/>.</summary>
    public static async Task ApplyAsync(
        DegerliDbContext db,
        FixtureSet set,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(set);

        await SeedSectorsAndIndicesAsync(db, set, cancellationToken).ConfigureAwait(false);
        await SeedInstrumentsAsync(db, set, cancellationToken).ConfigureAwait(false);
        await SeedMembershipsAsync(db, set, cancellationToken).ConfigureAwait(false);
        await SeedPricesAsync(db, set, cancellationToken).ConfigureAwait(false);
        await SeedIndexLevelsAsync(db, set, cancellationToken).ConfigureAwait(false);
        await SeedStatementsAsync(db, set, cancellationToken).ConfigureAwait(false);
        await SeedDividendsAsync(db, set, cancellationToken).ConfigureAwait(false);
        await SeedCorporateActionsAsync(db, set, cancellationToken).ConfigureAwait(false);
        await SeedKapDisclosuresAsync(db, set, cancellationToken).ConfigureAwait(false);
        await SeedMacroValuesAsync(db, set, cancellationToken).ConfigureAwait(false);
        await SeedFundsAsync(db, set, cancellationToken).ConfigureAwait(false);
        await SeedAccountsAsync(db, set, cancellationToken).ConfigureAwait(false);
        await SeedUserContentAsync(db, set, cancellationToken).ConfigureAwait(false);
        await SeedDescriptionsAsync(db, set, cancellationToken).ConfigureAwait(false);
        await SeedCoverageAsync(db, set, cancellationToken).ConfigureAwait(false);
    }

    private static DateTimeOffset At(FixtureAnchor anchor, int dayOffset = 0) =>
        new(anchor.T.AddDays(dayOffset).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static async Task SeedSectorsAndIndicesAsync(
        DegerliDbContext db,
        FixtureSet set,
        CancellationToken ct)
    {
        var sectorCodes = (await db.Sectors.Select(s => s.Code).ToListAsync(ct).ConfigureAwait(false))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var sector in set.Sectors.Where(s => !sectorCodes.Contains(s.Code)))
        {
            db.Sectors.Add(new Sector
            {
                Code = sector.Code,
                NameTr = sector.NameTr,
                NameEn = sector.NameEn,
            });
        }

        var indexCodes = (await db.Indices.Select(i => i.Code).ToListAsync(ct).ConfigureAwait(false))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var index in set.Indices.Where(i => !indexCodes.Contains(i.Code)))
        {
            db.Indices.Add(new MarketIndex
            {
                Code = index.Code,
                NameTr = index.NameTr,
                NameEn = index.NameEn,
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task SeedInstrumentsAsync(
        DegerliDbContext db,
        FixtureSet set,
        CancellationToken ct)
    {
        var existing = (await db.Instruments.Select(i => i.Symbol).ToListAsync(ct).ConfigureAwait(false))
            .ToHashSet(StringComparer.Ordinal);
        var sectors = await db.Sectors.ToDictionaryAsync(s => s.Code, s => s.Id, ct).ConfigureAwait(false);

        foreach (var instrument in set.Instruments.Where(i => !existing.Contains(i.Symbol)))
        {
            db.Instruments.Add(new Instrument
            {
                Symbol = instrument.Symbol,
                Name = instrument.Name,
                SectorId = instrument.SectorCode is not null && sectors.TryGetValue(instrument.SectorCode, out var sectorId)
                    ? sectorId
                    : null,
                ListingDate = instrument.ListingDate,
                Status = "active",
                SourceRef = $"{SourceRef}:instruments",
                RecordedAt = At(set.Anchor),
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task SeedMembershipsAsync(
        DegerliDbContext db,
        FixtureSet set,
        CancellationToken ct)
    {
        var indexIds = await db.Indices.ToDictionaryAsync(i => i.Code, i => i.Id, ct).ConfigureAwait(false);
        var instrumentIds = await db.Instruments.ToDictionaryAsync(i => i.Symbol, i => i.Id, ct).ConfigureAwait(false);

        var existing = (await db.IndexConstituents
                .Select(c => new { c.IndexId, c.InstrumentId, c.EffectiveFrom })
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .Select(c => (c.IndexId, c.InstrumentId, c.EffectiveFrom))
            .ToHashSet();

        foreach (var membership in set.Memberships)
        {
            var key = (indexIds[membership.IndexCode], instrumentIds[membership.Symbol], membership.EffectiveFrom);
            if (existing.Contains(key))
            {
                continue;
            }

            db.IndexConstituents.Add(new IndexConstituent
            {
                IndexId = key.Item1,
                InstrumentId = key.Item2,
                EffectiveFrom = membership.EffectiveFrom,
                EffectiveTo = membership.EffectiveTo,
                SourceRef = $"{SourceRef}:membership",
                RecordedAt = At(set.Anchor),
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task SeedPricesAsync(
        DegerliDbContext db,
        FixtureSet set,
        CancellationToken ct)
    {
        var instrumentIds = await db.Instruments.ToDictionaryAsync(i => i.Symbol, i => i.Id, ct).ConfigureAwait(false);

        var existing = (await db.DailyPrices
                .Select(p => new { p.InstrumentId, p.PriceDate })
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .Select(p => (p.InstrumentId, p.PriceDate))
            .ToHashSet();

        foreach (var price in set.Prices)
        {
            var instrumentId = instrumentIds[price.Symbol];
            if (!existing.Add((instrumentId, price.Date)))
            {
                continue;
            }

            db.DailyPrices.Add(new DailyPrice
            {
                InstrumentId = instrumentId,
                PriceDate = price.Date,
                Open = price.Open,
                High = price.High,
                Low = price.Low,
                CloseRaw = price.CloseRaw,
                CloseAdjusted = price.CloseAdjusted ?? price.CloseRaw,
                Volume = price.Volume,
                SourceRef = $"{SourceRef}:prices",
                RecordedAt = At(set.Anchor),
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task SeedIndexLevelsAsync(
        DegerliDbContext db,
        FixtureSet set,
        CancellationToken ct)
    {
        var indexIds = await db.Indices.ToDictionaryAsync(i => i.Code, i => i.Id, ct).ConfigureAwait(false);

        var existing = (await db.IndexLevels
                .Select(l => new { l.IndexId, l.LevelDate })
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .Select(l => (l.IndexId, l.LevelDate))
            .ToHashSet();

        foreach (var level in set.IndexLevels)
        {
            var indexId = indexIds[level.IndexCode];
            if (!existing.Add((indexId, level.Date)))
            {
                continue;
            }

            db.IndexLevels.Add(new IndexLevel
            {
                IndexId = indexId,
                LevelDate = level.Date,
                Close = level.Close,
                SourceRef = $"{SourceRef}:index-levels",
                RecordedAt = At(set.Anchor),
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task SeedStatementsAsync(
        DegerliDbContext db,
        FixtureSet set,
        CancellationToken ct)
    {
        var instrumentIds = await db.Instruments.ToDictionaryAsync(i => i.Symbol, i => i.Id, ct).ConfigureAwait(false);

        var existing = (await db.FinancialStatements
                .Select(s => new { s.InstrumentId, s.PeriodType, s.PeriodEndDate, s.StatementType, s.Version })
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .Select(s => Key(s.InstrumentId, s.PeriodType, s.PeriodEndDate, s.StatementType, s.Version))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var statement in set.Statements)
        {
            var instrumentId = instrumentIds[statement.Symbol];
            if (!existing.Add(Key(instrumentId, statement.PeriodType, statement.PeriodEndDate, statement.StatementType, statement.Version)))
            {
                continue;
            }

            db.FinancialStatements.Add(new FinancialStatement
            {
                InstrumentId = instrumentId,
                PeriodType = statement.PeriodType,
                PeriodEndDate = statement.PeriodEndDate,
                FiscalYear = statement.FiscalYear,
                StatementType = statement.StatementType,
                Version = statement.Version,
                RestatementDate = statement.RestatementDate,
                PublishedAt = statement.PublishedAt,
                SourceRef = $"{SourceRef}:statements",
                RecordedAt = At(set.Anchor),
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var statementIds = (await db.FinancialStatements
                .Select(s => new { s.Id, s.InstrumentId, s.PeriodType, s.PeriodEndDate, s.StatementType, s.Version })
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .ToDictionary(
                s => Key(s.InstrumentId, s.PeriodType, s.PeriodEndDate, s.StatementType, s.Version),
                s => s.Id,
                StringComparer.Ordinal);

        var existingLines = (await db.FinLineItems
                .Select(l => new { l.StatementId, l.ItemCode })
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .Select(l => $"{l.StatementId}|{l.ItemCode}")
            .ToHashSet(StringComparer.Ordinal);

        foreach (var statement in set.Statements)
        {
            var instrumentId = instrumentIds[statement.Symbol];
            var statementId = statementIds[Key(instrumentId, statement.PeriodType, statement.PeriodEndDate, statement.StatementType, statement.Version)];
            foreach (var (itemCode, value) in statement.Lines)
            {
                if (!existingLines.Add($"{statementId}|{itemCode}"))
                {
                    continue;
                }

                db.FinLineItems.Add(new FinLineItem
                {
                    StatementId = statementId,
                    ItemCode = itemCode,
                    Value = value,
                });
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task SeedDividendsAsync(
        DegerliDbContext db,
        FixtureSet set,
        CancellationToken ct)
    {
        var instrumentIds = await db.Instruments.ToDictionaryAsync(i => i.Symbol, i => i.Id, ct).ConfigureAwait(false);

        var existing = (await db.Dividends
                .Select(d => new { d.InstrumentId, d.ExDate, d.AmountPerShare })
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .Select(d => (d.InstrumentId, d.ExDate, d.AmountPerShare))
            .ToHashSet();

        foreach (var dividend in set.Dividends)
        {
            var instrumentId = instrumentIds[dividend.Symbol];
            if (!existing.Add((instrumentId, dividend.ExDate, dividend.AmountPerShare)))
            {
                continue;
            }

            db.Dividends.Add(new Dividend
            {
                InstrumentId = instrumentId,
                ExDate = dividend.ExDate,
                PayDate = dividend.PayDate,
                AmountPerShare = dividend.AmountPerShare,
                Currency = dividend.Currency,
                SourceRef = $"{SourceRef}:dividends",
                RecordedAt = At(set.Anchor),
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task SeedCorporateActionsAsync(
        DegerliDbContext db,
        FixtureSet set,
        CancellationToken ct)
    {
        var instrumentIds = await db.Instruments.ToDictionaryAsync(i => i.Symbol, i => i.Id, ct).ConfigureAwait(false);

        var existing = (await db.CorporateActions
                .Select(a => new { a.InstrumentId, a.ActionType, a.ActionDate })
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .Select(a => (a.InstrumentId, a.ActionType, a.ActionDate))
            .ToHashSet();

        foreach (var action in set.CorporateActions)
        {
            var instrumentId = instrumentIds[action.Symbol];
            if (!existing.Add((instrumentId, action.ActionType, action.ActionDate)))
            {
                continue;
            }

            db.CorporateActions.Add(new CorporateAction
            {
                InstrumentId = instrumentId,
                ActionType = action.ActionType,
                ActionDate = action.ActionDate,
                TermsJson = action.TermsJson,
                SourceRef = $"{SourceRef}:corporate-actions",
                RecordedAt = At(set.Anchor),
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task SeedKapDisclosuresAsync(
        DegerliDbContext db,
        FixtureSet set,
        CancellationToken ct)
    {
        var instrumentIds = await db.Instruments.ToDictionaryAsync(i => i.Symbol, i => i.Id, ct).ConfigureAwait(false);
        var existing = (await db.KapDisclosures.Select(k => k.Id).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();

        foreach (var disclosure in set.KapDisclosures.Where(k => !existing.Contains(k.Id)))
        {
            db.KapDisclosures.Add(new KapDisclosure
            {
                Id = disclosure.Id,
                InstrumentId = instrumentIds[disclosure.Symbol],
                DisclosureType = disclosure.DisclosureType,
                PublishDate = disclosure.PublishDate,
                Title = disclosure.Title,
                SourceUrl = disclosure.SourceUrl,
                SourceRef = $"{SourceRef}:kap-disclosures",
                RecordedAt = At(set.Anchor),
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task SeedMacroValuesAsync(
        DegerliDbContext db,
        FixtureSet set,
        CancellationToken ct)
    {
        var existing = (await db.MacroValues
                .Select(v => new { v.SeriesCode, v.ValueDate, v.RecordedAt })
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .Select(v => (v.SeriesCode, v.ValueDate, v.RecordedAt))
            .ToHashSet();

        foreach (var value in set.MacroValues)
        {
            if (!existing.Add((value.SeriesCode, value.ValueDate, value.RecordedAt)))
            {
                continue;
            }

            db.MacroValues.Add(new MacroValue
            {
                SeriesCode = value.SeriesCode,
                ValueDate = value.ValueDate,
                Value = value.Value,
                SourceRef = $"{SourceRef}:macro",
                RecordedAt = value.RecordedAt,
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task SeedFundsAsync(
        DegerliDbContext db,
        FixtureSet set,
        CancellationToken ct)
    {
        var existingFunds = (await db.Funds.Select(f => f.Code).ToListAsync(ct).ConfigureAwait(false))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var fund in set.Funds.Where(f => !existingFunds.Contains(f.Code)))
        {
            db.Funds.Add(new Fund
            {
                Code = fund.Code,
                Name = fund.Name,
                FundType = fund.FundType,
                Status = "active",
                SourceRef = $"{SourceRef}:funds",
                RecordedAt = At(set.Anchor),
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var existingNavs = (await db.FundNavs
                .Select(n => new { n.FundId, n.NavDate })
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .Select(n => (n.FundId, n.NavDate))
            .ToHashSet();
        foreach (var nav in set.FundNavs)
        {
            if (!existingNavs.Add((nav.FundCode, nav.NavDate)))
            {
                continue;
            }

            db.FundNavs.Add(new FundNav
            {
                FundId = nav.FundCode,
                NavDate = nav.NavDate,
                NavValue = nav.NavValue,
                SourceRef = $"{SourceRef}:fund-navs",
                RecordedAt = At(set.Anchor),
            });
        }

        var existingPerf = (await db.FundPerformances
                .Select(p => new { p.FundId, p.Period, p.AsOfDate })
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .Select(p => (p.FundId, p.Period, p.AsOfDate))
            .ToHashSet();
        foreach (var performance in set.FundPerformances)
        {
            if (!existingPerf.Add((performance.FundCode, performance.Period, performance.AsOfDate)))
            {
                continue;
            }

            db.FundPerformances.Add(new FundPerformance
            {
                FundId = performance.FundCode,
                Period = performance.Period,
                AsOfDate = performance.AsOfDate,
                ReturnValue = performance.ReturnValue,
                SourceRef = $"{SourceRef}:fund-performances",
                RecordedAt = At(set.Anchor),
            });
        }

        var instrumentIds = await db.Instruments.ToDictionaryAsync(i => i.Symbol, i => i.Id, ct).ConfigureAwait(false);
        var existingHoldings = (await db.FundHoldings
                .Select(h => new { h.FundId, h.AsOfDate, h.LineNo })
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .Select(h => (h.FundId, h.AsOfDate, h.LineNo))
            .ToHashSet();
        foreach (var holding in set.FundHoldings)
        {
            if (!existingHoldings.Add((holding.FundCode, holding.AsOfDate, holding.LineNo)))
            {
                continue;
            }

            db.FundHoldings.Add(new FundHolding
            {
                FundId = holding.FundCode,
                AsOfDate = holding.AsOfDate,
                LineNo = holding.LineNo,
                InstrumentId = holding.Symbol is not null && instrumentIds.TryGetValue(holding.Symbol, out var instrumentId)
                    ? instrumentId
                    : null,
                NameRaw = holding.NameRaw,
                Weight = holding.Weight,
                Units = holding.Units,
                SourceRef = $"{SourceRef}:fund-holdings",
                RecordedAt = At(set.Anchor),
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task SeedAccountsAsync(
        DegerliDbContext db,
        FixtureSet set,
        CancellationToken ct)
    {
        var requiredRoles = set.Accounts.Select(a => a.Role).Distinct(StringComparer.Ordinal).ToArray();
        var roles = await db.Roles.ToDictionaryAsync(r => r.NormalizedName!, r => r, ct).ConfigureAwait(false);
        foreach (var roleName in requiredRoles)
        {
            var normalized = roleName.ToUpperInvariant();
            if (roles.ContainsKey(normalized))
            {
                continue;
            }

            var role = new ApplicationRole(roleName) { NormalizedName = normalized };
            db.Roles.Add(role);
            roles[normalized] = role;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var hasher = new PasswordHasher<ApplicationUser>();
        var existingUsers = await db.Users
            .ToDictionaryAsync(u => u.NormalizedEmail!, u => u, ct)
            .ConfigureAwait(false);

        foreach (var account in set.Accounts)
        {
            var normalized = account.Email.ToUpperInvariant();
            if (existingUsers.ContainsKey(normalized))
            {
                continue;
            }

            var user = new ApplicationUser
            {
                UserName = account.Email,
                NormalizedUserName = normalized,
                Email = account.Email,
                NormalizedEmail = normalized,
                EmailConfirmed = account.Verified,
                LanguagePref = account.Language,
                SecurityStamp = Guid.NewGuid().ToString(),
                ConcurrencyStamp = Guid.NewGuid().ToString(),
                LockoutEnabled = true,
            };
            user.PasswordHash = hasher.HashPassword(user, account.Password);
            db.Users.Add(user);
            existingUsers[normalized] = user;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Reload to obtain generated ids for the newly added users/roles.
        var userIds = await db.Users.ToDictionaryAsync(u => u.NormalizedEmail!, u => u.Id, ct).ConfigureAwait(false);
        var roleIds = await db.Roles.ToDictionaryAsync(r => r.NormalizedName!, r => r.Id, ct).ConfigureAwait(false);
        var existingLinks = (await db.UserRoles
                .Select(ur => new { ur.UserId, ur.RoleId })
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .Select(ur => (ur.UserId, ur.RoleId))
            .ToHashSet();

        foreach (var account in set.Accounts)
        {
            var userId = userIds[account.Email.ToUpperInvariant()];
            var roleId = roleIds[account.Role.ToUpperInvariant()];
            if (!existingLinks.Add((userId, roleId)))
            {
                continue;
            }

            db.UserRoles.Add(new IdentityUserRole<long> { UserId = userId, RoleId = roleId });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task SeedUserContentAsync(
        DegerliDbContext db,
        FixtureSet set,
        CancellationToken ct)
    {
        var userIds = await db.Users.ToDictionaryAsync(u => u.NormalizedEmail!, u => u.Id, ct).ConfigureAwait(false);
        var instrumentIds = await db.Instruments.ToDictionaryAsync(i => i.Symbol, i => i.Id, ct).ConfigureAwait(false);
        var timestamp = At(set.Anchor);

        var existingScreens = (await db.SavedScreens
                .Select(s => new { s.UserId, s.Name })
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .Select(s => (s.UserId, s.Name))
            .ToHashSet();
        foreach (var screen in set.SavedScreens)
        {
            var userId = userIds[screen.UserEmail.ToUpperInvariant()];
            if (!existingScreens.Add((userId, screen.Name)))
            {
                continue;
            }

            db.SavedScreens.Add(new SavedScreen
            {
                UserId = userId,
                Name = screen.Name,
                CriteriaJson = screen.CriteriaJson,
                CreatedAt = timestamp,
                UpdatedAt = timestamp,
            });
        }

        var existingScenarios = (await db.DcfScenarios
                .Select(s => new { s.UserId, s.InstrumentId, s.Name })
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .Select(s => (s.UserId, s.InstrumentId, s.Name))
            .ToHashSet();
        foreach (var scenario in set.Scenarios)
        {
            var userId = userIds[scenario.UserEmail.ToUpperInvariant()];
            var instrumentId = instrumentIds[scenario.Symbol];
            if (!existingScenarios.Add((userId, instrumentId, scenario.Name)))
            {
                continue;
            }

            db.DcfScenarios.Add(new DcfScenario
            {
                UserId = userId,
                InstrumentId = instrumentId,
                Name = scenario.Name,
                ParamsJson = scenario.ParamsJson,
                CreatedAt = timestamp,
                UpdatedAt = timestamp,
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task SeedDescriptionsAsync(
        DegerliDbContext db,
        FixtureSet set,
        CancellationToken ct)
    {
        var instrumentIds = await db.Instruments.ToDictionaryAsync(i => i.Symbol, i => i.Id, ct).ConfigureAwait(false);

        var existing = (await db.BusinessDescriptions
                .Select(d => new { d.InstrumentId, d.Version })
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .Select(d => (d.InstrumentId, d.Version))
            .ToHashSet();

        foreach (var description in set.BusinessDescriptions)
        {
            var instrumentId = instrumentIds[description.Symbol];
            if (!existing.Add((instrumentId, description.Version)))
            {
                continue;
            }

            db.BusinessDescriptions.Add(new BusinessDescription
            {
                InstrumentId = instrumentId,
                Version = description.Version,
                Status = description.Status,
                TextTr = description.TextTr,
                TextEn = description.TextEn,
                SourceRefsJson = System.Text.Json.JsonSerializer.Serialize(description.SourceRefs),
                LastReviewedAt = description.LastReviewedAt,
                PublishedAt = description.PublishedAt,
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task SeedCoverageAsync(
        DegerliDbContext db,
        FixtureSet set,
        CancellationToken ct)
    {
        var instrumentIds = await db.Instruments.ToDictionaryAsync(i => i.Symbol, i => i.Id, ct).ConfigureAwait(false);

        var existing = (await db.CoverageMetadata
                .Select(c => new { c.Scope, c.InstrumentId, c.FundId, c.DataType })
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .Select(c => (c.Scope, c.InstrumentId, c.FundId, c.DataType))
            .ToHashSet();

        foreach (var coverage in set.Coverage)
        {
            long? instrumentId = coverage.Symbol is not null ? instrumentIds[coverage.Symbol] : null;
            if (!existing.Add((coverage.Scope, instrumentId, coverage.FundCode, coverage.DataType)))
            {
                continue;
            }

            db.CoverageMetadata.Add(new CoverageMetadata
            {
                Scope = coverage.Scope,
                InstrumentId = instrumentId,
                FundId = coverage.FundCode,
                DataType = coverage.DataType,
                AvailableFrom = coverage.AvailableFrom,
                AvailableTo = coverage.AvailableTo,
                Notes = coverage.Notes,
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static string Key(long instrumentId, string periodType, DateOnly periodEnd, string statementType, string version) =>
        $"{instrumentId}|{periodType}|{periodEnd:yyyy-MM-dd}|{statementType}|{version}";
}

import { useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useParams } from 'react-router'
import { useQuery } from '@tanstack/react-query'

import { buttonVariants } from '@/components/ui/button'
import { SectionNav } from '@/components/ui/section-nav'
import { useDraftStore, type StockPageSection } from '@/store/draftStore'
import { fetchStockOverview, stockPageQueryKeys } from '@/screens/stock-page/api'
import { StockPriceHeader } from '@/screens/stock-page/StockPriceHeader'
import {
  BalanceSheetSection,
  DividendsSection,
  FinancialsSection,
  GrowthSection,
  KapReportsSection,
  OverviewSection,
  ProfitabilitySection,
} from '@/screens/stock-page/StockSections'
import { ValuationSection } from '@/screens/stock-page/ValuationSection'

/** The eight sections in presentation order; labels are resolved from the catalogs. */
const SECTION_ORDER: StockPageSection[] = [
  'overview',
  'valuation',
  'financials',
  'profitability',
  'growth',
  'balanceSheet',
  'dividends',
  'kapReports',
]

const SECTION_TITLE_KEYS: Record<StockPageSection, string> = {
  overview: 'stockPage.sections.overview',
  valuation: 'stockPage.sections.valuation',
  financials: 'stockPage.sections.financials',
  profitability: 'stockPage.sections.profitability',
  growth: 'stockPage.sections.growth',
  balanceSheet: 'stockPage.sections.balanceSheet',
  dividends: 'stockPage.sections.dividends',
  kapReports: 'stockPage.sections.kapReports',
}

/**
 * The stock page's identity anchor (TKT-foundation-057(ii); SCR-005 header).
 *
 * With several tabs open the page must say which stock it belongs to. The identity
 * comes from the overview payload the page already loads — the same query key, so
 * this costs no extra request — rendered as `symbol — official name` (`Alfa Teknoloji
 * A.Ş.`-style, language-neutral, so both catalogs show the same identity).
 *
 * Honest degradation: while the payload is loading or failed, or when it carries no
 * name, only the symbol is shown — the header never invents a company name.
 */
function StockIdentity({ symbol }: { symbol: string }) {
  const { t } = useTranslation()
  const query = useQuery({
    queryKey: stockPageQueryKeys.overview(symbol),
    queryFn: ({ signal }) => fetchStockOverview(symbol, signal),
  })

  const identity = query.data
  const resolvedSymbol = identity?.symbol?.trim() ? identity.symbol : symbol
  const name = identity?.name?.trim() ? identity.name : null

  return (
    <p
      data-testid="stock-page-identity"
      className="text-3xl font-semibold tracking-tight text-foreground sm:text-4xl"
    >
      {name
        ? t('stockPage.identity.withName', { symbol: resolvedSymbol, name })
        : resolvedSymbol}
    </p>
  )
}

/**
 * SCR-005 — Stock Page (UX `stock-research.md` §SCR-005; FR-RES-005..012/014..016/026/027).
 *
 * One screen hosting the eight content sections as discrete navigable units, with
 * the current section indicated and preserved across in-session navigation
 * (UXR-RES-008, UXR-G-021). Every section is its own endpoint and query (M-5), so
 * sections load, fail and retry independently (UXR-G-003); the active section lives
 * in the M-3 draft store. The Valuation section's UI (TKT-res-009) and the DCF
 * hand-off (`/stocks/{symbol}/dcf`) complete the screen.
 */
export function StockPage() {
  const { t } = useTranslation()
  const { symbol = '' } = useParams()
  const activeSection = useDraftStore((state) => state.activeStockSection)
  const setActiveSection = useDraftStore((state) => state.setActiveStockSection)
  const resolvedActiveSection: StockPageSection = SECTION_ORDER.includes(activeSection)
    ? activeSection
    : 'overview'
  const pendingScrollSectionRef = useRef<StockPageSection | null>(null)

  useEffect(() => {
    if (pendingScrollSectionRef.current === resolvedActiveSection) {
      pendingScrollSectionRef.current = null
      if (typeof document !== 'undefined') {
        document
          .getElementById(`stock-section-${resolvedActiveSection}`)
          ?.scrollIntoView({ behavior: 'smooth', block: 'start' })
      }
    }
  }, [resolvedActiveSection])

  const sectionItems = SECTION_ORDER.map((id, idx) => ({
    id,
    index: String(idx + 1).padStart(2, '0'),
    label: t(SECTION_TITLE_KEYS[id]),
  }))

  const handleSelect = (id: string) => {
    const section: StockPageSection = SECTION_ORDER.includes(id as StockPageSection)
      ? (id as StockPageSection)
      : 'overview'
    if (section === resolvedActiveSection) {
      if (typeof document !== 'undefined') {
        document
          .getElementById(`stock-section-${section}`)
          ?.scrollIntoView({ behavior: 'smooth', block: 'start' })
      }
      return
    }
    pendingScrollSectionRef.current = section
    setActiveSection(section)
  }

  const renderActiveSection = () => {
    switch (resolvedActiveSection) {
      case 'overview':
        return (
          <OverviewSection
            symbol={symbol}
            onOpenKapReports={() => handleSelect('kapReports')}
          />
        )
      case 'valuation':
        return <ValuationSection symbol={symbol} />
      case 'financials':
        return (
          <FinancialsSection
            symbol={symbol}
            onOpenKapReports={() => handleSelect('kapReports')}
          />
        )
      case 'profitability':
        return <ProfitabilitySection symbol={symbol} />
      case 'growth':
        return <GrowthSection symbol={symbol} />
      case 'balanceSheet':
        return <BalanceSheetSection symbol={symbol} />
      case 'dividends':
        return <DividendsSection symbol={symbol} />
      case 'kapReports':
        return <KapReportsSection symbol={symbol} />
    }
  }

  return (
    <section
      data-screen="SCR-005"
      data-route-id="stockPage"
      className="flex flex-col gap-6"
    >
      <header className="flex flex-col gap-5 border-b border-[var(--border-strong)] pb-6">
        {/* The screen title stays the single level-1 heading; the company identity
            below it carries the visual weight (editorial stock hero). */}
        <h1 className="font-mono text-[11px] font-medium uppercase tracking-[0.08em] text-muted-foreground">
          {t('screens.stockPage.title')}
        </h1>
        <div className="flex flex-col gap-5 lg:flex-row lg:items-end lg:justify-between">
          <div className="flex min-w-0 flex-col gap-3">
            {/*
              The identity anchor (TKT-foundation-057) carries the stock's movement
              beside it (TKT-foundation-055(b)): symbol + name + the day's served
              price/change read as one glanceable unit, ahead of the description.
            */}
            <div
              data-testid="stock-page-identity-anchor"
              className="flex flex-col gap-2"
            >
              <StockIdentity symbol={symbol} />
              <StockPriceHeader symbol={symbol} />
            </div>
            <p className="max-w-2xl text-sm text-muted-foreground">
              {t('screens.stockPage.description')}
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <Link
              to="/stocks"
              className={buttonVariants({ variant: 'outline', size: 'sm' })}
            >
              {t('stockPage.backToStockList')}
            </Link>
            <Link
              to={`/stocks/${encodeURIComponent(symbol)}/dcf`}
              className={buttonVariants({ size: 'sm' })}
            >
              {t('stockPage.dcfHandoff')}
            </Link>
          </div>
        </div>
      </header>

      <div className="grid grid-cols-1 items-start gap-8 lg:grid-cols-[200px_1fr] lg:gap-12">
        <SectionNav
          items={sectionItems}
          activeId={resolvedActiveSection}
          onSelect={handleSelect}
          ariaLabel={t('stockPage.sectionsLabel')}
          orientation="rail"
          className="lg:sticky lg:top-[76px]"
        />

        <div className="flex min-w-0 flex-col gap-8">{renderActiveSection()}</div>
      </div>
    </section>
  )
}

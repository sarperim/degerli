import { useTranslation } from 'react-i18next'
import { Link, useParams } from 'react-router'

import { SectionNav } from '@/components/ui/section-nav'
import { useDraftStore, type StockPageSection } from '@/store/draftStore'
import { StockPriceHeader } from '@/screens/stock-page/StockPriceHeader'
import {
  BalanceSheetSection,
  DividendsSection,
  FinancialsSection,
  GrowthSection,
  OverviewSection,
  ProfitabilitySection,
} from '@/screens/stock-page/StockSections'
import { ValuationSection } from '@/screens/stock-page/ValuationSection'

/** The seven sections in presentation order; labels are resolved from the catalogs. */
const SECTION_ORDER: StockPageSection[] = [
  'overview',
  'valuation',
  'financials',
  'profitability',
  'growth',
  'balanceSheet',
  'dividends',
]

const SECTION_TITLE_KEYS: Record<StockPageSection, string> = {
  overview: 'stockPage.sections.overview',
  valuation: 'stockPage.sections.valuation',
  financials: 'stockPage.sections.financials',
  profitability: 'stockPage.sections.profitability',
  growth: 'stockPage.sections.growth',
  balanceSheet: 'stockPage.sections.balanceSheet',
  dividends: 'stockPage.sections.dividends',
}

/**
 * SCR-005 — Stock Page (UX `stock-research.md` §SCR-005; FR-RES-005..012/014..016/026/027).
 *
 * One screen hosting the seven content sections as discrete navigable units, with
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

  const sectionItems = SECTION_ORDER.map((id) => ({
    id,
    label: t(SECTION_TITLE_KEYS[id]),
  }))

  const handleSelect = (id: string) => {
    const section = id as StockPageSection
    setActiveSection(section)
    if (typeof document !== 'undefined') {
      document
        .getElementById(`stock-section-${id}`)
        ?.scrollIntoView({ behavior: 'smooth', block: 'start' })
    }
  }

  return (
    <section
      data-screen="SCR-005"
      data-route-id="stockPage"
      className="flex flex-col gap-6"
    >
      <header className="flex flex-col gap-2">
        <h1 className="text-h1 font-semibold tracking-tight">
          {t('screens.stockPage.title')}
        </h1>
        <p className="max-w-2xl text-muted-foreground">
          {t('screens.stockPage.description')}
        </p>
        <StockPriceHeader symbol={symbol} />
        <div>
          <Link
            to={`/stocks/${encodeURIComponent(symbol)}/dcf`}
            className="text-sm font-medium text-primary underline-offset-4 hover:underline focus-visible:underline"
          >
            {t('stockPage.dcfHandoff')}
          </Link>
        </div>
      </header>

      <SectionNav
        items={sectionItems}
        activeId={activeSection}
        onSelect={handleSelect}
        ariaLabel={t('stockPage.sectionsLabel')}
      />

      <div className="flex flex-col gap-6">
        <OverviewSection symbol={symbol} />
        <ValuationSection symbol={symbol} />
        <FinancialsSection symbol={symbol} />
        <ProfitabilitySection symbol={symbol} />
        <GrowthSection symbol={symbol} />
        <BalanceSheetSection symbol={symbol} />
        <DividendsSection symbol={symbol} />
      </div>
    </section>
  )
}

import { useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Inbox } from 'lucide-react'

import { Disclaimer } from '@/components/Disclaimer'
import {
  AdjustedNotice,
  AsOfDate,
  NotMeaningful,
  NoDataState,
  RestatedMarker,
  RestatedNotice,
  StaleBadge,
} from '@/components/honest-data'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { EmptyState } from '@/components/ui/empty-state'
import { FormField } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { SectionNav, type SectionNavItem } from '@/components/ui/section-nav'
import { Skeleton, SkeletonTable, SkeletonText } from '@/components/ui/skeleton'
import {
  Table,
  TableBody,
  TableCaption,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'

const AS_OF_SAMPLE = '2026-10-09'

const COLOR_TOKENS = [
  ['background', 'bg-background text-foreground border'],
  ['foreground', 'bg-foreground'],
  ['primary', 'bg-primary'],
  ['secondary', 'bg-secondary border'],
  ['muted', 'bg-muted border'],
  ['accent', 'bg-accent border'],
  ['destructive', 'bg-destructive'],
  ['stale', 'bg-stale'],
  ['restated', 'bg-restated'],
  ['adjusted', 'bg-adjusted'],
  ['not-meaningful', 'bg-not-meaningful border'],
  ['success', 'bg-success'],
  ['warning', 'bg-warning'],
  ['info', 'bg-info'],
] as const

const TYPE_SPECIMENS = [
  ['display', 'text-display'],
  ['h1', 'text-h1'],
  ['h2', 'text-h2'],
  ['h3', 'text-h3'],
  ['lead', 'text-lead'],
  ['body-lg', 'text-body-lg'],
  ['body', 'text-body'],
  ['caption', 'text-caption'],
] as const

const SPACING_TOKENS = [
  ['3xs', 'var(--space-3xs)'],
  ['2xs', 'var(--space-2xs)'],
  ['xs', 'var(--space-xs)'],
  ['sm', 'var(--space-sm)'],
  ['md', 'var(--space-md)'],
  ['lg', 'var(--space-lg)'],
  ['xl', 'var(--space-xl)'],
  ['2xl', 'var(--space-2xl)'],
] as const

const SECTION_ITEMS: SectionNavItem[] = [
  { id: 'overview', label: 'Overview' },
  { id: 'valuation', label: 'Valuation' },
  { id: 'financials', label: 'Financials' },
  { id: 'profitability', label: 'Profitability' },
  { id: 'growth', label: 'Growth' },
  { id: 'balanceSheet', label: 'Balance Sheet' },
  { id: 'dividends', label: 'Dividends' },
]

function Section({
  title,
  children,
}: {
  title: string
  children: ReactNode
}) {  return (
    <section className="flex flex-col gap-4 border-b pb-8">
      <h2 className="text-h2 font-semibold tracking-tight">{title}</h2>
      {children}
    </section>
  )
}

/**
 * Component index (TKT-foundation-011) at `/design-system`.
 *
 * A living index of the design-system foundations: tokens, core components, the
 * global shell surfaces, the informational-only disclaimer and the honest-data
 * marker vocabulary. It is the browser-verifiable deliverable for the ticket's
 * explicit acceptance (no TCs); it is deliberately not part of the product
 * navigation. Cards that embed components own their own showcase state.
 */
export function ComponentIndex() {
  const { t } = useTranslation()
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [activeSection, setActiveSection] = useState('overview')

  return (
    <div className="mx-auto flex max-w-content flex-col gap-8">
      <header className="flex flex-col gap-2">
        <h1 className="text-h1 font-semibold tracking-tight">
          Design system foundations
        </h1>
        <p className="text-muted-foreground">
          Tokens, core components, shell surfaces, disclaimer and honest-data
          markers — authored in-repo (TKT-foundation-011). Turkish default,
          English toggle in the global header.
        </p>
      </header>

      <Section title="Color tokens">
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-4 lg:grid-cols-7">
          {COLOR_TOKENS.map(([name, cls]) => (
            <div key={name} className="flex flex-col gap-1">
              <div className={`h-14 rounded-md ${cls}`} />
              <span className="text-caption text-muted-foreground">{name}</span>
            </div>
          ))}
        </div>
      </Section>

      <Section title="Spacing scale">
        <div className="flex flex-col gap-2">
          {SPACING_TOKENS.map(([name, value]) => (
            <div key={name} className="flex items-center gap-3">
              <span className="w-10 text-caption text-muted-foreground">
                {name}
              </span>
              <div
                className="h-3 rounded-sm bg-primary"
                style={{ width: value }}
              />
            </div>
          ))}
        </div>
      </Section>

      <Section title="Type scale (TR + EN)">
        <div className="flex flex-col gap-3">
          {TYPE_SPECIMENS.map(([name, cls]) => (
            <div key={name} className="flex flex-col gap-0.5">
              <span className="text-caption text-muted-foreground">{name}</span>
              <p className={cls}>
                Değerli — BIST değer yatırımı araştırma platformu. Şirket
                finansalları, ışık, İstanbul, Çağrı, ölçüm, üçüncü çeyrek.
              </p>
              <p className={cls} lang="en">
                Değerli — BIST value investing research platform. Company
                financials, quick brown fox, İstanbul, Çağrı, measurement.
              </p>
            </div>
          ))}
        </div>
      </Section>

      <Section title="Buttons">
        <div className="flex flex-wrap items-center gap-3">
          <Button>Default</Button>
          <Button variant="secondary">Secondary</Button>
          <Button variant="outline">Outline</Button>
          <Button variant="ghost">Ghost</Button>
          <Button variant="destructive">Destructive</Button>
          <Button variant="link">Link</Button>
          <Button disabled>Disabled</Button>
          <Button size="sm">Small</Button>
          <Button size="lg">Large</Button>
        </div>
      </Section>

      <Section title="Inputs & validation states">
        <div className="grid max-w-xl gap-4">
          <FormField id="ds-name" label="Stock name" hint="Name or ticker code.">
            {(control) => <Input {...control} placeholder="THYAO" />}
          </FormField>
          <FormField
            id="ds-invalid"
            label="E-mail"
            required
            error="Enter a valid e-mail address."
            hint="We use this only to sign you in."
          >
            {(control) => (
              <Input {...control} defaultValue="not-an-email" type="email" />
            )}
          </FormField>
          <FormField id="ds-disabled" label="Locked field">
            {(control) => <Input {...control} disabled defaultValue="read only" />}
          </FormField>
        </div>
      </Section>

      <Section title="Badges (incl. honest-data vocabulary)">
        <div className="flex flex-wrap items-center gap-2">
          <Badge>Default</Badge>
          <Badge variant="secondary">Secondary</Badge>
          <Badge variant="outline">Outline</Badge>
          <Badge variant="stale">{t('honestData.stale')}</Badge>
          <Badge variant="notMeaningful">{t('honestData.notMeaningful')}</Badge>
          <Badge variant="restated">{t('honestData.restated')}</Badge>
          <Badge variant="adjusted">{t('honestData.adjusted')}</Badge>
          <Badge variant="success">OK</Badge>
          <Badge variant="warning">Warning</Badge>
          <Badge variant="info">Info</Badge>
        </div>
      </Section>

      <Section title="Honest-data markers">
        <div className="grid gap-4">
          <div className="flex flex-wrap items-center gap-4">
            <AsOfDate asOf={AS_OF_SAMPLE} />
            <StaleBadge />
            <NotMeaningful />
            <span className="text-sm">
              P/E <RestatedMarker /> 12.4 / EV/EBITDA 7.1
            </span>
          </div>
          <RestatedNotice />
          <AdjustedNotice />
          <div className="grid gap-3 sm:grid-cols-3">
            <NoDataState state="no-data" availableFrom="2019-01-02" />
            <NoDataState state="preparing" />
            <NoDataState state="unavailable" />
          </div>
        </div>
      </Section>

      <Section title="Semantic table">
        <TableContainer>
          <Table>
            <TableCaption>
              Valuation metrics — canonical values with their data-as-of date.
            </TableCaption>
            <TableHeader>
              <TableRow>
                <TableHead>Metric</TableHead>
                <TableHead className="text-right">Value</TableHead>
                <TableHead className="text-right">Sector median</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow>
                <TableHead scope="row" className="font-medium text-foreground">
                  P/E
                </TableHead>
                <TableCell className="text-right tabular-nums">12.4</TableCell>
                <TableCell className="text-right tabular-nums">9.8</TableCell>
              </TableRow>
              <TableRow>
                <TableHead scope="row" className="font-medium text-foreground">
                  P/B
                </TableHead>
                <TableCell className="text-right tabular-nums">2.1</TableCell>
                <TableCell className="text-right tabular-nums">1.6</TableCell>
              </TableRow>
              <TableRow>
                <TableHead scope="row" className="font-medium text-foreground">
                  FCF yield
                </TableHead>
                <TableCell className="text-right">
                  <NotMeaningful />
                </TableCell>
                <TableCell className="text-right tabular-nums">4.3%</TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </TableContainer>
        <div className="flex items-center justify-between gap-3">
          <AsOfDate asOf={AS_OF_SAMPLE} />
          <StaleBadge />
        </div>
      </Section>

      <Section title="Dialogs & confirmations">
        <div className="flex flex-wrap gap-3">
          <ConfirmDialog
            open={confirmOpen}
            onOpenChange={setConfirmOpen}
            title="Delete this saved screen?"
            description="The screen “Value picks 2026” and its criteria will be permanently removed. This cannot be undone."
            confirmLabel="Delete"
            cancelLabel="Cancel"
            closeLabel={t('common.close')}
            destructive
            onConfirm={() => setConfirmOpen(false)}
            trigger={<Button variant="destructive">Delete saved screen…</Button>}
          />
        </div>
        <p className="text-sm text-muted-foreground">
          The dialog traps focus while open (Tab cycles inside), closes on Esc or
          the overlay, and restores focus to the trigger on close.
        </p>
      </Section>

      <Section title="Section navigation">
        <SectionNav
          items={SECTION_ITEMS}
          activeId={activeSection}
          onSelect={setActiveSection}
          ariaLabel="Stock page sections"
        />
        <p className="text-sm text-muted-foreground">
          Active section: {activeSection}
        </p>
      </Section>

      <Section title="Loading skeletons">
        <div className="grid gap-4 sm:grid-cols-2">
          <SkeletonText lines={4} />
          <SkeletonTable rows={4} columns={4} />
        </div>
        <div className="flex items-center gap-3">
          <Skeleton className="size-10 rounded-full" />
          <Skeleton className="h-4 w-40" />
        </div>
      </Section>

      <Section title="Empty & no-data states">
        <div className="grid gap-3 sm:grid-cols-2">
          <EmptyState
            icon={<Inbox />}
            title="No matching stocks"
            description="No stock matched the current criteria. Try relaxing a bound."
            action={<Button variant="outline">Reset criteria</Button>}
          />
          <NoDataState state="no-data" boundaryNote="Listed in 2019 — no earlier data." />
        </div>
      </Section>

      <Section title="Informational-only disclaimer">
        <Disclaimer />
      </Section>
    </div>
  )
}

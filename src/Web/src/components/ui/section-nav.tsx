import { cn } from '@/lib/utils'

export interface SectionNavItem {
  /** Stable section id, used by the owning screen to key content. */
  id: string
  /** Localized section label. */
  label: string
  /** Optional decorative rail index (`01`, `02`, ...) hidden from accessible name. */
  index?: string
}

export interface SectionNavProps {
  items: SectionNavItem[]
  /** The currently active section id. */
  activeId: string
  /** Called with the selected section id. */
  onSelect: (id: string) => void
  /** Accessible name for the navigation landmark (localized). */
  ariaLabel: string
  /** `horizontal` (default) or `rail` (vertical sticky left rail on lg+, horizontal strip below lg). */
  orientation?: 'horizontal' | 'rail'
  className?: string
}

/**
 * In-page section navigation (design-system primitive, TKT-foundation-011).
 *
 * Used by the multi-section screens (e.g. SCR-005 Stock Page's seven sections,
 * SCR-012 Admin's eight) to move between sections while preserving in-session
 * state (UXR-G-021). Rendered as buttons so it is keyboard operable and exposes
 * `aria-current` for the active section (UXR-G-026/028). At narrow widths the
 * list scrolls horizontally rather than collapsing, so every section stays
 * reachable (UXR-G-025).
 */
export function SectionNav({
  items,
  activeId,
  onSelect,
  ariaLabel,
  orientation = 'horizontal',
  className,
}: SectionNavProps) {
  const isRail = orientation === 'rail'
  return (
    <nav
      data-slot="section-nav"
      data-orientation={orientation}
      aria-label={ariaLabel}
      className={cn(
        isRail ? 'w-full border-b border-border pb-2 lg:border-b-0 lg:pb-0' : 'w-full border-b',
        className,
      )}
    >
      <ul
        className={cn(
          'flex list-none overflow-x-auto',
          isRail ? 'gap-1 lg:flex-col lg:gap-0.5 lg:overflow-visible' : '-mb-px gap-1',
        )}
      >
        {items.map((item) => {
          const active = item.id === activeId
          return (
            <li key={item.id} className="shrink-0">
              <button
                type="button"
                data-magnetic=""
                aria-current={active ? 'true' : undefined}
                onClick={() => onSelect(item.id)}
                className={cn(
                  'inline-flex cursor-pointer items-baseline gap-2.5 whitespace-nowrap font-mono text-xs transition-colors',
                  'outline-none focus-visible:ring-[2px] focus-visible:ring-ring/50',
                  isRail
                    ? cn(
                        'w-full border-b-2 px-3 py-2 text-left lg:border-b-0 lg:border-l-2 lg:px-3 lg:py-2.5',
                        active
                          ? 'border-[var(--brand-accent)] bg-[var(--accent-soft)] font-semibold text-foreground'
                          : 'border-transparent text-muted-foreground hover:text-foreground',
                      )
                    : cn(
                        'rounded-t-[2px] border-b-2 px-3 py-2 font-medium',
                        active
                          ? 'border-[var(--brand-accent)] text-foreground'
                          : 'border-transparent text-muted-foreground hover:text-foreground',
                      ),
                )}
              >
                {item.index ? (
                  <span
                    aria-hidden="true"
                    className={cn(
                      'text-[10px] tabular-nums',
                      active ? 'text-[var(--stale-foreground)]' : 'text-muted-foreground',
                    )}
                  >
                    {item.index}
                  </span>
                ) : null}
                <span>{item.label}</span>
              </button>
            </li>
          )
        })}
      </ul>
    </nav>
  )
}

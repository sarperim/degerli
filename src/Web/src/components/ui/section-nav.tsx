import { cn } from '@/lib/utils'

export interface SectionNavItem {
  /** Stable section id, used by the owning screen to key content. */
  id: string
  /** Localized section label. */
  label: string
}

export interface SectionNavProps {
  items: SectionNavItem[]
  /** The currently active section id. */
  activeId: string
  /** Called with the selected section id. */
  onSelect: (id: string) => void
  /** Accessible name for the navigation landmark (localized). */
  ariaLabel: string
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
  className,
}: SectionNavProps) {
  return (
    <nav
      data-slot="section-nav"
      aria-label={ariaLabel}
      className={cn('w-full border-b', className)}
    >
      <ul className="-mb-px flex list-none gap-1 overflow-x-auto">
        {items.map((item) => {
          const active = item.id === activeId
          return (
            <li key={item.id} className="shrink-0">
              <button
                type="button"
                aria-current={active ? 'true' : undefined}
                onClick={() => onSelect(item.id)}
                className={cn(
                  'inline-flex whitespace-nowrap rounded-t-md border-b-2 px-3 py-2 text-sm font-medium transition-colors',
                  'outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50',
                  active
                    ? 'border-primary text-foreground'
                    : 'border-transparent text-muted-foreground hover:text-foreground',
                )}
              >
                {item.label}
              </button>
            </li>
          )
        })}
      </ul>
    </nav>
  )
}

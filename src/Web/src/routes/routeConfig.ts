/**
 * Route contracts for the 12 SCR screens (architecture §3 C1; UX `00-screen-inventory.md` §2).
 *
 * This is the shared shell route table that all screen tickets wire their
 * implementations against. `titleKey`/`descriptionKey`/`navLabelKey` are
 * i18n catalog keys (see `src/i18n/catalogs`); the i18n parity script reads
 * these literals to determine which catalog keys are in use.
 */

export const SCREEN_IDS = [
  'marketOverview',
  'stockList',
  'screener',
  'savedScreens',
  'stockPage',
  'dcfCalculator',
  'register',
  'signIn',
  'passwordReset',
  'accountSettings',
  'dcfScenarios',
  'admin',
] as const

export type ScreenId = (typeof SCREEN_IDS)[number]

export interface ScreenRoute {
  id: ScreenId
  /** SCR id from the UX screen inventory. */
  screen: `SCR-${string}`
  /** React Router path pattern. */
  path: string
  /** i18n key for the screen title. */
  titleKey: string
  /** i18n key for the screen description. */
  descriptionKey: string
  /** When present, the route appears in the global primary navigation. */
  navLabelKey?: string
  /** Route requires a signed-in session. */
  requiresAuth?: boolean
  /** Route requires the `builder` role (SCR-012). */
  builderOnly?: boolean
  /**
   * Whether the route is a research surface that must show the informational-only
   * disclaimer (UXR-G-016): SCR-001..006 and SCR-011. Account screens (SCR-007..010)
   * and the builder admin surface (SCR-012) are not research surfaces.
   */
  research?: boolean
}

export const APP_ROUTES: ScreenRoute[] = [
  {
    id: 'marketOverview',
    research: true,
    screen: 'SCR-001',
    path: '/',
    titleKey: 'screens.marketOverview.title',
    descriptionKey: 'screens.marketOverview.description',
    navLabelKey: 'nav.marketOverview',
  },
  {
    id: 'stockList',
    research: true,
    screen: 'SCR-002',
    path: '/stocks',
    titleKey: 'screens.stockList.title',
    descriptionKey: 'screens.stockList.description',
    navLabelKey: 'nav.stocks',
  },
  {
    id: 'stockPage',
    research: true,
    screen: 'SCR-005',
    path: '/stocks/:symbol',
    titleKey: 'screens.stockPage.title',
    descriptionKey: 'screens.stockPage.description',
  },
  {
    id: 'dcfCalculator',
    research: true,
    screen: 'SCR-006',
    path: '/stocks/:symbol/dcf',
    titleKey: 'screens.dcfCalculator.title',
    descriptionKey: 'screens.dcfCalculator.description',
  },
  {
    id: 'screener',
    research: true,
    screen: 'SCR-003',
    path: '/screener',
    titleKey: 'screens.screener.title',
    descriptionKey: 'screens.screener.description',
    navLabelKey: 'nav.screener',
  },
  {
    id: 'savedScreens',
    research: true,
    screen: 'SCR-004',
    path: '/screens',
    titleKey: 'screens.savedScreens.title',
    descriptionKey: 'screens.savedScreens.description',
    navLabelKey: 'nav.savedScreens',
    requiresAuth: true,
  },
  {
    id: 'dcfScenarios',
    research: true,
    screen: 'SCR-011',
    path: '/scenarios',
    titleKey: 'screens.dcfScenarios.title',
    descriptionKey: 'screens.dcfScenarios.description',
    navLabelKey: 'nav.dcfScenarios',
    requiresAuth: true,
  },
  {
    id: 'register',
    screen: 'SCR-007',
    path: '/register',
    titleKey: 'screens.register.title',
    descriptionKey: 'screens.register.description',
  },
  {
    id: 'signIn',
    screen: 'SCR-008',
    path: '/sign-in',
    titleKey: 'screens.signIn.title',
    descriptionKey: 'screens.signIn.description',
  },
  {
    id: 'passwordReset',
    screen: 'SCR-009',
    path: '/reset-password',
    titleKey: 'screens.passwordReset.title',
    descriptionKey: 'screens.passwordReset.description',
  },
  {
    id: 'accountSettings',
    screen: 'SCR-010',
    path: '/settings',
    titleKey: 'screens.accountSettings.title',
    descriptionKey: 'screens.accountSettings.description',
    requiresAuth: true,
  },
  {
    id: 'admin',
    screen: 'SCR-012',
    path: '/admin',
    titleKey: 'screens.admin.title',
    descriptionKey: 'screens.admin.description',
    navLabelKey: 'nav.admin',
    requiresAuth: true,
    builderOnly: true,
  },
]

/** Research surfaces that must render the informational-only disclaimer (UXR-G-016). */
export const RESEARCH_ROUTES = APP_ROUTES.filter((route) => route.research === true)

/** Routes shown in the global primary navigation, in declaration order. */
export const NAVIGATION_ROUTES = APP_ROUTES.filter(
  (route): route is ScreenRoute & { navLabelKey: string } =>
    route.navLabelKey !== undefined,
)

/** Find the route contract for a screen id (throws on an unknown id — a defect). */
export function routeById(id: ScreenId): ScreenRoute {
  const route = APP_ROUTES.find((candidate) => candidate.id === id)
  if (!route) throw new Error(`Unknown screen id: ${id}`)
  return route
}

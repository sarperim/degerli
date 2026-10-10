import { createBrowserRouter, Navigate, RouterProvider } from 'react-router'

import { AppShell } from '@/components/AppShell'
import { ComponentIndex } from '@/components/design-system/ComponentIndex'
import { RouteError } from '@/components/RouteError'
import { APP_ROUTES, type ScreenRoute } from '@/routes/routeConfig'
import { RoutePlaceholder } from '@/routes/RoutePlaceholder'
import { StockListPage } from '@/routes/StockListPage'

/**
 * Screen tickets replace the shared placeholder with their routed implementation.
 * The route contracts in `routeConfig.ts` stay the source of truth; only the
 * element for an implemented screen changes here.
 */
function routeElement(route: ScreenRoute) {
  if (route.id === 'stockList') return <StockListPage />
  return <RoutePlaceholder route={route} />
}

const router = createBrowserRouter([
  {
    path: '/',
    element: <AppShell />,
    errorElement: <RouteError />,
    children: [
      ...APP_ROUTES.map((route) => ({
        index: route.path === '/',
        path: route.path === '/' ? undefined : route.path,
        element: routeElement(route),
        handle: { research: route.research === true },
      })),
      {
        path: 'design-system',
        element: <ComponentIndex />,
        handle: { research: false },
      },
      { path: '*', element: <Navigate to="/" replace /> },
    ],
  },
])

/** Router root. Providers (query, i18n) are mounted in `main.tsx`. */
export function App() {
  return <RouterProvider router={router} />
}

import { createBrowserRouter, Navigate, RouterProvider } from 'react-router'

import { AppShell } from '@/components/AppShell'
import { ComponentIndex } from '@/components/design-system/ComponentIndex'
import { RouteError } from '@/components/RouteError'
import { APP_ROUTES } from '@/routes/routeConfig'
import { RoutePlaceholder } from '@/routes/RoutePlaceholder'

const router = createBrowserRouter([
  {
    path: '/',
    element: <AppShell />,
    errorElement: <RouteError />,
    children: [
      ...APP_ROUTES.map((route) => ({
        index: route.path === '/',
        path: route.path === '/' ? undefined : route.path,
        element: <RoutePlaceholder route={route} />,
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

import type { ReactNode } from 'react'
import { createBrowserRouter, Navigate, RouterProvider } from 'react-router'

import { AppShell } from '@/components/AppShell'
import { ComponentIndex } from '@/components/design-system/ComponentIndex'
import { RouteError } from '@/components/RouteError'
import { AccountSettingsScreen } from '@/screens/accountSettings/AccountSettingsScreen'
import { AdminScreen } from '@/screens/admin/AdminScreen'
import { DcfCalculatorScreen } from '@/screens/dcf/DcfCalculatorScreen'
import { DcfScenariosScreen } from '@/screens/dcfScenarios/DcfScenariosScreen'
import { RegisterScreen } from '@/screens/register/RegisterScreen'
import { VerifyEmailScreen } from '@/screens/register/VerifyEmailScreen'
import { PasswordResetScreen } from '@/screens/password-reset/PasswordResetScreen'
import { SavedScreensScreen } from '@/screens/savedScreens/SavedScreensScreen'
import { SignInScreen } from '@/screens/signIn/SignInScreen'
import { ScreenerScreen } from '@/screens/screener/ScreenerScreen'
import { StockPage } from '@/screens/stock-page/StockPage'
import { APP_ROUTES, type ScreenRoute } from '@/routes/routeConfig'
import { RoutePlaceholder } from '@/routes/RoutePlaceholder'
import { StockListPage } from '@/routes/StockListPage'

/**
 * Routes with a real implementation; everything else keeps the shared
 * placeholder. The route contracts in `routeConfig.ts` stay the source of truth;
 * only the element for an implemented screen changes here.
 */
const SCREEN_ELEMENTS: Partial<Record<ScreenRoute['id'], ReactNode>> = {
  stockList: <StockListPage />,
  stockPage: <StockPage />,
  register: <RegisterScreen />,
  passwordReset: <PasswordResetScreen />,
  signIn: <SignInScreen />,
  screener: <ScreenerScreen />,
  admin: <AdminScreen />,
  dcfCalculator: <DcfCalculatorScreen />,
  dcfScenarios: <DcfScenariosScreen />,
  savedScreens: <SavedScreensScreen />,
  accountSettings: <AccountSettingsScreen />,
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
        element: SCREEN_ELEMENTS[route.id] ?? <RoutePlaceholder route={route} />,
        handle: { research: route.research === true },
      })),
      {
        // E-mail verification link landing, hosted by SCR-007 (UX `user-accounts.md`
        // §2/§3.3): the external entry point from the verification e-mail. The path
        // matches the API-built link `{AppUrl}/verify-email?token=…`
        // (`src/Api/Identity/IdentityEndpoints.cs` BuildVerificationLink).
        path: 'verify-email',
        element: <VerifyEmailScreen />,
        handle: { research: false },
      },
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

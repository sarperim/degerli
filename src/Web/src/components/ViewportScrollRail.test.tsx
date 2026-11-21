import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen } from '@testing-library/react'

import { ViewportScrollRail } from '@/components/ViewportScrollRail'
import { runA11y, formatA11yViolations } from '@/test/a11y'

const originalMatchMedia = window.matchMedia

interface MatchMediaConfig {
  fine?: boolean
  coarse?: boolean
  hover?: boolean
}

function mockMatchMedia({
  fine = true,
  coarse = false,
  hover = true,
}: MatchMediaConfig = {}) {
  const matchMediaMock = vi.fn((query: string): MediaQueryList => {
    let matches = false
    if (query === '(hover: hover) and (pointer: fine)') matches = fine && hover
    else if (query === '(pointer: coarse)') matches = coarse
    else if (query === '(pointer: fine)') matches = fine
    else if (query === '(hover: hover)') matches = hover

    return {
      matches,
      media: query,
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(() => false),
    }
  })

  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    writable: true,
    value: matchMediaMock,
  })
}

function setDocumentScrollHeight(height: number) {
  Object.defineProperty(document.documentElement, 'scrollHeight', {
    configurable: true,
    get: () => height,
  })
}

function setViewport({
  width = 1000,
  height = 800,
  scrollY = 0,
}: {
  width?: number
  height?: number
  scrollY?: number
} = {}) {
  Object.defineProperty(window, 'innerWidth', { configurable: true, get: () => width })
  Object.defineProperty(window, 'innerHeight', { configurable: true, get: () => height })
  Object.defineProperty(window, 'scrollY', { configurable: true, get: () => scrollY })
}

function mockTrackRect(element: HTMLElement, top = 100, height = 200) {
  element.getBoundingClientRect = () =>
    ({
      top,
      height,
      bottom: top + height,
      left: 0,
      right: 2,
      width: 2,
      x: 0,
      y: top,
      toJSON: () => ({}),
    }) as DOMRect
}

async function flushFrames() {
  await act(async () => {
    await new Promise<void>((resolve) => {
      window.requestAnimationFrame(() => resolve())
    })
  })
}

async function waitForIdleFade() {
  await act(async () => {
    await new Promise<void>((resolve) => {
      window.setTimeout(resolve, 1150)
    })
  })
}

/**
 * jsdom has no `PointerEvent` constructor, so Testing Library's pointer helpers
 * fall back to a bare `Event` that drops `clientX`/`clientY`. Dispatch a
 * `MouseEvent` with a pointer event type instead: listeners receive the same
 * coordinate/shape fields they would in a browser.
 */
function firePointer(
  target: Window | Element,
  type: string,
  init: MouseEventInit = {},
) {
  fireEvent(
    target,
    new MouseEvent(type, { bubbles: true, cancelable: true, ...init }),
  )
}

beforeEach(() => {
  mockMatchMedia()
  setViewport()
  setDocumentScrollHeight(800)
  vi.spyOn(window, 'scrollTo').mockImplementation(() => {})
})

afterEach(() => {
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    writable: true,
    value: originalMatchMedia,
  })
  delete (document.documentElement as unknown as Record<string, unknown>).scrollHeight
  delete (window as unknown as Record<string, unknown>).innerWidth
  delete (window as unknown as Record<string, unknown>).innerHeight
  delete (window as unknown as Record<string, unknown>).scrollY
  document.body.classList.remove('scroll-rail-dragging')
  document.documentElement.classList.remove('scroll-rail-active')
  vi.restoreAllMocks()
})

describe('ViewportScrollRail accessibility', () => {
  it('exposes scrollbar semantics wired to the app main landmark', () => {
    render(
      <main id="app-main">
        <ViewportScrollRail />
      </main>,
    )

    const rail = screen.getByTestId('viewport-scroll-rail')
    expect(rail).toHaveAttribute('role', 'scrollbar')
    expect(rail).toHaveAttribute('id', 'viewport-scroll-rail')
    expect(rail).toHaveAttribute('aria-controls', 'app-main')
    expect(rail).toHaveAttribute('aria-label', 'Page scroll')
    expect(rail).toHaveAttribute('aria-orientation', 'vertical')
    expect(rail).toHaveAttribute('aria-valuemin', '0')
    expect(rail).toHaveAttribute('aria-valuemax', '100')
    expect(rail).toHaveAttribute('aria-valuenow', '0')
  })

  it('passes axe-core inside the app-main landmark with zero violations', async () => {
    const { container } = render(
      <main id="app-main">
        <ViewportScrollRail />
      </main>,
    )

    const main = container.querySelector('main')
    expect(main).not.toBeNull()
    const violations = await runA11y(main as HTMLElement)
    expect(formatA11yViolations(violations)).toBe('')
    expect(violations).toHaveLength(0)
  })
})

describe('ViewportScrollRail overflow and progress', () => {
  it('stays hidden when the document does not overflow', async () => {
    setViewport({ height: 800 })
    setDocumentScrollHeight(800)
    render(<ViewportScrollRail />)

    await flushFrames()

    const rail = screen.getByTestId('viewport-scroll-rail')
    expect(rail).toHaveAttribute('data-overflow', 'false')
    expect(rail).not.toHaveClass('is-visible')

    // A pointer near the right edge plus a scroll event must not reveal the
    // rail on a page with nothing to scroll.
    firePointer(window, 'pointermove', { clientX: 990, clientY: 400 })
    fireEvent.scroll(window)
    await flushFrames()

    expect(rail).not.toHaveClass('is-visible')
    expect(rail).toHaveAttribute('data-overflow', 'false')
  })

  it('drives fill/tick/readout with GPU transforms and reveals on scroll', async () => {
    setViewport({ height: 800, scrollY: 800 })
    setDocumentScrollHeight(2400)
    const { container } = render(<ViewportScrollRail />)
    const track = container.querySelector<HTMLElement>('.viewport-scroll-rail__track')
    expect(track).not.toBeNull()
    mockTrackRect(track as HTMLElement)

    await flushFrames()
    fireEvent.scroll(window)
    await flushFrames()

    const rail = screen.getByTestId('viewport-scroll-rail')
    const fill = screen.getByTestId('scroll-rail-fill')
    const tick = screen.getByTestId('scroll-rail-tick')
    const readout = screen.getByTestId('scroll-rail-readout')

    // maxScroll = 1600, scrollY = 800 => 50%
    expect(rail).toHaveAttribute('data-overflow', 'true')
    expect(rail).toHaveClass('is-visible')
    expect(rail).toHaveAttribute('aria-valuenow', '50')
    expect(fill.style.transform).toBe('scaleY(0.5)')
    expect(tick.style.transform).toBe('translate3d(0, 100px, 0)')
    expect(readout.style.transform).toBe('translate3d(0, 100px, 0) translateY(-50%)')
    expect(readout.textContent).toBe('50%')
    expect(readout).toHaveAttribute('aria-hidden', 'true')
  })

  it('fades out after 1000ms of idle scrolling with the pointer away', async () => {
    setViewport({ height: 800, scrollY: 100 })
    setDocumentScrollHeight(2400)
    render(<ViewportScrollRail />)

    await flushFrames()
    fireEvent.scroll(window)
    await flushFrames()

    const rail = screen.getByTestId('viewport-scroll-rail')
    expect(rail).toHaveClass('is-visible')

    await waitForIdleFade()
    expect(rail).not.toHaveClass('is-visible')
  })

  it('recalculates overflow on window resize for dynamic SPA heights', async () => {
    setViewport({ height: 800 })
    setDocumentScrollHeight(800)
    render(<ViewportScrollRail />)

    await flushFrames()
    expect(screen.getByTestId('viewport-scroll-rail')).toHaveAttribute(
      'data-overflow',
      'false',
    )

    setDocumentScrollHeight(2400)
    fireEvent.resize(window)
    await flushFrames()
    expect(screen.getByTestId('viewport-scroll-rail')).toHaveAttribute(
      'data-overflow',
      'true',
    )
  })

  it('resets aria-valuenow and visual state when overflow disappears on resize', async () => {
    setViewport({ height: 800, scrollY: 800 })
    setDocumentScrollHeight(2400)
    const { container } = render(<ViewportScrollRail />)
    const track = container.querySelector<HTMLElement>('.viewport-scroll-rail__track')
    mockTrackRect(track as HTMLElement)

    await flushFrames()
    fireEvent.scroll(window)
    await flushFrames()

    const rail = screen.getByTestId('viewport-scroll-rail')
    expect(rail).toHaveAttribute('aria-valuenow', '50')
    expect(screen.getByTestId('scroll-rail-fill').style.transform).toBe('scaleY(0.5)')

    setDocumentScrollHeight(800)
    fireEvent.resize(window)
    await flushFrames()

    expect(rail).toHaveAttribute('data-overflow', 'false')
    expect(rail).toHaveAttribute('aria-valuenow', '0')
    expect(screen.getByTestId('scroll-rail-fill').style.transform).toBe('scaleY(0)')
    expect(screen.getByTestId('scroll-rail-tick').style.transform).toBe(
      'translate3d(0, 0, 0)',
    )
    expect(screen.getByTestId('scroll-rail-readout').style.transform).toBe(
      'translate3d(0, 0, 0) translateY(-50%)',
    )
    expect(screen.getByTestId('scroll-rail-readout').textContent).toBe('0%')
  })
})

describe('ViewportScrollRail right-edge proximity', () => {
  it('reveals within 48px of the right edge and fades when moving away', async () => {
    setViewport({ width: 1000, height: 800, scrollY: 0 })
    setDocumentScrollHeight(2400)
    render(<ViewportScrollRail />)

    await flushFrames()
    const rail = screen.getByTestId('viewport-scroll-rail')
    expect(rail).not.toHaveClass('is-visible')

    firePointer(window, 'pointermove', { clientX: 980, clientY: 400 })
    expect(rail).toHaveClass('is-visible')

    firePointer(window, 'pointermove', { clientX: 200, clientY: 400 })
    await waitForIdleFade()
    expect(rail).not.toHaveClass('is-visible')
  })
})

describe('ViewportScrollRail click and drag scrubbing', () => {
  it('jumps on pointerdown, scrubs 1:1, and cleans up on pointerup', async () => {
    setViewport({ height: 800, scrollY: 0 })
    setDocumentScrollHeight(2400)
    const { container } = render(<ViewportScrollRail />)
    const track = container.querySelector<HTMLElement>('.viewport-scroll-rail__track')
    mockTrackRect(track as HTMLElement)

    await flushFrames()
    const rail = screen.getByTestId('viewport-scroll-rail')
    expect(rail).toHaveAttribute('data-overflow', 'true')

    firePointer(rail, 'pointerdown', { button: 0, clientX: 990, clientY: 200 })
    // rect.top 100, height 200, clientY 200 => ratio 0.5 * maxScroll 1600 = 800
    expect(window.scrollTo).toHaveBeenLastCalledWith({ top: 800, behavior: 'auto' })
    expect(rail).toHaveClass('is-dragging')
    expect(document.body).toHaveClass('scroll-rail-dragging')
    expect(rail).toHaveClass('is-visible')

    setViewport({ height: 800, scrollY: 800 })
    await flushFrames()
    expect(screen.getByTestId('scroll-rail-readout').textContent).toBe('50%')

    firePointer(rail, 'pointermove', { clientX: 990, clientY: 300 })
    expect(window.scrollTo).toHaveBeenLastCalledWith({ top: 1600, behavior: 'auto' })

    firePointer(rail, 'pointerup', { clientX: 990 })
    expect(rail).not.toHaveClass('is-dragging')
    expect(document.body).not.toHaveClass('scroll-rail-dragging')
  })
})

describe('ViewportScrollRail pointer gating', () => {
  it('toggles html.scroll-rail-active while mounted on fine pointers', async () => {
    setViewport({ height: 800, scrollY: 0 })
    setDocumentScrollHeight(2400)
    const { unmount } = render(<ViewportScrollRail />)

    await flushFrames()
    expect(document.documentElement).toHaveClass('scroll-rail-active')

    unmount()
    expect(document.documentElement).not.toHaveClass('scroll-rail-active')
  })

  it('does not activate on coarse pointers', async () => {
    mockMatchMedia({ fine: false, coarse: true, hover: false })
    setViewport({ height: 800, scrollY: 0 })
    setDocumentScrollHeight(2400)
    render(<ViewportScrollRail />)

    await flushFrames()
    fireEvent.scroll(window)
    await flushFrames()

    const rail = screen.getByTestId('viewport-scroll-rail')
    expect(rail).toHaveAttribute('data-overflow', 'false')
    expect(rail).not.toHaveClass('is-visible')
    expect(document.documentElement).not.toHaveClass('scroll-rail-active')

    firePointer(rail, 'pointerdown', { button: 0, clientX: 990, clientY: 300 })
    expect(window.scrollTo).not.toHaveBeenCalled()
  })
})

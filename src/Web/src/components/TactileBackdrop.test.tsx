import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'

import { TactileBackdrop } from '@/components/TactileBackdrop'

interface MatchMediaConfig {
  fine?: boolean
  coarse?: boolean
  reducedMotion?: boolean
}

const originalMatchMedia = window.matchMedia

function mockMatchMedia({
  fine = true,
  coarse = false,
  reducedMotion = false,
}: MatchMediaConfig = {}) {
  const matchMediaMock = vi.fn((query: string): MediaQueryList => {
    let matches = false
    if (query === '(pointer: fine)') matches = fine
    else if (query === '(pointer: coarse)') matches = coarse
    else if (query === '(prefers-reduced-motion: reduce)') matches = reducedMotion

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

afterEach(() => {
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    writable: true,
    value: originalMatchMedia,
  })
  document.documentElement.className = ''
  vi.restoreAllMocks()
})

describe('TactileBackdrop', () => {
  it('renders cursor-reticle and cursor-dot with aria-hidden="true"', () => {
    mockMatchMedia()
    render(<TactileBackdrop />)

    expect(screen.getByTestId('cursor-reticle')).toHaveAttribute('aria-hidden', 'true')
    expect(screen.getByTestId('cursor-dot')).toHaveAttribute('aria-hidden', 'true')
  })

  it('syncs cursor-dot 1:1 on mousemove and activates cursorless mode on fine pointer', () => {
    mockMatchMedia({ fine: true, coarse: false, reducedMotion: false })
    render(<TactileBackdrop />)

    const dot = screen.getByTestId('cursor-dot')
    const reticle = screen.getByTestId('cursor-reticle')

    fireEvent.mouseMove(window, { clientX: 120, clientY: 84 })

    expect(dot.style.transform).toBe('translate3d(120px, 84px, 0) translate(-50%, -50%)')
    expect(dot).toHaveClass('visible')
    expect(reticle).toHaveClass('visible')
    expect(document.documentElement).toHaveClass('cursorless-active')
  })

  it('toggles .is-text when moving over text inputs and clears it on non-text elements', () => {
    mockMatchMedia({ fine: true, coarse: false, reducedMotion: false })
    render(
      <>
        <TactileBackdrop />
        <input type="text" aria-label="Ticker search" />
        <textarea aria-label="Notes" />
        <div data-testid="surface">Plain surface</div>
      </>,
    )

    const dot = screen.getByTestId('cursor-dot')
    const reticle = screen.getByTestId('cursor-reticle')
    const input = screen.getByLabelText('Ticker search')
    const textarea = screen.getByLabelText('Notes')
    const surface = screen.getByTestId('surface')

    fireEvent.mouseMove(input, { clientX: 40, clientY: 50 })
    expect(dot).toHaveClass('is-text')
    expect(reticle).toHaveClass('is-text')

    fireEvent.mouseMove(surface, { clientX: 60, clientY: 70 })
    expect(dot).not.toHaveClass('is-text')
    expect(reticle).not.toHaveClass('is-text')

    fireEvent.mouseMove(textarea, { clientX: 80, clientY: 90 })
    expect(dot).toHaveClass('is-text')
    expect(reticle).toHaveClass('is-text')
  })

  it('handles magnetic/interactive targets and clears cursorless state on mouseleave and unmount', () => {
    mockMatchMedia({ fine: true, coarse: false, reducedMotion: false })
    const { unmount } = render(
      <>
        <TactileBackdrop />
        <button type="button" data-magnetic="">
          Magnetic CTA
        </button>
        <button type="button">Standard CTA</button>
      </>,
    )

    const dot = screen.getByTestId('cursor-dot')
    const reticle = screen.getByTestId('cursor-reticle')
    const magneticBtn = screen.getByRole('button', { name: 'Magnetic CTA' })
    const standardBtn = screen.getByRole('button', { name: 'Standard CTA' })

    fireEvent.mouseMove(magneticBtn, { clientX: 150, clientY: 200 })
    expect(document.documentElement).toHaveClass('cursorless-active')
    expect(dot).toHaveClass('visible')
    expect(reticle).toHaveClass('visible')

    fireEvent.mouseMove(standardBtn, { clientX: 220, clientY: 240 })
    expect(document.documentElement).toHaveClass('cursorless-active')

    fireEvent.mouseLeave(document.documentElement)
    expect(document.documentElement).not.toHaveClass('cursorless-active')
    expect(dot).not.toHaveClass('visible')
    expect(reticle).not.toHaveClass('visible')

    fireEvent.mouseMove(standardBtn, { clientX: 225, clientY: 245 })
    expect(document.documentElement).toHaveClass('cursorless-active')

    unmount()
    expect(document.documentElement).not.toHaveClass('cursorless-active')
  })

  it('does not activate cursorless mode when prefers-reduced-motion is reduce', () => {
    mockMatchMedia({ fine: true, coarse: false, reducedMotion: true })
    render(<TactileBackdrop />)

    fireEvent.mouseMove(window, { clientX: 100, clientY: 100 })

    expect(document.documentElement).not.toHaveClass('cursorless-active')
    expect(screen.getByTestId('cursor-dot')).not.toHaveClass('visible')
    expect(screen.getByTestId('cursor-reticle')).not.toHaveClass('visible')
  })

  it('does not activate cursorless mode on coarse pointers', () => {
    mockMatchMedia({ fine: false, coarse: true, reducedMotion: false })
    render(<TactileBackdrop />)

    fireEvent.mouseMove(window, { clientX: 100, clientY: 100 })

    expect(document.documentElement).not.toHaveClass('cursorless-active')
    expect(screen.getByTestId('cursor-dot')).not.toHaveClass('visible')
    expect(screen.getByTestId('cursor-reticle')).not.toHaveClass('visible')
  })
})

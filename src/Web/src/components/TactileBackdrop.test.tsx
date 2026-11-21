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

  it('does not trigger .is-text on non-text inputs such as input[type="checkbox"]', () => {
    mockMatchMedia({ fine: true, coarse: false, reducedMotion: false })
    render(
      <>
        <TactileBackdrop />
        <input type="checkbox" aria-label="Include stale rows" />
      </>,
    )

    const dot = screen.getByTestId('cursor-dot')
    const reticle = screen.getByTestId('cursor-reticle')
    const checkbox = screen.getByLabelText('Include stale rows')

    fireEvent.mouseMove(checkbox, { clientX: 48, clientY: 64 })

    expect(dot).toHaveClass('visible')
    expect(reticle).toHaveClass('visible')
    expect(dot).not.toHaveClass('is-text')
    expect(reticle).not.toHaveClass('is-text')
  })

  it('clears cursorless-active, .visible, and .is-text on window blur', () => {
    mockMatchMedia({ fine: true, coarse: false, reducedMotion: false })
    render(
      <>
        <TactileBackdrop />
        <input type="text" aria-label="Search symbol" />
      </>,
    )

    const dot = screen.getByTestId('cursor-dot')
    const reticle = screen.getByTestId('cursor-reticle')
    const input = screen.getByLabelText('Search symbol')

    fireEvent.mouseMove(input, { clientX: 90, clientY: 110 })
    expect(document.documentElement).toHaveClass('cursorless-active')
    expect(dot).toHaveClass('visible', 'is-text')
    expect(reticle).toHaveClass('visible', 'is-text')

    fireEvent.blur(window)

    expect(document.documentElement).not.toHaveClass('cursorless-active')
    expect(dot).not.toHaveClass('visible')
    expect(dot).not.toHaveClass('is-text')
    expect(reticle).not.toHaveClass('visible')
    expect(reticle).not.toHaveClass('is-text')
  })

  it('animates reticle geometry for idle, interactive, pressed, magnetic, and disconnected targets', () => {
    mockMatchMedia({ fine: true, coarse: false, reducedMotion: false })
    const originalRaf = window.requestAnimationFrame
    const originalCancelRaf = window.cancelAnimationFrame
    let nextId = 1
    const pendingCallbacks = new Map<number, FrameRequestCallback>()

    window.requestAnimationFrame = vi.fn((cb: FrameRequestCallback) => {
      const id = nextId++
      pendingCallbacks.set(id, cb)
      return id
    })
    window.cancelAnimationFrame = vi.fn((id: number) => {
      pendingCallbacks.delete(id)
    })

    const flushFrames = (count: number) => {
      for (let i = 0; i < count; i += 1) {
        const batch = Array.from(pendingCallbacks.entries())
        pendingCallbacks.clear()
        for (const [, cb] of batch) {
          cb(performance.now())
        }
      }
    }

    const magneticBtn = document.createElement('button')
    magneticBtn.type = 'button'
    magneticBtn.setAttribute('data-magnetic', '')
    magneticBtn.textContent = 'Magnetic CTA'
    document.body.appendChild(magneticBtn)

    try {
      const { unmount } = render(
        <>
          <TactileBackdrop />
          <div data-testid="idle-surface">Surface</div>
          <button type="button">Standard CTA</button>
        </>,
      )

      const reticle = screen.getByTestId('cursor-reticle')
      const idleSurface = screen.getByTestId('idle-surface')
      const standardBtn = screen.getByRole('button', { name: 'Standard CTA' })

      vi.spyOn(magneticBtn, 'getBoundingClientRect').mockReturnValue({
        left: 100,
        top: 200,
        width: 120,
        height: 40,
        right: 220,
        bottom: 240,
        x: 100,
        y: 200,
        toJSON: () => ({}),
      } as DOMRect)

      // Idle: 24px x 24px, pressed -> 20px x 20px
      fireEvent.mouseMove(idleSurface, { clientX: 160, clientY: 220 })
      flushFrames(40)
      expect(reticle.style.width).toBe('24px')
      expect(reticle.style.height).toBe('24px')
      expect(reticle.style.transform).toBe('translate3d(148.0px, 208.0px, 0)')

      fireEvent.mouseDown(window)
      flushFrames(40)
      expect(reticle.style.width).toBe('20px')
      expect(reticle.style.height).toBe('20px')

      fireEvent.mouseUp(window)

      // Interactive standard button: 18px x 18px, pressed -> 16px x 16px
      fireEvent.mouseMove(standardBtn, { clientX: 160, clientY: 220 })
      flushFrames(40)
      expect(reticle.style.width).toBe('18px')
      expect(reticle.style.height).toBe('18px')

      fireEvent.mouseDown(standardBtn)
      flushFrames(40)
      expect(reticle.style.width).toBe('16px')
      expect(reticle.style.height).toBe('16px')

      fireEvent.mouseUp(standardBtn)

      // Magnetic button: bounding box + 4px pad (128x48 @ 96,196), pressed -> +2px pad (124x44 @ 98,198)
      fireEvent.mouseMove(magneticBtn, { clientX: 160, clientY: 220 })
      flushFrames(40)
      expect(reticle.style.width).toBe('128px')
      expect(reticle.style.height).toBe('48px')
      expect(reticle.style.transform).toBe('translate3d(96.0px, 196.0px, 0)')

      fireEvent.mouseDown(magneticBtn)
      flushFrames(40)
      expect(reticle.style.width).toBe('124px')
      expect(reticle.style.height).toBe('44px')
      expect(reticle.style.transform).toBe('translate3d(98.0px, 198.0px, 0)')

      fireEvent.mouseUp(magneticBtn)

      // Removing magnetic element falls back to idle pointer tracking via isConnected guard
      magneticBtn.remove()
      expect(() => flushFrames(40)).not.toThrow()
      expect(reticle.style.width).toBe('24px')
      expect(reticle.style.height).toBe('24px')
      expect(reticle.style.transform).toBe('translate3d(148.0px, 208.0px, 0)')

      unmount()
    } finally {
      magneticBtn.remove()
      window.requestAnimationFrame = originalRaf
      window.cancelAnimationFrame = originalCancelRaf
    }
  })
})

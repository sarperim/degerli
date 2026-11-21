import { useRef } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, screen } from '@testing-library/react'

import {
  HeroInteractivePlate,
  useHeroBackdropCanvas,
} from '@/screens/market-overview/HeroInteractivePlate'
import { renderWithProviders } from '@/test/render'

interface MockImageInstance {
  src: string
  naturalWidth: number
  naturalHeight: number
  width: number
  height: number
  onload: null | (() => void)
}

function createCanvas2dSpy() {
  return {
    createLinearGradient: vi.fn(() => ({ addColorStop: vi.fn() })),
    clearRect: vi.fn(),
    fillRect: vi.fn(),
    beginPath: vi.fn(),
    moveTo: vi.fn(),
    lineTo: vi.fn(),
    quadraticCurveTo: vi.fn(),
    closePath: vi.fn(),
    stroke: vi.fn(),
    fill: vi.fn(),
    setLineDash: vi.fn(),
    setTransform: vi.fn(),
    drawImage: vi.fn(),
    getImageData: vi.fn(() => ({
      data: new Uint8ClampedArray(360 * 270 * 4),
      width: 360,
      height: 270,
      colorSpace: 'srgb' as PredefinedColorSpace,
    })),
    putImageData: vi.fn(),
    fillStyle: '',
    strokeStyle: '',
    lineWidth: 1,
  }
}

function installMockImage() {
  const images: MockImageInstance[] = []
  class MockImage implements MockImageInstance {
    src = ''
    naturalWidth = 0
    naturalHeight = 0
    width = 0
    height = 0
    onload: null | (() => void) = null
    constructor() {
      images.push(this)
    }
  }
  vi.stubGlobal('Image', MockImage)
  return images
}

function BackdropHarness() {
  const heroRef = useRef<HTMLElement | null>(null)
  const canvasRef = useRef<HTMLCanvasElement | null>(null)
  useHeroBackdropCanvas({
    heroRef,
    canvasRef,
    xu100Level: 10418.6,
    asOf: '2026-11-18',
    locale: 'tr',
  })

  return (
    <header ref={heroRef} data-testid="hero-harness">
      <canvas ref={canvasRef} data-testid="hero-canvas-harness" />
    </header>
  )
}

describe('HeroInteractivePlate', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('keeps the Bayer 4x4 canvas palette dark-room duotone while the outer frame adapts via theme tokens', () => {
    const grayscale = new Uint8ClampedArray(360 * 270 * 4)
    for (let i = 0; i < grayscale.length; i += 4) {
      const lum = Math.round(((i / 4) % 360) / 359 * 255)
      grayscale[i] = lum
      grayscale[i + 1] = lum
      grayscale[i + 2] = lum
      grayscale[i + 3] = 255
    }
    const ctxSpy = createCanvas2dSpy()
    ctxSpy.getImageData.mockReturnValue({
      data: grayscale,
      width: 360,
      height: 270,
      colorSpace: 'srgb' as PredefinedColorSpace,
    })
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(
      ctxSpy as unknown as CanvasRenderingContext2D,
    )
    installMockImage()

    const { container } = renderWithProviders(<HeroInteractivePlate />)

    const plate = container.querySelector('#photo-plate')
    const viewport = container.querySelector('#photo-viewport')
    expect(plate?.className).toContain('border-[var(--border-strong)]')
    expect(plate?.className).toContain('bg-card')
    expect(plate?.className).not.toContain('bg-[#121519]')
    expect(viewport?.className).toContain('border-border')
    expect(viewport?.className).toContain('bg-background')
    expect(viewport?.className).not.toContain('bg-[#0B0D10]')

    const cornerMarks = plate?.querySelectorAll('span[aria-hidden="true"]') ?? []
    expect(cornerMarks).toHaveLength(4)
    for (const mark of cornerMarks) {
      expect(mark.className).toContain('text-muted-foreground')
    }

    const hudBadges = viewport?.querySelectorAll('span') ?? []
    expect(hudBadges).toHaveLength(2)
    expect(hudBadges[0].className).toContain('border-[var(--border-strong)]')
    expect(hudBadges[0].className).toContain('bg-background/85')
    expect(hudBadges[0].className).toContain('text-muted-foreground')
    expect(hudBadges[1].className).toContain('border-[var(--border-strong)]')
    expect(hudBadges[1].className).toContain('bg-background/85')
    expect(hudBadges[1].className).toContain('text-[var(--stale-foreground)]')

    const caption = plate?.lastElementChild
    expect(caption?.className).toContain('text-muted-foreground')

    const swapLabel = plate?.querySelector('label')
    expect(swapLabel?.className).toContain('text-[var(--stale-foreground)]')
    expect(swapLabel?.className).toContain('hover:text-foreground')

    const darkPalette = new Set([
      '17,19,22',
      '68,75,84',
      '198,139,73',
      '224,220,212',
    ])
    const usedColors = new Set<string>()
    for (let i = 0; i < grayscale.length; i += 4) {
      usedColors.add(`${grayscale[i]},${grayscale[i + 1]},${grayscale[i + 2]}`)
    }

    expect(usedColors.has('246,245,240')).toBe(false)
    for (const color of usedColors) {
      expect(darkPalette.has(color)).toBe(true)
    }
  })

  it('renders procedural skyline fallback before the archival image finishes loading', () => {
    const ctxSpy = createCanvas2dSpy()
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(
      ctxSpy as unknown as CanvasRenderingContext2D,
    )
    const images = installMockImage()

    renderWithProviders(<HeroInteractivePlate />)

    expect(images).toHaveLength(1)
    expect(images[0].src).toContain('istanbul-financial-archive')
    expect(ctxSpy.drawImage).not.toHaveBeenCalled()
    expect(ctxSpy.fillRect).toHaveBeenCalled()
    expect(ctxSpy.putImageData).toHaveBeenCalledTimes(1)
  })

  it('draws the bundled archival image with center-cover scaling once loaded', () => {
    const ctxSpy = createCanvas2dSpy()
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(
      ctxSpy as unknown as CanvasRenderingContext2D,
    )
    const images = installMockImage()

    renderWithProviders(<HeroInteractivePlate />)
    expect(images).toHaveLength(1)

    const img = images[0]
    img.naturalWidth = 960
    img.naturalHeight = 540
    img.width = 960
    img.height = 540

    act(() => {
      img.onload?.()
    })

    expect(ctxSpy.drawImage).toHaveBeenCalledTimes(1)
    expect(ctxSpy.drawImage).toHaveBeenCalledWith(img, -60, 0, 480, 270)
    expect(ctxSpy.putImageData).toHaveBeenCalledTimes(2)
  })

  it('cancels the archival image onload callback when unmounted before load completes', () => {
    const ctxSpy = createCanvas2dSpy()
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(
      ctxSpy as unknown as CanvasRenderingContext2D,
    )
    const images = installMockImage()

    const { unmount } = renderWithProviders(<HeroInteractivePlate />)
    expect(images).toHaveLength(1)

    unmount()

    const img = images[0]
    img.naturalWidth = 960
    img.naturalHeight = 540
    img.width = 960
    img.height = 540

    act(() => {
      img.onload?.()
    })

    expect(ctxSpy.drawImage).not.toHaveBeenCalled()
  })

  it('observes hero resize in useHeroBackdropCanvas and disconnects on unmount', () => {
    const ctxSpy = createCanvas2dSpy()
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(
      ctxSpy as unknown as CanvasRenderingContext2D,
    )

    let resizeCb: ResizeObserverCallback | null = null
    const observe = vi.fn()
    const disconnect = vi.fn()
    class MockResizeObserver {
      constructor(cb: ResizeObserverCallback) {
        resizeCb = cb
      }
      observe = observe
      unobserve = vi.fn()
      disconnect = disconnect
    }
    vi.stubGlobal('ResizeObserver', MockResizeObserver)

    const { unmount } = renderWithProviders(<BackdropHarness />)

    const hero = screen.getByTestId('hero-harness')
    vi.spyOn(hero, 'getBoundingClientRect').mockReturnValue({
      width: 1200,
      height: 600,
      top: 0,
      left: 0,
      right: 1200,
      bottom: 600,
      x: 0,
      y: 0,
      toJSON: () => ({}),
    } as DOMRect)

    expect(observe).toHaveBeenCalledTimes(1)
    expect(observe).toHaveBeenCalledWith(hero)

    act(() => {
      resizeCb?.([], {} as ResizeObserver)
    })

    expect(ctxSpy.setTransform).toHaveBeenCalled()
    expect(ctxSpy.clearRect).toHaveBeenCalledWith(0, 0, 1200, 600)

    unmount()
    expect(disconnect).toHaveBeenCalledTimes(1)
  })
})

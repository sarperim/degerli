import { afterEach, describe, expect, it, vi } from 'vitest'
import { act } from '@testing-library/react'

import { HeroInteractivePlate } from '@/screens/market-overview/HeroInteractivePlate'
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
    fillRect: vi.fn(),
    beginPath: vi.fn(),
    moveTo: vi.fn(),
    quadraticCurveTo: vi.fn(),
    stroke: vi.fn(),
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

describe('HeroInteractivePlate', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('renders procedural skyline fallback before the archival image finishes loading', () => {
    const ctxSpy = createCanvas2dSpy()
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(
      ctxSpy as unknown as CanvasRenderingContext2D,
    )

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
})

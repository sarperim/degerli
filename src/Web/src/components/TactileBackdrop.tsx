import { useEffect, useRef } from 'react'

import { useTheme } from '@/lib/theme'

const MAGNETIC_SELECTOR = '[data-magnetic], [data-row-link], [data-sector-bar-row]'
const INTERACTIVE_SELECTOR =
  'a, button, select, summary, label[for], [role="button"], [role="link"], [role="tab"], [role="option"], [role="menuitem"], [tabindex]:not([tabindex="-1"])'
const TEXT_INPUT_SELECTOR =
  'input, textarea, [contenteditable="true"], [contenteditable=""], [role="textbox"]'
const NON_TEXT_INPUT_TYPES = new Set([
  'button',
  'submit',
  'reset',
  'checkbox',
  'radio',
  'range',
  'color',
  'file',
  'image',
  'hidden',
])

function isTextEditableTarget(target: Element | null): boolean {
  if (!target) return false
  const el = target.closest(TEXT_INPUT_SELECTOR)
  if (!el) return false
  if (el.tagName.toUpperCase() === 'INPUT') {
    const type = (el.getAttribute('type') ?? 'text').toLowerCase()
    return !NON_TEXT_INPUT_TYPES.has(type)
  }
  return true
}

/**
 * Tactile atmospheric layers from `geminisonuiux.html` (SLICE-024 / SLICE-035 / SLICE-036):
 *
 * 1. `#global-grid-canvas`: fixed 48px engineering-paper grid canvas that scrolls
 *    with the page and illuminates bronze `+` intersection crosshairs within a
 *    165px radius around the pointer.
 * 2. `.grain-overlay`: subtle fixed SVG fractalNoise film-grain layer.
 * 3. `.cursor-dot` + `.cursor-reticle`: zero-lag 1:1 `11px` `1.5px`-stroke white
 *    `difference`-blend `+` tick (`#cursor-dot`) paired with trailing original `1.5px`
 *    `var(--brand-accent)` bronze 4-corner brackets (`#cursor-reticle`) that
 *    interpolate (`requestAnimationFrame`), magnetically snap to `[data-magnetic]`,
 *    `[data-row-link]`, and `[data-sector-bar-row]`, tighten on interactive controls,
 *    and morph into an I-beam over text inputs. Automatically disabled on coarse
 *    pointers and `prefers-reduced-motion: reduce`.
 */
export function TactileBackdrop() {
  const canvasRef = useRef<HTMLCanvasElement | null>(null)
  const reticleRef = useRef<HTMLDivElement | null>(null)
  const dotRef = useRef<HTMLDivElement | null>(null)
  const theme = useTheme()

  // 1. Global engineering-paper grid + pointer crosshair illumination
  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return
    let ctx: CanvasRenderingContext2D | null = null
    try {
      ctx = canvas.getContext('2d')
    } catch {
      ctx = null
    }
    if (!ctx) return

    const ptr = { x: -500, y: -500, active: false }
    let rafId = 0

    const drawGrid = () => {
      rafId = 0
      const st = window.getComputedStyle(document.documentElement)
      const w = window.innerWidth
      const h = window.innerHeight
      const step = 48
      const offY = -((window.scrollY || 0) % step)
      const gridLine = st.getPropertyValue('--grid-line').trim() || 'rgba(128,128,128,0.03)'
      const glow = st.getPropertyValue('--grid-glow').trim() || 'rgba(198,139,73,0.38)'

      ctx.clearRect(0, 0, w, h)
      ctx.strokeStyle = gridLine
      ctx.lineWidth = 1
      ctx.beginPath()
      for (let x = 0; x <= w; x += step) {
        ctx.moveTo(x + 0.5, 0)
        ctx.lineTo(x + 0.5, h)
      }
      for (let y = offY; y <= h; y += step) {
        ctx.moveTo(0, y + 0.5)
        ctx.lineTo(w, y + 0.5)
      }
      ctx.stroke()

      if (ptr.active) {
        const rad = 165
        for (let x = 0; x <= w; x += step) {
          if (Math.abs(x - ptr.x) > rad) continue
          for (let y = offY; y <= h; y += step) {
            const d = Math.hypot(x - ptr.x, y - ptr.y)
            if (d < rad) {
              ctx.strokeStyle = glow
              ctx.globalAlpha = (1 - d / rad) * 0.85
              ctx.beginPath()
              ctx.moveTo(x - 3, y + 0.5)
              ctx.lineTo(x + 4, y + 0.5)
              ctx.moveTo(x + 0.5, y - 3)
              ctx.lineTo(x + 0.5, y + 4)
              ctx.stroke()
            }
          }
        }
        ctx.globalAlpha = 1
      }
    }

    const scheduleDraw = () => {
      if (!rafId) {
        rafId = window.requestAnimationFrame(drawGrid)
      }
    }

    const resize = () => {
      const dpr = Math.min(window.devicePixelRatio || 1, 2)
      canvas.width = Math.floor(window.innerWidth * dpr)
      canvas.height = Math.floor(window.innerHeight * dpr)
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
      drawGrid()
    }

    const onMouseMove = (e: MouseEvent) => {
      ptr.x = e.clientX
      ptr.y = e.clientY
      ptr.active = true
      scheduleDraw()
    }

    const onMouseLeave = () => {
      ptr.active = false
      scheduleDraw()
    }

    resize()
    window.addEventListener('mousemove', onMouseMove, { passive: true })
    document.addEventListener('mouseleave', onMouseLeave)
    window.addEventListener('scroll', scheduleDraw, { passive: true })
    window.addEventListener('resize', resize)

    return () => {
      if (rafId) window.cancelAnimationFrame(rafId)
      window.removeEventListener('mousemove', onMouseMove)
      document.removeEventListener('mouseleave', onMouseLeave)
      window.removeEventListener('scroll', scheduleDraw)
      window.removeEventListener('resize', resize)
    }
  }, [theme])

  // 2. Cursorless center dot + magnetic 4-corner targeting reticle
  useEffect(() => {
    const ret = reticleRef.current
    const dot = dotRef.current
    if (!ret || !dot || typeof window.matchMedia !== 'function') return

    const isFine = window.matchMedia('(pointer: fine)').matches
    const isCoarse = window.matchMedia('(pointer: coarse)').matches
    const isReduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    if (!isFine || isCoarse || isReduced) return

    let mouseX = -100
    let mouseY = -100
    let currX = -100
    let currY = -100
    let currW = 24
    let currH = 24
    let activeEl: HTMLElement | null = null
    let interactive = false
    let isText = false
    let pressed = false
    let visible = false
    let started = false
    let rafId = 0

    const moveDot = (x: number, y: number) => {
      dot.style.transform = `translate3d(${x}px, ${y}px, 0) translate(-50%, -50%)`
    }

    const setTextMode = (nextIsText: boolean) => {
      if (isText === nextIsText) return
      isText = nextIsText
      dot.classList.toggle('is-text', nextIsText)
      ret.classList.toggle('is-text', nextIsText)
    }

    const clearCursor = () => {
      dot.classList.remove('visible', 'is-text')
      ret.classList.remove('visible', 'is-text')
      visible = false
      started = false
      activeEl = null
      interactive = false
      isText = false
      pressed = false
      document.documentElement.classList.remove('cursorless-active')
    }

    const onMouseMove = (e: MouseEvent) => {
      mouseX = e.clientX
      mouseY = e.clientY
      if (!started) {
        currX = mouseX - currW / 2
        currY = mouseY - currH / 2
        started = true
      }

      moveDot(mouseX, mouseY)

      if (!visible) {
        visible = true
        dot.classList.add('visible')
        ret.classList.add('visible')
        document.documentElement.classList.add('cursorless-active')
      }

      const targetEl = e.target instanceof Element ? e.target : null
      if (isTextEditableTarget(targetEl)) {
        setTextMode(true)
        activeEl = null
        interactive = false
      } else {
        setTextMode(false)
        activeEl = targetEl ? targetEl.closest<HTMLElement>(MAGNETIC_SELECTOR) : null
        interactive = !activeEl && Boolean(targetEl && targetEl.closest(INTERACTIVE_SELECTOR))
      }
    }

    const onMouseDown = () => {
      pressed = true
    }

    const onMouseUp = () => {
      pressed = false
    }

    const tick = () => {
      if (activeEl && !activeEl.isConnected) {
        activeEl = null
      }

      let targetW = pressed ? 20 : 24
      let targetH = pressed ? 20 : 24
      let targetX = mouseX - targetW / 2
      let targetY = mouseY - targetH / 2

      if (!isText && activeEl) {
        const pad = pressed ? 2 : 4
        const rect = activeEl.getBoundingClientRect()
        targetX = rect.left - pad
        targetY = rect.top - pad
        targetW = rect.width + pad * 2
        targetH = rect.height + pad * 2
      } else if (!isText && interactive) {
        targetW = pressed ? 16 : 18
        targetH = pressed ? 16 : 18
        targetX = mouseX - targetW / 2
        targetY = mouseY - targetH / 2
      }

      currX += (targetX - currX) * 0.24
      currY += (targetY - currY) * 0.24
      currW += (targetW - currW) * 0.24
      currH += (targetH - currH) * 0.24

      ret.style.width = `${currW.toFixed(1)}px`
      ret.style.height = `${currH.toFixed(1)}px`
      ret.style.transform = `translate3d(${currX.toFixed(1)}px, ${currY.toFixed(1)}px, 0)`

      rafId = window.requestAnimationFrame(tick)
    }

    window.addEventListener('mousemove', onMouseMove, { passive: true })
    window.addEventListener('mousedown', onMouseDown, { passive: true })
    window.addEventListener('mouseup', onMouseUp, { passive: true })
    window.addEventListener('blur', clearCursor)
    document.documentElement.addEventListener('mouseleave', clearCursor)
    rafId = window.requestAnimationFrame(tick)

    return () => {
      if (rafId) window.cancelAnimationFrame(rafId)
      window.removeEventListener('mousemove', onMouseMove)
      window.removeEventListener('mousedown', onMouseDown)
      window.removeEventListener('mouseup', onMouseUp)
      window.removeEventListener('blur', clearCursor)
      document.documentElement.removeEventListener('mouseleave', clearCursor)
      document.documentElement.classList.remove('cursorless-active')
    }
  }, [])

  return (
    <>
      <canvas
        ref={canvasRef}
        id="global-grid-canvas"
        data-testid="global-grid-canvas"
        aria-hidden="true"
      />
      <div className="grain-overlay" data-testid="grain-overlay" aria-hidden="true" />
      <div
        ref={dotRef}
        id="cursor-dot"
        data-testid="cursor-dot"
        className="cursor-dot"
        aria-hidden="true"
      />
      <div
        ref={reticleRef}
        id="cursor-reticle"
        data-testid="cursor-reticle"
        className="cursor-reticle"
        aria-hidden="true"
      >
        <span className="tl" />
        <span className="tr" />
        <span className="bl" />
        <span className="br" />
      </div>
    </>
  )
}

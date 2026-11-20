import { useEffect, useRef } from 'react'

import { useTheme } from '@/lib/theme'

/**
 * Tactile atmospheric layers from `geminisonuiux.html` (SLICE-024):
 *
 * 1. `#global-grid-canvas`: fixed 48px engineering-paper grid canvas that scrolls
 *    with the page and illuminates bronze `+` intersection crosshairs within a
 *    165px radius around the pointer.
 * 2. `.grain-overlay`: subtle fixed SVG fractalNoise film-grain layer.
 * 3. `.cursor-reticle`: 4-corner bronze targeting reticle (`tl`/`tr`/`bl`/`br`)
 *    that smoothly interpolates (`requestAnimationFrame`) to follow the pointer
 *    and magnetically expands around hovered `[data-magnetic]`, `[data-row-link]`,
 *    and `[data-sector-bar-row]` interactive elements. Automatically disabled on
 *    coarse pointers and `prefers-reduced-motion: reduce`.
 */
export function TactileBackdrop() {
  const canvasRef = useRef<HTMLCanvasElement | null>(null)
  const reticleRef = useRef<HTMLDivElement | null>(null)
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

  // 2. Magnetic 4-corner targeting reticle
  useEffect(() => {
    const ret = reticleRef.current
    if (!ret || typeof window.matchMedia !== 'function') return
    if (
      window.matchMedia('(pointer: coarse)').matches ||
      window.matchMedia('(prefers-reduced-motion: reduce)').matches
    ) {
      return
    }

    let mx = -100
    let my = -100
    let rx = -100
    let ry = -100
    let rw = 20
    let rh = 20
    let target: HTMLElement | null = null
    let rafId = 0

    const selector = '[data-magnetic], [data-row-link], [data-sector-bar-row]'

    const onMouseMove = (e: MouseEvent) => {
      mx = e.clientX
      my = e.clientY
      ret.classList.add('visible')
    }

    const onMouseLeave = () => {
      ret.classList.remove('visible')
    }

    const onMouseOver = (e: MouseEvent) => {
      const el = e.target instanceof Element ? e.target.closest<HTMLElement>(selector) : null
      if (el) target = el
    }

    const onMouseOut = (e: MouseEvent) => {
      const el = e.target instanceof Element ? e.target.closest<HTMLElement>(selector) : null
      if (el && el === target) target = null
    }

    const tick = () => {
      let tx = mx
      let ty = my
      let tw = 20
      let th = 20
      if (target && document.body.contains(target)) {
        const r = target.getBoundingClientRect()
        tx = r.left + r.width / 2
        ty = r.top + r.height / 2
        tw = r.width + 10
        th = r.height + 8
      }
      rx += (tx - rx) * 0.24
      ry += (ty - ry) * 0.24
      rw += (tw - rw) * 0.24
      rh += (th - rh) * 0.24
      ret.style.width = `${rw.toFixed(1)}px`
      ret.style.height = `${rh.toFixed(1)}px`
      ret.style.transform = `translate3d(${(rx - rw / 2).toFixed(1)}px, ${(ry - rh / 2).toFixed(1)}px, 0)`
      rafId = window.requestAnimationFrame(tick)
    }

    window.addEventListener('mousemove', onMouseMove, { passive: true })
    document.addEventListener('mouseleave', onMouseLeave)
    document.addEventListener('mouseover', onMouseOver, { passive: true })
    document.addEventListener('mouseout', onMouseOut, { passive: true })
    rafId = window.requestAnimationFrame(tick)

    return () => {
      if (rafId) window.cancelAnimationFrame(rafId)
      window.removeEventListener('mousemove', onMouseMove)
      document.removeEventListener('mouseleave', onMouseLeave)
      document.removeEventListener('mouseover', onMouseOver)
      document.removeEventListener('mouseout', onMouseOut)
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

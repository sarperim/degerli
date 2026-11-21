import { useEffect, useRef, useState } from 'react'

const HOVER_QUERY = '(hover: hover) and (pointer: fine)'
const COARSE_QUERY = '(pointer: coarse)'
const EDGE_PROXIMITY_PX = 48
const IDLE_FADE_MS = 1000

function clamp01(value: number): number {
  return Math.max(0, Math.min(1, value))
}

/**
 * SLICE-039 — Split-Rail Fill viewport scroll indicator.
 *
 * A compact, editorial 1px bronze rail pinned to the right edge of the viewport.
 * Progress is expressed as a vertical `scaleY` fill, a 4×1px split tick, and a
 * drag-only monospaced percentage readout. All per-frame DOM writes happen inside
 * a `requestAnimationFrame` scheduled from passive scroll/resize/ResizeObserver
 * callbacks, so scrolling and scrubbing never trigger React renders.
 *
 * The native main-viewport scrollbar is hidden in CSS while this rail is active
 * (fine pointer only); nested scroll containers keep a quiet architectural bar.
 * The active class (`html.scroll-rail-active`) is added when the fine-pointer
 * effect mounts and removed on cleanup, so native scrollbars are suppressed only
 * while this component is actually running.
 *
 * `tabIndex` is intentionally omitted: native document keyboard scrolling
 * (Arrow/Page/Home/End/Space) already covers keyboard users without injecting an
 * extra tab stop into AppShell's keyboard focus order. `aria-valuenow` is owned
 * imperatively by the rAF loop, which is the single writer per frame; React only
 * supplies the initial `0` and the static scrollbar semantics.
 */
export function ViewportScrollRail() {
  const railRef = useRef<HTMLDivElement | null>(null)
  const trackRef = useRef<HTMLDivElement | null>(null)
  const fillRef = useRef<HTMLDivElement | null>(null)
  const tickRef = useRef<HTMLDivElement | null>(null)
  const readoutRef = useRef<HTMLSpanElement | null>(null)
  const [hasOverflow, setHasOverflow] = useState(false)

  useEffect(() => {
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return
    if (!window.matchMedia(HOVER_QUERY).matches) return
    if (window.matchMedia(COARSE_QUERY).matches) return

    const rail = railRef.current
    const track = trackRef.current
    const fill = fillRef.current
    const tick = tickRef.current
    const readout = readoutRef.current
    if (!rail || !track || !fill || !tick || !readout) return

    document.documentElement.classList.add('scroll-rail-active')

    let rafId = 0
    let idleTimer: number | undefined
    let dragging = false
    let pointerInZone = false
    let lastClientX = Number.NEGATIVE_INFINITY

    const readMetrics = () => {
      const doc = document.documentElement
      const maxScroll = Math.max(0, doc.scrollHeight - window.innerHeight)
      const overflow = doc.scrollHeight > window.innerHeight + 4
      return { maxScroll, overflow }
    }

    const setVisible = (visible: boolean) => {
      const allowed = visible && readMetrics().overflow
      rail.classList.toggle('is-visible', allowed)
    }

    const clearIdleTimer = () => {
      if (idleTimer !== undefined) {
        window.clearTimeout(idleTimer)
        idleTimer = undefined
      }
    }

    const scheduleIdleFade = () => {
      clearIdleTimer()
      if (dragging) return
      idleTimer = window.setTimeout(() => {
        idleTimer = undefined
        if (!dragging && !pointerInZone) setVisible(false)
      }, IDLE_FADE_MS)
    }

    const render = () => {
      rafId = 0
      const { maxScroll, overflow } = readMetrics()
      rail.setAttribute('data-overflow', overflow ? 'true' : 'false')
      setHasOverflow((prev) => (prev === overflow ? prev : overflow))
      if (!overflow) {
        setVisible(false)
        fill.style.transform = 'scaleY(0)'
        tick.style.transform = 'translate3d(0, 0, 0)'
        readout.style.transform = 'translate3d(0, 0, 0) translateY(-50%)'
        readout.textContent = '0%'
        rail.setAttribute('aria-valuenow', '0')
        return
      }

      const progress = maxScroll > 0 ? clamp01(window.scrollY / maxScroll) : 0
      const percent = Math.round(progress * 100)
      const trackHeight = track.getBoundingClientRect().height
      const splitY = progress * Math.max(0, trackHeight)

      fill.style.transform = `scaleY(${progress})`
      tick.style.transform = `translate3d(0, ${splitY}px, 0)`
      readout.style.transform = `translate3d(0, ${splitY}px, 0) translateY(-50%)`
      readout.textContent = `${percent}%`
      rail.setAttribute('aria-valuenow', String(percent))
    }

    const scheduleRender = () => {
      if (!rafId) rafId = window.requestAnimationFrame(render)
    }

    const ratioFromPointer = (clientY: number) => {
      const rect = track.getBoundingClientRect()
      return clamp01((clientY - rect.top) / Math.max(1, rect.height))
    }

    const scrollToRatio = (ratio: number) => {
      const { maxScroll } = readMetrics()
      window.scrollTo({ top: ratio * maxScroll, behavior: 'auto' })
    }

    const onScroll = () => {
      setVisible(true)
      scheduleRender()
      scheduleIdleFade()
    }

    const onPointerMoveWindow = (event: PointerEvent | MouseEvent) => {
      lastClientX = event.clientX
      pointerInZone = window.innerWidth - event.clientX <= EDGE_PROXIMITY_PX
      if (pointerInZone) {
        clearIdleTimer()
        setVisible(true)
      } else if (!dragging) {
        scheduleIdleFade()
      }
    }

    const onPointerEnter = () => {
      pointerInZone = true
      clearIdleTimer()
      setVisible(true)
    }

    const onPointerLeave = () => {
      pointerInZone = window.innerWidth - lastClientX <= EDGE_PROXIMITY_PX
      if (!pointerInZone && !dragging) scheduleIdleFade()
    }

    const endDrag = () => {
      if (!dragging) return
      dragging = false
      rail.classList.remove('is-dragging')
      document.body.classList.remove('scroll-rail-dragging')
      pointerInZone = window.innerWidth - lastClientX <= EDGE_PROXIMITY_PX
      if (!pointerInZone) scheduleIdleFade()
    }

    const onPointerDown = (event: PointerEvent) => {
      if (event.button !== 0 || !readMetrics().overflow) return
      dragging = true
      rail.classList.add('is-dragging')
      document.body.classList.add('scroll-rail-dragging')
      setVisible(true)
      clearIdleTimer()
      try {
        rail.setPointerCapture?.(event.pointerId)
      } catch {
        // jsdom / browsers without pointer capture support
      }
      scrollToRatio(ratioFromPointer(event.clientY))
      scheduleRender()
    }

    const onPointerMoveDrag = (event: PointerEvent) => {
      if (!dragging) return
      scrollToRatio(ratioFromPointer(event.clientY))
      scheduleRender()
    }

    const onPointerUp = (event: PointerEvent) => {
      try {
        if (rail.hasPointerCapture?.(event.pointerId)) {
          rail.releasePointerCapture(event.pointerId)
        }
      } catch {
        // jsdom / browsers without pointer capture support
      }
      endDrag()
    }

    const onResize = () => scheduleRender()

    render()
    window.addEventListener('scroll', onScroll, { passive: true })
    window.addEventListener('resize', onResize)
    window.addEventListener('pointermove', onPointerMoveWindow, { passive: true })
    rail.addEventListener('pointerenter', onPointerEnter)
    rail.addEventListener('pointerleave', onPointerLeave)
    rail.addEventListener('pointerdown', onPointerDown)
    rail.addEventListener('pointermove', onPointerMoveDrag)
    rail.addEventListener('pointerup', onPointerUp)
    rail.addEventListener('pointercancel', onPointerUp)
    rail.addEventListener('lostpointercapture', onPointerUp)

    let observer: ResizeObserver | undefined
    if (typeof ResizeObserver !== 'undefined') {
      observer = new ResizeObserver(() => scheduleRender())
      observer.observe(document.body)
      observer.observe(document.documentElement)
    }

    return () => {
      if (rafId) window.cancelAnimationFrame(rafId)
      clearIdleTimer()
      observer?.disconnect()
      window.removeEventListener('scroll', onScroll)
      window.removeEventListener('resize', onResize)
      window.removeEventListener('pointermove', onPointerMoveWindow)
      rail.removeEventListener('pointerenter', onPointerEnter)
      rail.removeEventListener('pointerleave', onPointerLeave)
      rail.removeEventListener('pointerdown', onPointerDown)
      rail.removeEventListener('pointermove', onPointerMoveDrag)
      rail.removeEventListener('pointerup', onPointerUp)
      rail.removeEventListener('pointercancel', onPointerUp)
      rail.removeEventListener('lostpointercapture', onPointerUp)
      rail.classList.remove('is-visible', 'is-dragging')
      document.body.classList.remove('scroll-rail-dragging')
      document.documentElement.classList.remove('scroll-rail-active')
    }
  }, [])

  return (
    <div
      ref={railRef}
      id="viewport-scroll-rail"
      data-testid="viewport-scroll-rail"
      data-overflow={hasOverflow ? 'true' : 'false'}
      className="viewport-scroll-rail"
      role="scrollbar"
      aria-controls="app-main"
      aria-label="Page scroll"
      aria-orientation="vertical"
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={0}
    >
      <div ref={trackRef} className="viewport-scroll-rail__track">
        <div
          className="viewport-scroll-rail__base"
          data-testid="scroll-rail-base"
        />
        <div
          ref={fillRef}
          className="viewport-scroll-rail__fill"
          data-testid="scroll-rail-fill"
        />
        <div
          ref={tickRef}
          className="viewport-scroll-rail__tick"
          data-testid="scroll-rail-tick"
        />
        <span
          ref={readoutRef}
          className="viewport-scroll-rail__readout"
          data-testid="scroll-rail-readout"
          aria-hidden="true"
        />
      </div>
    </div>
  )
}

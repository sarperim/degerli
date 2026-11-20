import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'

import { formatNumber } from '@/lib/format'
import { useTheme } from '@/lib/theme'

const BAYER_4X4 = [
  [0, 8, 2, 10],
  [12, 4, 14, 6],
  [3, 11, 1, 9],
  [15, 7, 13, 5],
] as const

export interface HeroCurvePoint {
  date: string
  val: number
}

/**
 * Generates a deterministic 64-session BIST 100 EOD backdrop curve anchored to
 * the served `xu100Level` and `asOf` date (or fallback 10,418.60 / 2026-11-18).
 */
export function buildHeroCurve(xu100Level?: number | null, asOf?: string): HeroCurvePoint[] {
  const anchorVal = xu100Level && Number.isFinite(xu100Level) ? xu100Level : 10418.6
  const startVal = anchorVal * 0.85
  const span = anchorVal - startVal
  const baseDate = asOf && /^\d{4}-\d{2}-\d{2}$/.test(asOf) ? new Date(`${asOf}T00:00:00Z`) : new Date(Date.UTC(2026, 10, 18))

  return Array.from({ length: 64 }, (_, i) => {
    const val =
      i === 63
        ? anchorVal
        : Math.round(
            (startVal +
              (i / 63) * span +
              Math.sin(i * 0.34) * (anchorVal * 0.023) +
              Math.cos(i * 0.72) * (anchorVal * 0.0125)) *
              100,
          ) / 100
    const d = new Date(baseDate.getTime() - Math.round((63 - i) * 1.45) * 86_400_000)
    const yyyy = d.getUTCFullYear()
    const mm = String(d.getUTCMonth() + 1).padStart(2, '0')
    const dd = String(d.getUTCDate()).padStart(2, '0')
    return { date: `${yyyy}-${mm}-${dd}`, val }
  })
}

/**
 * Interactive 64-session BIST 100 EOD backdrop canvas + live scrub readout bar
 * from `geminisonuiux.html` (SLICE-025).
 */
export function useHeroBackdropCanvas({
  heroRef,
  canvasRef,
  xu100Level,
  asOf,
  locale,
}: {
  heroRef: React.RefObject<HTMLElement | null>
  canvasRef: React.RefObject<HTMLCanvasElement | null>
  xu100Level?: number | null
  asOf?: string
  locale: string
}) {
  const theme = useTheme()
  const [scrubPoint, setScrubPoint] = useState<HeroCurvePoint | null>(null)

  useEffect(() => {
    const hero = heroRef.current
    const canvas = canvasRef.current
    if (!hero || !canvas) return
    let ctx: CanvasRenderingContext2D | null = null
    try {
      ctx = canvas.getContext('2d')
    } catch {
      ctx = null
    }
    if (!ctx) return

    const curve = buildHeroCurve(xu100Level, asOf)
    const vals = curve.map((p) => p.val)
    const minVal = Math.min(...vals) * 0.97
    const maxVal = Math.max(...vals) * 1.02
    const valSpan = Math.max(1, maxVal - minVal)
    const mouse = { x: -1, inside: false }

    const draw = () => {
      const rect = hero.getBoundingClientRect()
      const w = rect.width
      const h = rect.height
      if (!w || !h) return

      const st = window.getComputedStyle(document.documentElement)
      const stroke = st.getPropertyValue('--curve-stroke').trim() || 'rgba(198,139,73,0.5)'
      const fill = st.getPropertyValue('--curve-fill').trim() || 'rgba(198,139,73,0.055)'
      const crosshair = st.getPropertyValue('--crosshair').trim() || 'rgba(220,225,230,0.18)'
      const accent = st.getPropertyValue('--brand-accent').trim() || '#C68B49'

      ctx.clearRect(0, 0, w, h)
      const topY = h * 0.42
      const botY = Math.max(topY + 24, h - 24)
      const pts = curve.map((p, i) => ({
        x: (i / 63) * w,
        y: botY - ((p.val - minVal) / valSpan) * (botY - topY),
        ...p,
      }))

      ctx.beginPath()
      pts.forEach((p, i) => (i === 0 ? ctx.moveTo(p.x, p.y) : ctx.lineTo(p.x, p.y)))
      ctx.strokeStyle = stroke
      ctx.lineWidth = 1.4
      ctx.stroke()

      ctx.lineTo(w, h)
      ctx.lineTo(0, h)
      ctx.closePath()
      ctx.fillStyle = fill
      ctx.fill()

      if (mouse.inside) {
        const idx = Math.round(Math.max(0, Math.min(1, mouse.x / w)) * 63)
        const pt = pts[idx]
        if (pt) {
          ctx.strokeStyle = crosshair
          ctx.setLineDash([3, 4])
          ctx.beginPath()
          ctx.moveTo(pt.x + 0.5, 0)
          ctx.lineTo(pt.x + 0.5, h)
          ctx.moveTo(0, pt.y + 0.5)
          ctx.lineTo(w, pt.y + 0.5)
          ctx.stroke()
          ctx.setLineDash([])

          ctx.fillStyle = accent
          ctx.fillRect(pt.x - 3.5, pt.y - 3.5, 7, 7)
          setScrubPoint({ date: pt.date, val: pt.val })
        }
      }
    }

    const resize = () => {
      const rect = hero.getBoundingClientRect()
      if (!rect.width || !rect.height) return
      const dpr = Math.min(window.devicePixelRatio || 1, 2)
      canvas.width = Math.floor(rect.width * dpr)
      canvas.height = Math.floor(rect.height * dpr)
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
      draw()
    }

    const onMouseMove = (e: MouseEvent) => {
      const rect = hero.getBoundingClientRect()
      mouse.x = e.clientX - rect.left
      mouse.inside = true
      draw()
    }

    const onMouseLeave = () => {
      mouse.inside = false
      setScrubPoint(null)
      draw()
    }

    resize()
    hero.addEventListener('mousemove', onMouseMove, { passive: true })
    hero.addEventListener('mouseleave', onMouseLeave)
    window.addEventListener('resize', resize)

    return () => {
      hero.removeEventListener('mousemove', onMouseMove)
      hero.removeEventListener('mouseleave', onMouseLeave)
      window.removeEventListener('resize', resize)
    }
  }, [heroRef, canvasRef, xu100Level, asOf, theme, locale])

  return scrubPoint
    ? `XU100 · ${scrubPoint.date} · ${formatNumber(locale, scrubPoint.val, {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
      })}`
    : null
}

/**
 * Right-hand Archival Photo Plate (`#photo-plate`) from `geminisonuiux.html` (SLICE-025):
 *
 * - 4 corner `+` crop marks (`tl`/`tr`/`bl`/`br`)
 * - 3D perspective tilt on hover + radial bronze `.photo-loupe`
 * - Real-time 4-level Bayer 4×4 ordered-dithered İstanbul Bosphorus & financial skyline canvas
 * - HUD overlay (`41.0812° N, 29.0112° E` · `DUOTONE · BAYER 4×4`)
 * - `FIG. 01 — İSTANBUL FINANCIAL ARCHIVE` caption bar with `Swap photo` upload
 */
export function HeroInteractivePlate() {
  const { t } = useTranslation()
  const theme = useTheme()
  const plateRef = useRef<HTMLDivElement | null>(null)
  const viewportRef = useRef<HTMLDivElement | null>(null)
  const loupeRef = useRef<HTMLDivElement | null>(null)
  const canvasRef = useRef<HTMLCanvasElement | null>(null)
  const customImageRef = useRef<HTMLImageElement | null>(null)
  const [imageVersion, setImageVersion] = useState(0)

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return
    let pCtx: CanvasRenderingContext2D | null = null
    try {
      pCtx = canvas.getContext('2d')
    } catch {
      pCtx = null
    }
    if (!pCtx) return

    const w = canvas.width
    const h = canvas.height
    const off = document.createElement('canvas')
    off.width = w
    off.height = h
    let c: CanvasRenderingContext2D | null = null
    try {
      c = off.getContext('2d')
    } catch {
      c = null
    }
    if (!c) return

    const customImg = customImageRef.current
    if (customImg) {
      const s = Math.max(w / customImg.width, h / customImg.height)
      c.drawImage(
        customImg,
        (w - customImg.width * s) / 2,
        (h - customImg.height * s) / 2,
        customImg.width * s,
        customImg.height * s,
      )
    } else {
      const sky = c.createLinearGradient(0, 0, 0, h * 0.68)
      sky.addColorStop(0, '#1c1c1c')
      sky.addColorStop(0.55, '#606060')
      sky.addColorStop(1, '#e0e0e0')
      c.fillStyle = sky
      c.fillRect(0, 0, w, h)

      const towers: [number, number, number, number][] = [
        [24, 115, 22, 74],
        [52, 92, 26, 97],
        [82, 126, 18, 63],
        [106, 84, 30, 105],
        [142, 108, 22, 81],
        [192, 68, 28, 121],
        [224, 52, 24, 137],
        [252, 86, 32, 103],
        [290, 104, 24, 85],
        [318, 118, 28, 71],
      ]
      for (const [x, y, tw, th] of towers) {
        c.fillStyle = '#191919'
        c.fillRect(x, y, tw, th)
        c.fillStyle = 'rgba(242,242,242,0.46)'
        for (let wy = y + 8; wy < y + th - 14; wy += 9) {
          if ((x + wy) % 3 !== 0) {
            c.fillRect(x + 3, wy, tw - 6, 1.5)
          }
        }
      }

      const dY = h * 0.65
      c.fillStyle = '#101010'
      c.strokeStyle = '#101010'
      c.fillRect(w * 0.25, h * 0.25, 5, h * 0.45)
      c.fillRect(w * 0.25 + 9, h * 0.25, 5, h * 0.45)
      c.fillRect(w * 0.76, h * 0.29, 4, h * 0.41)
      c.fillRect(0, dY, w, 4)

      c.lineWidth = 2
      c.beginPath()
      c.moveTo(0, h * 0.56)
      c.quadraticCurveTo(w * 0.14, h * 0.48, w * 0.25 + 7, h * 0.26)
      c.quadraticCurveTo(w * 0.51, h * 0.64, w * 0.76 + 2, h * 0.3)
      c.quadraticCurveTo(w * 0.89, h * 0.49, w, h * 0.57)
      c.stroke()

      const wat = c.createLinearGradient(0, dY + 4, 0, h)
      wat.addColorStop(0, '#1c1c1c')
      wat.addColorStop(0.5, '#3a3a3a')
      wat.addColorStop(1, '#121212')
      c.fillStyle = wat
      c.fillRect(0, dY + 4, w, h - dY - 4)

      for (let ry = dY + 9; ry < h - 4; ry += 4) {
        c.fillStyle = `rgba(236,236,236,${0.28 - (ry - dY) / 360})`
        c.fillRect(w * 0.55 - (ry - dY) * 0.5, ry, 54 + (ry % 17), 1.5)
      }
    }

    const src = c.getImageData(0, 0, w, h)
    const d = src.data
    const dark = theme === 'dark'
    const pal: [number, number, number][] = dark
      ? [
          [17, 19, 22],
          [68, 75, 84],
          [198, 139, 73],
          [224, 220, 212],
        ]
      : [
          [23, 26, 30],
          [106, 111, 116],
          [168, 108, 42],
          [246, 245, 240],
        ]

    for (let y = 0; y < h; y++) {
      for (let x = 0; x < w; x++) {
        const i = (y * w + x) * 4
        const lum = (0.299 * d[i] + 0.587 * d[i + 1] + 0.114 * d[i + 2]) / 255
        const lv = Math.min(
          3,
          Math.max(0, Math.floor(lum * 3 + ((BAYER_4X4[y & 3][x & 3] + 0.5) / 16) * 0.92)),
        )
        d[i] = pal[lv][0]
        d[i + 1] = pal[lv][1]
        d[i + 2] = pal[lv][2]
        d[i + 3] = 255
      }
    }
    pCtx.putImageData(src, 0, 0)
  }, [theme, imageVersion])

  const handleMouseMove = (e: React.MouseEvent<HTMLDivElement>) => {
    const viewport = viewportRef.current
    const plate = plateRef.current
    const loupe = loupeRef.current
    if (!viewport || !plate || !loupe) return

    const r = viewport.getBoundingClientRect()
    if (!r.width || !r.height) return
    const px = (e.clientX - r.left) / r.width
    const py = (e.clientY - r.top) / r.height
    loupe.style.setProperty('--lx', `${(px * 100).toFixed(1)}%`)
    loupe.style.setProperty('--ly', `${(py * 100).toFixed(1)}%`)
    plate.style.transform = `perspective(900px) rotateX(${((0.5 - py) * 4).toFixed(2)}deg) rotateY(${((px - 0.5) * 5).toFixed(2)}deg)`
  }

  const handleMouseLeave = () => {
    if (plateRef.current) {
      plateRef.current.style.transform = 'none'
    }
  }

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (!file) return
    const reader = new FileReader()
    reader.onload = (ev) => {
      const result = ev.target?.result
      if (typeof result !== 'string') return
      const img = new Image()
      img.onload = () => {
        customImageRef.current = img
        setImageVersion((v) => v + 1)
      }
      img.src = result
    }
    reader.readAsDataURL(file)
  }

  return (
    <div
      ref={plateRef}
      id="photo-plate"
      data-testid="hero-photo-plate"
      className="relative border border-[var(--border-strong)] bg-card p-2.5 transition-transform duration-200 will-change-transform"
    >
      <span
        aria-hidden="true"
        className="pointer-events-none absolute -top-2 -left-1.5 font-mono text-[11px] leading-none text-muted-foreground select-none"
      >
        +
      </span>
      <span
        aria-hidden="true"
        className="pointer-events-none absolute -top-2 -right-1.5 font-mono text-[11px] leading-none text-muted-foreground select-none"
      >
        +
      </span>
      <span
        aria-hidden="true"
        className="pointer-events-none absolute -bottom-2 -left-1.5 font-mono text-[11px] leading-none text-muted-foreground select-none"
      >
        +
      </span>
      <span
        aria-hidden="true"
        className="pointer-events-none absolute -bottom-2 -right-1.5 font-mono text-[11px] leading-none text-muted-foreground select-none"
      >
        +
      </span>

      <div
        ref={viewportRef}
        id="photo-viewport"
        onMouseMove={handleMouseMove}
        onMouseLeave={handleMouseLeave}
        className="group relative aspect-[4/3] w-full cursor-crosshair overflow-hidden border border-border bg-background"
      >
        <canvas
          ref={canvasRef}
          id="editorial-photo-canvas"
          width={360}
          height={270}
          aria-hidden="true"
          className="block h-full w-full [image-rendering:pixelated]"
        />
        <div
          ref={loupeRef}
          id="photo-loupe"
          aria-hidden="true"
          className="pointer-events-none absolute inset-0 opacity-0 transition-opacity duration-150 group-hover:opacity-100"
          style={{
            background:
              'radial-gradient(circle 120px at var(--lx, 50%) var(--ly, 50%), rgba(198, 139, 73, 0.22), transparent 72%)',
          }}
        />
        <div
          aria-hidden="true"
          className="pointer-events-none absolute top-2.5 right-2.5 left-2.5 flex justify-between font-mono text-[10px]"
        >
          <span className="border border-[var(--border-strong)] bg-background/85 px-1.5 py-0.5 text-muted-foreground">
            41.0812° N, 29.0112° E
          </span>
          <span className="border border-[var(--border-strong)] bg-background/85 px-1.5 py-0.5 text-[var(--stale-foreground)]">
            DUOTONE · BAYER 4×4
          </span>
        </div>
      </div>

      <div className="flex items-center justify-between pt-2 font-mono text-[10px] text-muted-foreground">
        <span>{t('marketOverview.hero.plateCaption')}</span>
        <label
          data-magnetic=""
          className="cursor-pointer font-mono text-[10px] text-[var(--stale-foreground)] underline underline-offset-4 hover:text-foreground"
        >
          {t('marketOverview.hero.swapPhoto')}
          <input
            type="file"
            accept="image/*"
            aria-label={t('marketOverview.hero.swapPhoto')}
            onChange={handleFileChange}
            className="sr-only"
          />
        </label>
      </div>
    </div>
  )
}

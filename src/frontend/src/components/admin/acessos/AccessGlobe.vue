<script setup lang="ts">
/** Globo 3D (globe.gl, carregado sob demanda): países em mapa de calor e uma coluna por cidade. Arraste para girar, role para aproximar. */
import { feature } from 'topojson-client'
import { onBeforeUnmount, onMounted, shallowRef, watch } from 'vue'
import { countryCentroids, numericToIso2 } from '@/lib/countryCentroids'
import { countryName, heat, type CountryTotal, type MapPoint } from '@/lib/access'
import { formatInt } from '@/lib/format'

const props = defineProps<{ countries: CountryTotal[]; points: MapPoint[]; focus: string | null }>()
const emit = defineEmits<{ pick: [country: string] }>()

const host = shallowRef<HTMLDivElement | null>(null)
// eslint-disable-next-line @typescript-eslint/no-explicit-any
let world: any = null
let observer: ResizeObserver | null = null
let polygons: { id?: string; properties: { name: string } }[] = []

const byCountry = () => new Map(props.countries.map((c) => [c.pais, c.acessos]))
const maxCountry = () => Math.max(0, ...props.countries.map((c) => c.acessos))
const maxPoint = () => Math.max(0, ...props.points.map((p) => p.acessos))

type Rgb = [number, number, number]
const COLD: Rgb = [43, 58, 71]
const WARM: Rgb = [250, 176, 5]
const HOT: Rgb = [235, 64, 52]

/** Do frio (azul acinzentado) ao quente (laranja → vermelho). */
function ramp(t: number) {
  const [a, b, f] = t < 0.5 ? [COLD, WARM, t * 2] : [WARM, HOT, (t - 0.5) * 2]
  return `rgb(${a.map((v, i) => Math.round(v + ((b as Rgb)[i]! - v) * (f as number))).join(',')})`
}

function paint() {
  if (!world) return
  const totals = byCountry()
  const maxC = maxCountry()
  const maxP = maxPoint()
  const iso = (f: { id?: string }) => (f.id ? numericToIso2[String(f.id).padStart(3, '0')] : undefined)
  world
    .polygonCapColor((f: { id?: string }) => {
      const n = totals.get(iso(f) ?? '')
      return n ? ramp(heat(n, maxC)) : 'rgb(43,58,71)'
    })
    .polygonLabel((f: { id?: string; properties: { name: string } }) => {
      const code = iso(f)
      const n = code ? totals.get(code) : undefined
      return `<div style="padding:4px 8px"><b>${code ? countryName(code) : f.properties.name}</b><br/>${formatInt(n ?? 0)} acessos</div>`
    })
    .pointsData(props.points)
    .pointAltitude((p: MapPoint) => 0.01 + heat(p.acessos, maxP) * 0.25)
    .pointRadius((p: MapPoint) => 0.25 + heat(p.acessos, maxP) * 0.5)
    .pointColor(() => '#ffffff')
    .pointLabel((p: MapPoint) => `<div style="padding:4px 8px"><b>${p.label}</b><br/>${formatInt(p.acessos)} acessos</div>`)
}

function fly() {
  if (!world || !props.focus) return
  const c = countryCentroids[props.focus]
  if (c) world.pointOfView({ lat: c[0], lng: c[1], altitude: 0.9 }, 1000)
}

onMounted(async () => {
  const el = host.value
  if (!el) return
  const [{ default: Globe }, topology] = await Promise.all([import('globe.gl'), import('world-atlas/countries-110m.json')])
  if (!host.value) return
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  polygons = (feature(topology as any, (topology as any).objects.countries) as any).features
  world = new Globe(el, { animateIn: false })
    .backgroundColor('rgba(0,0,0,0)')
    .showAtmosphere(true)
    .atmosphereColor('#7aa7c7')
    .width(el.clientWidth)
    .height(el.clientHeight)
    .polygonsData(polygons)
    .polygonAltitude(0.006)
    .polygonSideColor(() => 'rgba(0,0,0,0)')
    .polygonStrokeColor(() => 'rgba(255,255,255,0.18)')
    .onPolygonClick((f: { id?: string }) => {
      const code = f.id ? numericToIso2[String(f.id).padStart(3, '0')] : undefined
      if (code) emit('pick', code)
    })
  world.globeMaterial().color.set('#16202a')
  world.pointOfView({ lat: -14, lng: -52, altitude: 2.2 })
  paint()
  fly()
  observer = new ResizeObserver(() => world?.width(el.clientWidth).height(el.clientHeight))
  observer.observe(el)
})

watch(() => [props.countries, props.points], paint)
watch(() => props.focus, fly)

onBeforeUnmount(() => {
  observer?.disconnect()
  world?._destructor?.()
  world = null
})
</script>

<template>
  <div ref="host" class="h-[420px] w-full overflow-hidden rounded-[24px] bg-surface-2 sm:h-[560px]" role="img" aria-label="Globo 3D com os acessos por país e cidade" />
</template>

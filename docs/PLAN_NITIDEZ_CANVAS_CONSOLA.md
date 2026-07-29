# Plan: eliminar el tiritón de los canvas dockeados en la consola

**Fecha:** 2026-07-28
**Alcance:** plan. No se ha modificado código ni assets todavía.
**Síntoma:** desde que los instrumentos se dockearon en la consola y quedaron más
pequeños, tiritan al acercarse a mirarlos y al mover la cabeza. El conning es el peor,
el AIS el que menos.
**Método:** lectura de `BridgeConsoleDisplayRig.cs` / `BridgeConningDisplay.cs`,
inspección de los `.asset` de URP y de los `.png.meta` del panel OpenBridge, y
arqueología de git sobre el asset de Quest. No se ha medido en el visor.

---

## 1. Resumen

No sobra resolución: **falta muestreo**. Los trazos finos del panel caen por debajo del
límite de Nyquist del display, así que al mover la cabeza la cobertura de cada línea se
enciende y apaga entre frames.

Dos cambios que antes se compensaban ahora se suman en contra:

| Commit | Cambio | Efecto |
|---|---|---|
| `201472e` | MSAA 4 → 2, RenderScale 1.6 → 1.0 | ~2.6× menos muestras por área de pantalla |
| `6c16913` / `908a227` | Instrumentos dockeados y reducidos a un slot de la consola | ~2.5× más detalle de UI por área de pantalla |

Mientras el conning flotaba a 1,65 m y ~2 m de ancho, el margen de muestreo absorbía el
recorte de calidad. Dockeado en la consola ya no.

---

## 2. Diagnóstico

### 2.1 Densidad de píxeles por instrumento

`FitInstrument` reparte el nativo del instrumento sobre los metros del slot conservando
aspecto ([`BridgeConsoleDisplayRig.cs:585`](../Assets/NavigationSim/Runtime/UI/BridgeConsoleDisplayRig.cs#L585)):

```csharp
var scale = Mathf.Min(widthM / native.x, heightM / native.y);
```

Con un slot de ~0,78 × 0,48 m (el que sale de `MaxSlotWidthMeters` / `MaxSlotHeightMeters`
sobre una consola de ~1,6 m útiles):

| Instrumento | Nativo px | mm por px de UI | px de pantalla por px de UI @0,5 m | @0,7 m |
|---|---|---:|---:|---:|
| **Conning** | 1920×1080 | **0,41** | **1,16** | **0,83** |
| Radar | 1200×960 | 0,50 | 1,40 | 1,00 |
| Chart | 1100×900 | 0,53 | 1,49 | 1,06 |
| AIS | 900×720 | 0,67 | 1,91 | 1,36 |

Quest 3 ronda los 25 px/grado, así que a 0,5 m un píxel de pantalla cubre ~0,35 mm y a
0,7 m ~0,49 mm.

**Nyquist pide ≥ 2 px de pantalla por rasgo para que sea estable.** El conning está en
~1,16 asomado y baja de 1:1 en cuanto te separas. El AIS casi llega a 2, y es justo el que
no molesta. El orden de la tabla reproduce exactamente el orden del síntoma reportado.

### 2.2 Por qué el conning es el peor

Tres razones que se acumulan:

1. **Es el más denso.** 1920×1080 en el mismo slot físico que el AIS de 900×720.
2. **Su contenido es un diseño de monitor.** El panel que se muestra es el import
   OpenBridge (`CaseSize = 1920×1080` en
   [`OpenBridgeConningBinder.cs:240`](../Assets/NavigationSim/Runtime/UI/OpenBridgeConningBinder.cs#L240)),
   autorado para 1080p de escritorio: lleno de trazos de 1–2 px y tipografía pequeña.
3. **Sus sprites no tienen mipmaps.** En
   `Assets/NavigationSim/Resources/OpenBridgeConning/*.png.meta`:

   ```yaml
   mipmaps:
     enableMipMap: 0
   aniso: 1
   ```

   Un sprite minificado por debajo de 1:1 sin mipmaps aliasea sin ningún filtro que lo
   salve. Y son precisamente las **agujas que rotan** (`conning-hdg-needle`,
   `conning-cog-arrow`), que es donde más salta a la vista.

### 2.3 Nota sobre qué arregla cada técnica

Conviene no mezclarlas, porque atacan cosas distintas:

- Los quads construidos en código usan `Texture2D.whiteTexture` teñida
  ([`BridgeInstrumentCanvas.cs:29`](../Assets/NavigationSim/Runtime/UI/BridgeInstrumentCanvas.cs#L29)),
  o sea que **todo lo visible es el borde del triángulo** → MSAA los resuelve casi perfecto.
- Los sprites del import OpenBridge tienen su forma en el alfa de la textura, no en la
  geometría → **MSAA no los toca**; ahí hacen falta mipmaps y aniso.
- El texto TMP se antialiasa analíticamente por derivadas en el shader SDF → suele ser
  estable y no es el sospechoso principal.

Por eso los pasos 1 y 2 del plan son complementarios, no alternativos.

---

## 3. Plan

Ordenado por relación coste/beneficio. La intención es parar después del paso 3 y volver
a evaluar en el visor antes de gastar fill rate o rehacer el layout.

### Paso 1 — MSAA 2 → 4 en el asset de Quest

`Assets/Universal Render Pipeline  Quest Asset.asset`, campo `m_MSAA`.

Es la palanca de mejor retorno. El asset tiene `m_RequireDepthTexture: 0` y
`m_RequireOpaqueTexture: 0`, así que el pase es forward de una sola pasada y el 4x se
resuelve dentro del tile memory de Quest 3 sin resolve extra a memoria principal. Deja
intacto el trabajo de `deebc0b` (sombras) y `201472e` (URP dedicado).

### Paso 2 — Mipmaps y aniso en los sprites del conning

Los 4 PNG de `Assets/NavigationSim/Resources/OpenBridgeConning/`:

- `enableMipMap: 0` → `1`
- `aniso: 1` → `4`

Coste en runtime: nulo. Coste en memoria: +33 % sobre 4 sprites pequeños, despreciable.
Es lo único que arregla el shimmer de las agujas al girar.

### Paso 3 — Engrosar los trazos sub-píxel del conning construido en código

- Borde de card de 2 px →4 px en
  [`BridgeConningDisplay.cs:900-903`](../Assets/NavigationSim/Runtime/UI/BridgeConningDisplay.cs#L900-L903).
- Agujas de 4 / 5 / 7 px en
  [`BridgeConningDisplay.cs:461-464`](../Assets/NavigationSim/Runtime/UI/BridgeConningDisplay.cs#L461-L464):
  subir el mínimo a ~6 px.
- Revisar el texto de 16 px (leyenda y etiquetas de readout) y subirlo a 18 px.

Coste cero. Aplica sólo a la ruta construida en código; si el binder OpenBridge está
activo, este paso no cambia nada de lo que se ve y puede saltarse.

### Paso 4 — RenderScale 1.0 → 1.15 *(sólo si hace falta)*

Escala cuadrático en fill rate y va justo en contra del trabajo de optimización de los
últimos cuatro commits. No tocar hasta confirmar que 1–3 no bastan, y medir GPU antes y
después.

### Paso 5 — `OVROverlayCanvas` *(la solución de fondo)*

No se usa en ningún punto de `NavigationSim`, pero el SDK está presente
(`Assets/Resources/OVROverlayCanvasSettings.asset`).

Renderiza el canvas a una RenderTexture y la envía como **compositor quad layer**. El
compositor la filtra y reproyecta a resolución nativa de panel en cada refresco del
display, lo que elimina a la vez el shimmer y el judder al mover la cabeza — que es
literalmente el síntoma descrito. Serían 2 capas, una por slot dockeado.

Riesgo a resolver antes de adoptarlo: las capas de compositor no se ordenan por
profundidad con la escena. Hay que usar modo underlay con hole-punch para que la consola
y las manos no queden mal ocluidas, y verificar que el puntero láser de
`VrUiPointer` / los colliders de `SimUiButton` siguen alineados con lo que se ve.

---

## 4. A descartar en paralelo: respiración de escala

Independiente del aliasing, hay una vía de tiritón *físico* que conviene descartar porque
es barata de comprobar.

[`BridgeConsoleDisplayRig.cs:65-83`](../Assets/NavigationSim/Runtime/UI/BridgeConsoleDisplayRig.cs#L65-L83)
recalcula `LayoutSlots()` y `FitActive()` **cada LateUpdate**, y `TryMeasureSurface` saca
las medidas de `Renderer.bounds`
([`BridgeConsoleDisplayRig.cs:619-634`](../Assets/NavigationSim/Runtime/UI/BridgeConsoleDisplayRig.cs#L619-L634)),
que es un AABB en espacio de mundo:

```csharp
usableWidth = Mathf.Max(0.6f, Vector3.Dot(bounds.size, Abs(right)) - SideMarginMeters * 2f);
```

Si la consola no está alineada a los ejes de mundo, cualquier micro-rotación cambia
`bounds.size` → cambia `slotWidth` → `FitInstrument` escribe un `localScale` distinto cada
frame y el panel respira.

**Comprobación:** loguear `slot.WidthM` durante unos segundos con la consola colocada. Si
es constante, no hay nada que hacer. Si varía, cachear el layout y recalcular sólo cuando
la consola se mueva de verdad.

---

## 5. Criterio de aceptación

1. Con la cabeza en movimiento lateral lento a ~0,5 m, las agujas del compás del conning
   no parpadean.
2. Los bordes de card se mantienen continuos al alejarse a 0,7 m.
3. El coste GPU no empeora respecto a `deebc0b` — comprobar con el overlay de métricas
   de OVR antes y después del paso 1.

---

## 6. Descartado explícitamente

**Bajar la resolución del canvas** (p. ej. 1920×1080 → 1280×720 manteniendo el tamaño
físico) sí funcionaría, y no por ahorro de GPU: lo que hace es agrandar cada trazo ~1,5×
en píxeles de pantalla, que es exactamente la cura. Pero obliga a re-maquetar el import de
Figma y el binder de shapes de `OpenBridgeConningBinder`, y los pasos 1–3 consiguen lo
mismo sin tocar el layout. Se reconsidera sólo si 1–5 no bastan.

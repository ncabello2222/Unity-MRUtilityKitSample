# Análisis de performance — Unity MR Utility Kit / Ship Bridge + Crest

**Fecha:** 2026-07-23  
**Proyecto:** `Unity-MRUtilityKitSample`  
**Unity:** 6000.4.9f1 · **URP:** 17.4.0 · **Meta XR / MRUK:** 203.0.0 · **Crest:** 5.9.2  
**Escena objetivo:** `BridgeRoomPrototype` (bridge MR + exterior Crest + NavigationSim)  
**Tipo de análisis:** estático (código + settings). No incluye captura Perfetto / OVR Metrics en headset.

---

## 1. Resumen ejecutivo

El proyecto combina tres cargas pesadas a la vez:

| Área | Impacto estimado | Notas |
|------|------------------|--------|
| **Crest (océano)** | Muy alto (GPU + CPU) | Simulaciones LOD, FFT, transparentes, copias de depth/opaque |
| **URP Quest mal calibrado para el stack actual** | Muy alto (GPU) | MSAA 4× + Render Scale **1.2** + Depth/Opaque Texture |
| **Editor / Domain Reload** | Muy alto (solo Editor) | ~219 assemblies; Enter Play Mode Options desactivado |
| **Terreno + escenario costero** | Alto (GPU) | Terrain grande delante de ventanas |
| **MRUK + generación de bridge** | Medio (CPU al arranque) | Primitivas + materiales en runtime |
| **NavigationSim + UI runtime** | Medio-bajo (CPU) | 50 Hz fijo; paneles TMP construidos por código |
| **Casco Blender / Interaction SDK** | Medio (GPU/CPU) | Meshes + grabbables |

En headset, el riesgo principal es **GPU bound** (Crest + resolución efectiva). En Editor, el dolor principal es **Domain Reload**, no el frame de Play.

Presupuestos de referencia Meta (Quest 3, carga media–alta): ~200–600 draw calls, ~1–2 M tris, SetPass &lt; 80. Crest solo puede consumir una fracción grande de ese presupuesto.

---

## 2. Qué es más caro (orden aproximado)

### 2.1 GPU — render (runtime en Quest)

#### A) Crest Water — **#1 sospechoso**

Config actual en `CrestOcean.prefab`:

| Parámetro | Valor actual | Coste |
|-----------|--------------|--------|
| LOD cascades (`_Slices`) | **9** | Muchas pasadas de simulación |
| Resolución simulación (`_Resolution`) | **384** | Alta para mobile VR (típicamente 128–256 en Quest) |
| Scale range | 4 → **256** | Cascadas muy grandes lejos |
| ShapeFFT | ON, wind **20**, res **128** | Oleaje GPU continuo |
| Animated Waves / Depth / Foam / Shadow LOD | ON @ **256** | 4 simulaciones activas |
| Depth Lod · Include Terrain Height | ON | Extra trabajo con Terrain |
| Underwater / Reflections | OFF (bien) | — |
| Meniscus | ON | Pass extra |
| Write Color / Depth / Motion Vectors | ON | Más writes y sync con URP |
| Surface transparent (queue 3000) | ON | Overdraw + sorting |

Crest regenera/centra el mesh cada frame (LOD around viewpoint). Eso es diseño del sistema, pero en stereo (dos ojos / multiview) el coste se multiplica.

`CrestOceanBootstrap.LateUpdate` reposiciona el océano cada frame (barato en CPU; el coste real sigue siendo GPU).

#### B) URP — resolución y copias de pantalla — **#2**

`Universal Render Pipeline Asset`:

| Setting | Valor | Problema |
|---------|-------|----------|
| **MSAA** | **4** | Costoso en fill-rate (Meta lo recomienda en general, pero junto a Crest + scale 1.2 es agresivo) |
| **Render Scale** | **1.2** | +44 % píxeles vs 1.0; con MSAA 4× es brutal |
| **Require Depth Texture** | **1** | Copia de depth cada frame (Crest/refracción) |
| **Require Opaque Texture** | **1** | Copia de color opaco (refracción Crest); Opaque Downsampling = None → copia full-res |
| HDR | 0 | Bien para Quest |
| Main Light Shadows | ON, distance **4**, map **1024** | Distancia corta (bien); resolución algo alta |
| Additional Lights | Per Pixel, 4 | Aceptable si hay pocas luces |
| SRP Batcher | ON | Bien |

Quality actual: **Medium** (`m_CurrentQuality: 2`) con `antiAliasing: 4` — refuerza MSAA.

**Efecto combinado:**  
`resolución efectiva ≈ eyeBuffer × 1.2`, luego MSAA 4×, luego Crest transparentes + depth/opaque copies. Es la combinación más cara del proyecto.

#### C) Terrain + escenario exterior — **#3**

- `Scenario_CoastalMountains` con Unity Terrain delante del bridge.
- Terrain en Quest: vertex + pixel cost altos si el heightmap/pixel error no está limitado.
- Quality Medium: `terrainPixelError: 1`, distancias de detail/tree altas → poco culling agresivo.
- Se ve a través de ventanas → siempre en frustum → poco beneficio de culling lateral.

#### D) Passes URP Renderer Features — **#4**

El renderer tiene **5× Render Objects** (Stencil, StencilTransparent, UI, OverlayUI, AfterRenderOpaque). Cada feature es un pass adicional. En Quest, Meta recomienda evitar passes excesivos (&gt;2 extras cuando sea posible).

Útiles para el sample MRUK original; en BridgeRoom algunos pueden sobrar.

#### E) Casco + Interaction + UI world-space — **#5**

- Hull Blender instanciado (`VesselHullPresenter`).
- Controles Interaction SDK (grabbables, rueda, telégrafo).
- Canvas world-space (NavigationPanel, SimulationConfigPanel, Conning) con TMP.

Coste moderado frente a Crest/URP, pero suma draw calls y overdraw en el interior.

---

### 2.2 CPU — runtime

| Sistema | Coste | Detalle |
|---------|-------|---------|
| Crest simulations | Alto | Command buffers / compute / queries por frame |
| `ExteriorWorldMotion.Update` | Bajo–medio | Matrices cada frame (aceptable) |
| `NavigationSimRunner` @ 50 Hz | Medio | Substeps si `timeScale` &gt; 1 |
| `BridgeRoomMapper` | Spike al load | Genera paredes/ventanas/exterior una vez |
| `SimulationConfigPanel` | Spike al abrir | Construye UI TMP por código (~28 creates) |
| MRUK scene load | Spike al Play | Prefab room en Editor (bootstrap) |

Hot paths relativamente limpios: poco LINQ en Update. Riesgo de GC al abrir paneles o regenerar bridge.

---

### 2.3 Editor (no es FPS del build)

| Factor | Evidencia | Impacto |
|--------|-----------|---------|
| Domain Reload en cada Play | `EnterPlayModeOptionsEnabled: 0` | Minutos posibles |
| ~219 ScriptAssemblies | Meta XR + MRUK + Interaction + OpenXR + Crest + MCP + DA… | Reload largo |
| Library ~13 GB | Samples MRUK + Crest + imports | I/O lento |
| Crest `ExecuteDuringEditMode` | Water activo en Scene view | Editor “pesado” sin Play |
| Shader variants Crest | ~66 shaders/graphs | Primera compilación larga |
| Assets no usados en escena | `MRUKSamples` ~189 MB, `DA-Assets`, Hot Reload suelto | Inflan el proyecto |

---

## 3. Lo que ya está bien

- **Stereo Multiview** (`m_StereoRenderingPath: 2`).
- **Color space Linear**.
- **Android scripting IL2CPP**, arquitectura ARM64.
- **Vulkan** en Android (`m_APIs: 0b000000`).
- **HDR URP OFF**.
- Crest **Reflections OFF**, **Underwater OFF** en el prefab actual.
- Shadow distance URP corta (**4 m**) — alineado con interior de bridge.
- SRP Batcher ON.
- Seakeeping: pitch/roll del exterior desactivados (menos pelea CPU/visual con Crest plano).

---

## 4. Sugerencias de mejora (priorizadas)

### P0 — Mayor ganancia en Quest (GPU)

1. **Bajar Render Scale a 0.8–1.0** (ahora 1.2).  
   - Probar 1.0 primero; si hace falta margen, Dynamic Resolution / eye buffer scale de Meta.

2. **Perfil Crest “Quest”** (duplicar `CrestOcean.prefab` o overrides):  
   - `_Resolution`: 384 → **128 o 192**  
   - `_Slices`: 9 → **5–6**  
   - ShapeFFT `_WindSpeed`: 20 → **6–10**, `_Resolution` FFT → **64**  
   - Desactivar **Shadow Lod** si las sombras del agua no se notan desde el bridge  
   - Desactivar **Meniscus** si no se usa transición underwater  
   - Desactivar **Foam Lod** o bajar resolución a 128  
   - `WriteMotionVectors` OFF si no hay TAA/motion blur  
   - Valorar `Opaque Downsampling` 2× si se mantiene Opaque Texture (Crest recomienda None solo si underwater está ON)

3. **MSAA:** mantener 4× solo si el frame aguanta; si GPU bound, bajar a **2×** y compensar con sharpening suave / render scale estable. (Meta recomienda 4× en general; con Crest puede ser excesivo.)

4. **Terrain:**  
   - Subir `terrainPixelError` (p.ej. 5–12) en Quality Android  
   - Bajar `terrainBasemapDistance` / detail distance  
   - Considerar mesh low-poly de costa en lugar de Terrain completo si solo se ve desde ventanas

### P1 — Passes y overdraw

5. **Auditar Renderer Features:** desactivar Stencil/UI features que BridgeRoom no use.  
6. **Interior:** materials simples (URP/Simple Lit o Unlit) en paredes generadas; evitar Lit con muchas maps.  
7. **Occlusión:** ya hay wall occluders; asegurar que el exterior no se dibuje tras bulkheads (depth). Verificar que casco no se renderice si no se ve desde ventanas (layer/culling).

### P2 — CPU / memoria / arranque

8. **Enter Play Mode Options** (solo Editor): habilitar y desactivar *Reload Domain* cuando sea seguro → Play mucho más rápido.  
9. **Sacar del proyecto** (o mover fuera de `Assets`) lo no usado en builds: gran parte de `MRUKSamples`, `DA-Assets`, Hot Reload no referenciado.  
10. **Prewarm shaders** Crest en splash / primera escena.  
11. **NavigationSim:** limitar `timeScale` en dispositivo; no construir paneles hasta que el usuario los abra (ya es lazy al Open — mantenerlo).  
12. **Evitar `FindAnyObjectByType` en hot paths** (hoy mayormente en setup; OK).

### P3 — Validación con datos reales

13. En headset: **OVR Metrics Tool** + **Perfetto** (`metavr` / skill perfetto) para confirmar GPU vs CPU bound.  
14. Contar draw calls / tris con Frame Debugger o RenderDoc Quest.  
15. Probar build Development con `ovr-perf` / AppSW off primero; luego Dynamic Resolution.

---

## 5. Perfil “Bridge Quest” sugerido (objetivo)

| Setting | Actual | Objetivo Quest |
|---------|--------|----------------|
| URP Render Scale | 1.2 | **0.9–1.0** |
| URP MSAA | 4 | **2 o 4** (medir) |
| Opaque Texture | Full | Mantener si refracción; downsample **2×** si se puede |
| Crest Resolution | 384 | **128–192** |
| Crest Slices | 9 | **5–6** |
| Crest Shadow Lod | ON | **OFF** (prueba) |
| Crest Meniscus | ON | **OFF** (prueba) |
| ShapeFFT wind | 20 | **≤10** |
| Terrain pixel error | 1 | **≥5** |
| Enter Play Mode (Editor) | Domain reload | **Sin domain reload** en iteración |

---

## 6. Mapa de responsabilidades (dónde tocar)

| Qué | Dónde |
|-----|--------|
| MSAA / scale / depth-opaque | `Assets/Universal Render Pipeline Asset.asset` |
| Passes extra | `Assets/Universal Render Pipeline Asset_Renderer.asset` |
| Crest coste | `Assets/ShipBridgePrototype/Prefabs/CrestOcean.prefab` |
| Nivel del mar / sync | `CrestOceanBootstrap.cs` |
| Movimiento exterior | `ExteriorWorldMotion.cs` |
| Terrain | `Scenario_CoastalMountains.prefab` + QualitySettings |
| Domain Reload Editor | `Project Settings → Editor → Enter Play Mode Options` |
| Paquetes pesados | `Packages/manifest.json`, carpetas `MRUKSamples` / `DA-Assets` |

---

## 7. Riesgos al optimizar

- Bajar demasiado Crest resolution → olas “plásticas” o popping de LOD.  
- Quitar Opaque/Depth Texture → refracción rota o superficie rara.  
- MSAA 2× → shimmering en cables/UI; compensar con buen AA de UI.  
- Domain Reload off → bugs raros de estado estático en scripts; re-enable al debuggear rarezas.  
- Terrain más rough → silueta de montañas peor; aceptable desde ventanas lejanas.

---

## 8. Plan de trabajo recomendado

1. **Medir** en Quest 3 (OVR Metrics: GPU % / App CPU / FPS / Latencia).  
2. **Aplicar P0** (scale + Crest Quest profile) en una rama.  
3. **Re-medir**; si sigue GPU bound → MSAA 2× + terrain pixel error.  
4. **Editor:** Enter Play Mode Options + limpiar assets muertos.  
5. Documentar el perfil final en este archivo cuando se fijen números.

---

## 9. Conclusión

Lo más caro del producto en runtime es la **combinación Crest (resolución/cascadas altas) + URP a Render Scale 1.2 con MSAA 4× y copias Depth/Opaque**.  
Lo más caro del flujo de desarrollo es el **Domain Reload** con el stack Meta/Crest/MCP.

Sin mediciones en dispositivo esto es priorización por evidencia estática; el siguiente paso útil es un Perfetto / OVR Metrics de 30–60 s mirando al exterior por las ventanas delanteras (peor caso: Crest + Terrain juntos).

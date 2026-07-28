# Informe: caída de FPS al abrir el radar

**Fecha:** 2026-07-26
**Alcance:** sólo investigación. No se ha modificado código.
**Método:** lectura de `BridgeRadarDisplay.cs` / `BridgeChartDisplay.cs` y conteo instrumentado
de las operaciones que ejecuta `DrawPpi` por refresco, con los ecos reales que produce
`RadarModel` sobre el escenario costero. No se ha perfilado en el visor: los tiempos en
milisegundos son estimaciones derivadas del conteo, los conteos son exactos.

---

## 1. Resumen

El coste no está en el modelo de radar ni en el número de ecos. Está en cómo se pinta el
PPI: **313.000 llamadas nativas a `Texture2D` por refresco**, de las cuales el 95 % repintan
un fondo que no cambia.

| Concepto | Por refresco | Por segundo (8,3 Hz) |
|---|---:|---:|
| Llamadas nativas a textura | 312.995 | 2.607.248 |
| Basura al GC (`GetPixels`) | 6,25 MB | 52,1 MB/s |
| Subida a GPU (`Apply`) | 1,56 MB | 13,0 MB/s |

Desglose de las llamadas nativas:

| Origen | Llamadas | % |
|---|---:|---:|
| Máscara circular de fondo | 296.505 | 94,7 % |
| Anillos de distancia | 5.872 | 1,9 % |
| `GetPixel` dentro de `DrawLine` | 7.484 | 2,4 % |
| Barrido / EBL / VRM | 1.612 | 0,5 % |
| **Ecos (101, incluidos 96 de tierra)** | **1.493** | **0,5 %** |

### El número de ecos no es la causa

Conviene descartarlo explícitamente: los 101 ecos —incluidos los 96 puntos de costa que
ahora genera `ScenarioLandmassSampler`— suponen el **0,5 %** del coste. Reducirlos no
cambiaría nada perceptible. El problema es el fondo.

### La misma cantidad de píxeles, escrita bien, es gratis

Las 296.505 escrituras de la máscara circular, hechas sobre un array plano, tardan
**1,49 ms** (x64 de escritorio, 50 repeticiones). El coste real no viene de cuántos píxeles
hay, sino de que cada uno cruza la frontera managed→native:

```csharp
_tex.SetPixel(x, y, bg);   // una llamada de interop por píxel
```

`Texture2D.SetPixel` y `GetPixel` no están pensados para bucles: cada llamada valida
argumentos, resuelve el nivel de mip y entra en código nativo. A 2,6 millones de llamadas
por segundo, esto es el frame presupuesto entero de un Quest varias veces.

---

## 2. Problemas concretos localizados

**P1 — El fondo estático se repinta entero cada refresco.**
`DrawPpi` limpia, rellena la máscara circular y redibuja los cuatro anillos en cada pasada
([BridgeRadarDisplay.cs:324-356](../Assets/NavigationSim/Runtime/UI/BridgeRadarDisplay.cs#L324-L356)).
Máscara y anillos sólo dependen del tamaño del PPI y de la escala seleccionada: entre
cambios de rango son idénticos, y suponen el 96,6 % del trabajo.

**P2 — `GetPixels()` asigna 6,25 MB por refresco.**
Devuelve un `Color[]` (4 floats por píxel) que se llena de `Color.clear` y se vuelca con
`SetPixels`. Son 52 MB/s de basura; en Quest, con GC incremental, se traduce en microcortes
periódicos además del coste base.

**P3 — `DrawLine` lee la textura por píxel.**
Hace `_tex.GetPixel(x0, y0)` para decidir la mezcla
([BridgeRadarDisplay.cs:531](../Assets/NavigationSim/Runtime/UI/BridgeRadarDisplay.cs#L531)),
duplicando el interop en cada línea.

**P4 — El pico cae en `Update()`, en el hilo principal.**
`Refresh()` se llama desde `Update` justo antes del render, sin repartir. En VR un pico así
provoca pérdida de frame y reproyección visible, que es exactamente la sensación de "va más
lento" aunque el promedio de FPS no se desplome.

**P5 — La carta tiene el mismo patrón y es peor.**
`BridgeChartDisplay` usa `GetPixels` / `SetPixel` / `DrawLine` sobre **720×720** (1,27× los
píxeles del PPI) refrescando cada 0,15 s. Con radar y carta abiertos a la vez el coste se
suma. Cualquier solución debería cubrir ambos.

**P6 — El barrido se ve a saltos.**
`_sweepDeg` avanza a 72 °/s pero sólo se dibuja a 8,3 Hz, así que salta 8,7° por refresco.
Subir la cadencia con el diseño actual es inviable; con la alternativa D sale gratis.

---

## 3. Alternativas

### A. Escribir en el buffer nativo de la textura *(recomendada, primera)*

Sustituir `SetPixel`/`GetPixel` por escritura directa sobre
`_tex.GetRawTextureData<Color32>()` (o un `Color32[]` propio volcado con `SetPixels32`),
y `Apply(false)` una sola vez al final.

- Elimina las 313.000 llamadas de interop → indexación de array.
- Elimina los 6,25 MB de `GetPixels` (se escribe in situ, sin copia intermedia).
- Cambio mecánico: `SetPixel(x,y,c)` pasa a `buf[y*w + x] = c32`.
- **Riesgo bajo.** Ojo con dos detalles: la fila 0 es la inferior, y `Color32` es
  8 bits por canal (suficiente para un PPI).
- Esfuerzo estimado: 1–2 h para radar y carta.

### B. Cachear el fondo estático *(recomendada, junto con A)*

Precomputar máscara circular + anillos en un buffer aparte cuando cambia el rango o el
tamaño, y por refresco sólo hacer `Array.Copy` (memcpy de 1,6 MB, ~0,1 ms) antes de pintar
los elementos dinámicos.

- Con A, el refresco baja de ~313.000 operaciones a **~10.000**.
- Variante más limpia: **dos `RawImage` superpuestos** — uno de fondo, generado una vez y
  nunca tocado, y otro transparente encima sólo con ecos, barrido y vectores. Así ni
  siquiera se copia el fondo.

### C. Bajar la resolución del PPI *(complementaria, coste cero)*

Hoy son 640×640 = 409.600 píxeles. A 1,75 m de distancia sobre un panel de 640 px, 512²
(−36 %) o 384² (−64 %) son indistinguibles en el visor. Es cambiar una constante y se
combina con todo lo demás.

### D. Mover el PPI a la GPU *(solución definitiva)*

Máscara, anillos, barrido, EBL y VRM son analíticos en un fragment shader: distancia al
centro y `atan2`. Los ecos entran como array de uniforms (~64–128) o como textura pequeña
de posiciones.

- Coste de CPU: cero. Coste de GPU: despreciable para un quad.
- **El barrido pasa a animarse a la tasa del visor**, resolviendo P6 de paso.
- Permite refrescar los ecos a 8 Hz y aun así ver el barrido fluido.
- Más esfuerzo: shader + binding de datos. Es lo que hace un ECDIS/radar real en software.

### E. Geometría en vez de píxeles

Anillos y líneas como malla procedural o `Graphic` de UI personalizado; ecos como quads
instanciados. Evita escribir shader, pero hay que vigilar el número de draw calls en Quest,
donde cada elemento de UI que rompe el batch cuesta.

### F. Burst / Jobs sobre el buffer nativo

Paralelizar el pintado sobre `NativeArray<Color32>`. El proyecto ya usa Burst para el
océano. Probablemente **innecesario** si se aplican A+B: el coste restante es tan bajo que
no justifica la complejidad.

### G. Repartir el dibujo entre frames

Pintar por bandas (1/4 de textura por frame). Reduce el pico pero no el coste total, y
complica la coherencia temporal del PPI. Sólo tiene sentido si no se quiere tocar la lógica
de dibujo, cosa que A ya hace sin efectos secundarios.

### H. Bajar la cadencia de refresco

De 0,12 s a 0,25 s. No ataca la causa, reduce el coste a la mitad y empeora la sensación de
instrumento vivo. Descartable salvo como parche temporal.

---

## 4. Recomendación

1. **A + B + C**, en ese orden y en una sola tanda. Bajo riesgo, sin cambiar el aspecto, y
   se lleva por delante prácticamente todo el coste: de 313.000 llamadas nativas y 52 MB/s
   de GC por segundo a unas 10.000 escrituras de array y cero asignaciones.
2. **Extraer un helper compartido** (un `PixelCanvas` con buffer nativo, `DrawLine`,
   `DrawCircle`, `FillDisk`) y hacer que radar y carta lo usen. Hoy la lógica de dibujo está
   duplicada en los dos ficheros, así que arreglar sólo uno deja el problema a medias.
3. **D** cuando se quiera el barrido fluido y coste de CPU nulo. Es un trabajo aparte y con
   sentido propio, no una urgencia.

## 5. Qué falta por saber

- No hay medición en el visor. Antes de dar por bueno el arreglo conviene una captura del
  **Unity Profiler en el Quest** con el radar abierto y cerrado, mirando `Scripts` en el
  hilo principal y `GC.Alloc`. El conteo de este informe predice qué debería desaparecer.
- No se ha revisado si `BridgeInstrumentCanvas` fuerza reconstrucciones de Canvas al
  actualizar los `TMP_Text` cada refresco. Es un coste independiente del pintado, más
  pequeño, pero conviene comprobarlo en la misma captura.

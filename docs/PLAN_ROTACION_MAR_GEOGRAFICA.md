# Plan — Hacer que el mar gire con el mundo (muestreo geográfico del campo iFFT)

> Estado: diagnóstico verificado contra el código el 2026-07-28. Rama `performance/quest-pass`.
> Nada de esto está implementado todavía en el Shader Graph; el lado C# está escrito y desactivado.

## 1. Síntoma

Desde el puente, la costa y el tráfico giran correctamente al caer a estribor o a babor,
pero **el patrón de olas no gira con ellos**: conserva su propia orientación respecto al
buque. En una caída de 90° el mar de través pasa a ser mar de proa en la costa, pero las
crestas siguen viéndose igual desde las ventanas. La traslación (avante/atrás) sí funciona.

## 2. Diagnóstico

Son dos sistemas de movimiento distintos y solo uno sabe girar.

**El exterior gira porque se le rota el `Transform`.** `ExteriorWorldMotion` aplica la
transformación inversa completa —posición *y* rotación— a `ExteriorWorldRoot`, del que
cuelgan costa, islas y tráfico (`ExteriorWorldMotion.cs:332-336`).

**El mar no es geometría de ese árbol.** `OceanRoot` se crea suelto en la escena
(`NorthStarOceanAdapter.cs:435`) y cada frame se le reimpone rotación identidad
(`NorthStarOceanAdapter.cs:834-835`). Aunque se rotase no serviría: las olas no están en
los vértices sino en una textura de desplazamiento iFFT que el shader muestrea por
`XZ mundial × _OceanRcpScale`. La malla del quadtree es un lienzo con LOD centrado en el
observador; rotar el lienzo deja el dibujo donde estaba.

**La traslación está resuelta; la rotación no tiene mecanismo.** El avance se inyecta como
corrimiento de fase espectral `h(k) *= e^(iK·D)` vía `FieldOffset`
(`OceanSimulation.cs:46-51`, `NorthStarOceanAdapter.cs:267`), que es una traslación rígida
exacta del campo. Un corrimiento de fase **solo traslada**: no existe término análogo que
lo rote.

## 3. Alternativas descartadas (no reabrir)

| Alternativa | Por qué no |
|---|---|
| Contrarrotar el vector de viento con el rumbo | Solo repondera qué direcciones llevan energía en el espectro de Phillips: el patrón se disuelve y se rehace, no barre. Es lo que hace hoy `BuildWindVector()` y por eso hay que cuantizarlo a 1° (cada cambio reconstruye el espectro N² completo) |
| Rotar `OceanRoot` y muestrear en espacio objeto | `QuadtreeRenderer` construye las matrices de parche con `Quaternion.identity` en espacio mundo absoluto (`QuadtreeRenderer.cs:274`), ancla la rejilla al `snap` de la cámara en ejes mundo (`:98-100`) y dibuja 4 faldones a 0/90/180/270 fijos (`:149-152`). Las matrices no descienden del `Transform`, así que rotarlo no rota nada |
| Meter el océano bajo `ExteriorWorldRoot` | La malla debe seguir al observador para que el LOD tenga sentido, y el exterior se traslada decenas de km |

## 4. Solución: muestrear el campo en ejes geográficos

En vez de mover el campo, se mueve el **mapa de muestreo**. El shader recibe la
transformación sala→geografía y muestrea ahí; el campo queda anclado al mundo y es el
observador quien gira dentro de él.

El lado C# ya existe y está desactivado: `geographicFieldSampling` +
`PublishGeographicFrame()` (`NorthStarOceanAdapter.cs:287-323`), que publica

```
_OceanGeoRotation = (c, s, ox, oz)
u =  c*x + s*z + ox
v = -s*x + c*z + oz
```

**Falta la otra mitad.** `_OceanGeoRotation` solo aparece en el C#; el grafo
*Water Realistic* únicamente declara `_OceanRcpScale`. Hoy el flag está en `false` y el
componente se añade en runtime (`AddComponent` en `EnsureInstance`), así que no hay valor
serializado en escena. **Si se activa tal cual, el mar se congela**: la línea 315 pone
`FieldOffset = Vector2.zero` (el movimiento debería viajar en el mapa) y el shader ignora
un mapa que nunca recibe. Es exactamente lo que advierte el tooltip de las líneas 71-75.

### 4.1. Las dos rotaciones inversas (lo que el C# aún no cubre)

La textura almacena desplazamiento `(Dx, h, Dz)` y normales **en los ejes del campo**, que
ahora son geográficos. Al dibujar en la sala hay que devolverlos con `R⁻¹`:

```
R   = [[ c, s], [-s, c]]        (sala → geografía)
R⁻¹ = [[ c,-s], [ s, c]]        (geografía → sala)

desplazamiento_sala.xz = ( c*Dx - s*Dz ,  s*Dx + c*Dz )
normal_sala.xz         = ( c*Nx - s*Nz ,  s*Nx + c*Nz )
```

La altura `h` y la componente `y` de la normal son invariantes.

Omitir esto es un fallo **muy visible en VR**: las crestas choppy se inclinarían en la
dirección equivocada y, sobre todo, el reguero de sol giraría con el buque en vez de
quedarse en el azimut solar.

## 5. Pasos de implementación

1. **Declarar la propiedad en el grafo.** En `Water Realistic.shadergraph`, `Vector4`
   expuesto, reference name `_OceanGeoRotation`, valor por defecto `(1, 0, 0, 0)`.
   Mismo tratamiento que `_OceanRcpScale`: **no** marcar Global — llega por
   `MaterialPropertyBlock` en `OnBeginContextRendering` (`NorthStarOceanAdapter.cs:882`),
   igual que las propias texturas. La identidad `(1,0,0,0)` reproduce el muestreo actual,
   así que el grafo se puede publicar antes de tocar el C#.

2. **Insertar el mapa 2D antes de la división por el parche.** Sustituir
   `Position(World).xz` por `(c*x + s*z + ox, -s*x + c*z + oz)` en los **dos** sitios que
   muestrean el campo:
   - grupo *"Flatten waves past a certain distance from the camera"* → `_OceanDisplacement`
     (vértice, `SampleTexture2DLODNode`);
   - grupo *"OceanNormal (World Space)"* → `_OceanNormal` (fragmento).

   Conviene encapsularlo en un `.shadersubgraph` (p. ej. `Ocean Geo Frame.shadersubgraph`)
   para no duplicar el cableado y que ambos puntos no puedan divergir.

3. **Rotar de vuelta las salidas vectoriales** con `R⁻¹` según §4.1: `.xz` del
   desplazamiento y `.xz` de la normal. La `y` no se toca.

4. **Activar el camino en C#.** Poner `geographicFieldSampling = true` por defecto en
   `NorthStarOceanAdapter`. Como el componente se crea por `AddComponent`, el valor por
   defecto del campo *es* la configuración efectiva; no hace falta tocar escenas.

5. **Verificar la coherencia de la sonda.** `SampleHeight()` ya tiene su rama geográfica
   (`NorthStarOceanAdapter.cs:372-383`), que muestrea el punto de consulta directamente sin
   pasar por la sala. Debe seguir siendo el inverso exacto de lo que recibe el shader,
   incluida la resta del arrastre de corriente: si divergen, el tráfico flota por encima o
   por debajo de la ola sobre la que va.

6. **Retirar la reconstrucción de espectro por grado.** La rama geográfica de
   `BuildWindVector()` (`:718-727`) ya entrega el viento en ejes geográficos, con lo que el
   vector solo cambia cuando el instructor ordena otra mar. Se acaba el rebuild N² de
   Phillips durante cada caída — ganancia directa para la rama `performance/quest-pass`.

7. **Detalle menor a corregir de paso.** En `LateUpdate`, `SetVirtualShipPosition()` corre
   antes que `SyncSeaState()`, así que en el frame en que cambia el estado de la mar el
   `Mathf.Repeat(ox, _patchSize)` envuelve contra el parche *anterior* mientras
   `OnBeginContextRendering` ya publica el nuevo `_OceanRcpScale` → salto de todo el campo
   durante un frame. Basta invertir el orden de las dos llamadas.

## 6. Verificación

**Instrumentación.** `logFieldMotion` (`ReportFieldMotion()`) hoy solo mide traslación
—`commanded` vs `committed` en m/s—. Añadir el ángulo del marco publicado
(`atan2(s, c)` en grados) junto al rumbo, para poder afirmar con números que el marco gira
y con qué desfase.

**Pruebas manuales, todas con el buque parado sobre el fondo salvo donde se indique:**

| Prueba | Resultado esperado |
|---|---|
| Mar 4, viento del 090, caída de 90° a estribor | Las líneas de cresta mantienen su orientación **geográfica**: si estaban paralelas a la costa, siguen paralelas a la costa durante y después de la caída |
| Mismo caso, mirando el reguero de sol | Permanece en el azimut solar; no acompaña a la proa (valida §4.1) |
| Mar de proa → caída a mar de través | `WaveResponseModel` debe pasar de cabeceo dominante a balance dominante; la frecuencia de encuentro cambia con el rumbo |
| Todo avante en rumbo fijo | Las crestas pasan por el puente a STW × `timeScale`, como hoy — no se debe perder la traslación |
| Parado sobre el fondo con corriente | El mar pasa; a la deriva con la corriente, el mar queda quieto (comportamiento intencional de `ComputeWaterShift`) |
| Un remolcador del tráfico en la mar | Sigue pegado a la superficie durante toda la caída (valida el paso 5) |
| Derrota larga (> 50 km) | Sin saltos ni pérdida de precisión: `Mathf.Repeat` sobre el periodo del parche protege la mantisa float32 del shader |

## 7. Fuera de alcance

- **`_GiantWaveOffset`** (`GiantWave.hlsl:41`) sigue siendo un `SetGlobalVector` en ejes
  sala. Si alguna vez se usa la ola gigante con muestreo geográfico habrá que rotar también
  su centro. No bloquea nada: la característica no está en uso.
- **Actitud del exterior.** `applySeakeepingAttitudeToExterior` sigue en `false` y el océano
  sigue con rotación identidad; ninguno de los dos se inclina, a propósito, para que la
  costa no resbale sobre el agua. Solo comparten el canal vertical `_waterShiftY`.
- Estela, RAO avanzadas y rompientes en costa: son F4/F5 del
  [plan maestro](PLAN_MAESTRO_SIMULADOR_NAVAL.md).

## 8. Criterios de aceptación

1. Con `geographicFieldSampling = true`, el patrón de olas conserva su orientación
   geográfica durante una caída completa de 360°.
2. El reguero de sol y las crestas choppy no giran con la proa.
3. La traslación de crestas a rumbo fijo no se degrada respecto a hoy (`committed` ≈ STW ×
   `timeScale` en el log).
4. Cero reconstrucciones de espectro Phillips atribuibles a cambios de rumbo.
5. Sondas de altura coherentes con lo renderizado: el tráfico no flota ni se hunde en la
   caída.
6. Sin regresión de frame time en Quest respecto a la medición actual de la rama.

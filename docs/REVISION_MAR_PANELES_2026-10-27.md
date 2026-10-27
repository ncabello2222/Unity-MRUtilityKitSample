# Revisión del océano y los paneles

Base revisada: `b49614f16b1d4a0c197ec0c1677f35ed8f043d8a` de `main`.
Rama: `fix/ocean-panel-resource-lifecycle`.

## Lo que ya estaba corregido

El commit base ancla el muestreo del océano, espuma y normales a coordenadas
geográficas y evita reconstruir el espectro por cada grado de giro. También
corrige el signo de la resistencia del timón, incorpora amortiguación cuadrática
de guiñada y libera las mallas del quadtree, OceanRoot y el material del adaptador.
Esta revisión conserva esas modificaciones. La calibración física y el resultado
visual de esos cambios necesitan validación en ejecución; leer el código no los
certifica.

Ya existían FFT de 64 en Quest / 128 en PC, simulación del mar a 30 Hz, buffers
reutilizados en radar/carta, raster de 512², menor frecuencia de refresco y caché
de lecturas OpenBridge. No se redujeron resolución ni calidad visual.

## Cambios de esta rama

| Hallazgo | Cambio | Alcance esperado |
| --- | --- | --- |
| `OceanSimulation.OnEnable` inicializaba los recursos y después ponía la resolución cacheada a cero. El primer tick los destruía y recreaba. | Se conserva la resolución inicializada. | Elimina una reserva completa y una generación de tablas redundantes al habilitar el océano. No implica que existiera una fuga por fotograma. |
| `Initialize` público podía reemplazar recursos vivos; `Cleanup` asumía buffers existentes y no limpiaba el indicador de jobs pendientes. | Inicialización con limpieza previa; completar jobs antes de liberar; comprobar `IsCreated`; limpiar mapas, cachés y singleton. Destrucción diferida durante Play Mode. | Evita recursos huérfanos, doble liberación y uploads sobre texturas liberadas durante reinicios. |
| Radar y carta creaban `Texture2D` sin liberación explícita. Sus canvases pueden quedar fuera del padre original al acoplarlos a la consola. | Destruir texturas y raíces propias en `OnDestroy`; soltar buffers gestionados y referencia del RawImage. | Evita retención al destruir/recrear componentes o escenarios; cerrar un panel conserva su textura para reabrirlo. |
| Las tres agujas del radar rotaban en el Canvas que también contiene fondo y botones. | Subcanvas exclusivo para las agujas, conservando tamaño y posición. | Limita el trabajo de reconstrucción de UI causado por esas rotaciones; añade un Canvas, cuyo balance debe medirse en Quest. |
| Cerrar instrumentos y menú solo desactivaba el Canvas raíz. Sus colliders físicos y scripts de botones continuaban habilitados. | Caché de canvases, colliders y botones; deshabilitarlos al cerrar y restaurar sus estados al abrir, manteniendo activos los GameObjects. | Impide botones invisibles, desactiva explícitamente subcanvases y conserva las jerarquías precargadas. |
| Desactivar un botón mantenido no notificaba la liberación. | `OnDisable` libera la pulsación; `Press` rechaza botones deshabilitados y notifica la retención antes del click. | Evita dejar una orden NFU activa al cerrar configuración y evita reactivar la retención después de un click que cierra el panel. |
| Conning podía recibir `Open` mientras su coroutine todavía estaba construyendo la interfaz. | Esperar a `_built`, manteniendo la apertura pendiente. | La apertura usa una interfaz y una caché de visibilidad completas. |
| Reabrir radar/carta podía provocar otro refresco inmediatamente en el siguiente Update. | Reiniciar su temporizador tras el refresco de apertura. | Evita dos rasterizaciones consecutivas por la misma apertura. |
| Quadtree procesaba cámaras que excluyen su capa y recreaba matrices al habilitarse. | Respetar `cullingMask` antes de construir/enviar parches; reutilizar las matrices existentes. | Ahorra trabajo para cámaras que no muestran esa capa y asignaciones en reactivaciones. |

## Qué no se puede concluir sin medir

No se observó en los caminos revisados una reserva nueva de los buffers principales
del mar en cada frame. El coste percibido puede venir del shader/overdraw, de los
jobs FFT y su sincronización, de las subidas de texturas o de reconstrucciones de
Canvas. Las fugas y reinicios descritos arriba son distintos de ese coste sostenido.

La evolución de las olas continúa a 30 Hz mientras el visor puede ir a 72 Hz.
El mapa geográfico se actualiza por frame, pero reutiliza el último estado de las
olas: puede quedar una cadencia visible, especialmente con tiempo acelerado.
Interpolar estados de oleaje requeriría más texturas y cambios de shader; no se
añadió sin medir primero el coste y verificar las sondas de flotación.

Las fórmulas de giro no se modificaron nuevamente. Comparar a tiempo real (1x)
antes de valorar rapidez de giro; el simulador permite aceleración hasta 60x.

## Validación

- Revisión del diff, comprobación de whitespace y análisis sintáctico C#.
- Se añadieron tres pruebas en `Assets/Tests/BridgeResourceLifecycleTests.cs`:
  cierre/restauración de Canvas y botones con liberación de retención; ciclo del
  océano con jobs pendientes y cambio 64→128; destrucción de texturas y raíces
  acopladas de radar/carta.
- **Las pruebas de Unity no se ejecutaron en este entorno:** no hay Editor Unity
  6000.4.9f1 ni Quest. El análisis sintáctico no sustituye compilación, Burst,
  Shader Graph ni pruebas visuales. No se atribuye una mejora porcentual de FPS.

En Unity, ejecutar `NavigationSim.Tests.BridgeResourceLifecycleTests` en PlayMode.
Después comparar la misma escena en un Development Build de Quest, a 1x y 72 Hz:

1. Abrir/cerrar instrumentos, alternar los slots y reabrir Conning durante su
   precarga. Comprobar subcanvases, orden visual de agujas y ausencia de botones
   invisibles. Cerrar configuración mientras se mantiene NFU y confirmar neutral.
2. Comparar `Canvas.BuildBatch`, GC Alloc y tiempo CPU/GPU con radar abierto y
   cerrado. Confirmar que el fondo/botones no se reconstruyen por cada barrido.
3. Repetir la recreación del océano y paneles y comparar snapshots del Memory
   Profiler tras finalizar la destrucción. No debe acumular texturas/mallas propias
   por ciclo. Deshabilitar, habilitar y cambiar resolución sin errores de NativeArray.
4. Navegar recto y virar a velocidad sostenida, con referencias costeras visibles;
   comprobar que espuma, crestas, normales y flotación siguen siendo coherentes.

Referencia de la separación de Canvas y conservación de geometría al ocultar:
[Unity UI optimization tips](https://unity.com/es/how-to/unity-ui-optimization-tips).

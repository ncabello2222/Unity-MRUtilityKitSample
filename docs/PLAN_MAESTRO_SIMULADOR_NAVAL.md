# Plan maestro — Simulador de navegación (Ship Bridge VR)

> Estado: arquitectura verificada contra los repositorios reales el 2026-07-21.
> Repos de referencia clonados en `c:\unity\ship-sim-refs\` (fuera de `Assets/` para que Unity no los importe).

## Estado de implementación (2026-07-21)

**Implementado en `Assets/NavigationSim/`** — Fases 1, 2 y 3 completas, más el canal
visual de oleaje (parte de la F4) y el piloto automático HAND/NFU/AUTO (parte de la F5):

- Núcleo puro C# sin UnityEngine (`Runtime/Core|Dynamics|Propulsion|Environment|GNC`):
  MMG 3DOF portado de ShipMMG, Clarke 83 portado de PVS, RK4 propio a 50 Hz, actuador de
  timón con límite de velocidad, motor/eje con rampa y retardo de inversión, hélice KT/KQ,
  consumo SFOC, corriente relativa, viento Blendermann 94, respuesta visual heave/roll/pitch.
- Validado fuera de Unity con el Roslyn del editor (harness de consola): **7/7 tests** —
  equilibrio de velocidad = predicción de calibración, giro 35° KVLCC2 con diámetro táctico
  3.36 L (real ≈ 3.3 L), crash stop 13.4 L con inversión de eje, deriva por corriente exacta
  (SOG≠STW), abatimiento por viento, Clarke 83 estable y autopiloto con error < 1°.
- Capa Unity: `NavigationSimRunner` (50 Hz + interpolación) creado por bootstrap sin tocar la
  escena; `ExteriorWorldMotion` ahora lee el núcleo (el integrador arcade fue sustituido);
  el núcleo gobierna el ángulo real de timón de `ShipControlState`.
- Canvas de configuración mundial (botón **B** del mando derecho, o tecla B en editor) con
  6 pestañas: BUQUE (presets KVLCC2/costero, modelo, dimensiones Clarke), MOTOR (RPM, MCR,
  rampas, inversión, combustible MDO/HFO/LNG, e-stop), HÉLICE (KT/KQ, estela, thruster),
  GOBIERNO (HAND/NFU/AUTO, rumbo AP, ganancias, bombas), ENTORNO (corriente, viento, olas)
  e INSTRUM. (panel vivo completo). Puntero láser propio por raycast físico.

**Tiempo acelerado (fast-time)**: un VLCC laden tarda ~12 min en coger velocidad de
servicio y ~4 min en girar 90° — física correcta pero imperceptible en VR. `NavigationSimRunner`
corre el núcleo determinista N× más rápido (por defecto 16×, ajustable 1–60× desde el canvas,
pestaña BUQUE), preservando todas las proporciones. El buque por defecto es ahora el **costero
genérico** (ágil, respuesta perceptible en 2-3 s); el KVLCC2 calibrado sigue a un clic. El
integrador sub-pasa hasta 512 pasos de 0.02 s por frame para soportar 60×.

**Pendiente**: océano render (F4: adaptador Ocean CNG o material URP propio con fase),
estela, RAO avanzadas, fallos inyectables adicionales y otros buques (F5).

---

## 1. Resumen ejecutivo

El objetivo es convertir el prototipo actual (`Assets/ShipBridgePrototype/`) en un simulador
de puente de gobierno con física naval creíble, manteniendo el patrón ya prototipado de
**puente fijo + mundo que se mueve inversamente** (apto para VR/MR en Quest).

La autoridad física será un **núcleo matemático determinista en C#** (double + RK4 a paso fijo),
portado desde ShipMMG y complementado con Vessel.js, Python Vehicle Simulator y MSS.
Unity **no** calcula el movimiento del buque propio (sin Rigidbody); solo lo representa.

| Área | Sistema elegido | Función |
|---|---|---|
| Maniobra principal | **ShipMMG** (MMG 3DOF) | Avance, deriva, guiñada, casco+hélice+timón |
| Modelo genérico alternativo | **PVS shipClarke83** | Buques sin coeficientes MMG calibrados |
| Resistencia y calibración | **Vessel.js HullResistance** (Holtrop) | Curva resistencia–velocidad, calibración de `R_0'` |
| Hélice, potencia, combustible | **Vessel.js PropellerInteraction + FuelConsumption** | RPM, empuje, potencia de eje, SFOC |
| Viento y oleaje | **MSS (Fossen)** | blendermann94/isherwood72, espectros y RAO |
| Mar gráfico | **Ocean Community Next Gen** | Render, altura visual y fase de olas (tras adaptador) |
| Referencia externa | **ShipNetSim** | Solo comparación de resultados (GPL-3.0, no integrar) |
| Descartados | FRyDoM, OSP | Complejidad excesiva para esta fase |

---

## 2. Verificación de repositorios (hecho)

Todos los archivos y funciones citados en el análisis original **existen y fueron verificados**:

| Repo | Licencia (verificada) | Archivos clave confirmados |
|---|---|---|
| `ShipMMG/shipmmg` | **MIT** (2021 Taiga Mitsuyuki) | `shipmmg/mmg_3dof.py` (1515 líneas), `shipmmg/kt.py`, `tests/test_mmg_3dof.py` |
| `cybergalactic/PythonVehicleSimulator` | **MIT** | `src/python_vehicle_simulator/vehicles/shipClarke83.py`, `vehicles/frigate.py`, `lib/mainLoop.py`, `lib/gnc.py` |
| `shiplab/vesseljs` | **MIT** (2017 shiplab) | `source/classes/HullResistance.js` (403 l.), `PropellerInteraction.js` (80 l.), `FuelConsumption.js` (153 l.), `WaveMotion.js` (199 l.) |
| `cybergalactic/MSS` | **MIT** (2004 Thor I. Fossen) | `LIBRARY/environment/*.m`, `LIBRARY/numericalMethods/rk4.m` |
| `eliasts/Ocean_Community_Next_Gen` | **"Do whatever you want"** | `Assets/Ocean/Scripts/Ocean.cs`, `Boyant.cs`, `Buoyancy.cs`, `BoatController.cs` |
| `VTTI-CSM/ShipNetSim` | **GPL-3.0** ⚠️ | `src/ShipNetSimCore/ship/{holtropmethod,langmaomethod,ship,shipengine,shipfuel,shipgearbox,shippropeller}.cpp` |

### Correcciones al análisis original (halladas en la verificación)

1. **Rutas MSS**: las funciones no están en `MSS/LIBRARY/` directamente sino en
   `MSS/LIBRARY/environment/` (viento y oleaje) y `MSS/LIBRARY/numericalMethods/rk4.m`.
2. **Ocean**: `canCheckBuoyancyNow` es `byte[]`, no `bool[]` (`Ocean.cs:92`). `Singleton` está en
   `Ocean.cs:157`, `followMainCamera` en `:198`, `GetWaterHeightAtLocation2` en `:1976`,
   `GetChoppyAtLocation2` en `:2010`, `GetHeightChoppyAtLocation2` en `:2042`.
3. **Riesgo nuevo — pipeline de render**: Ocean Community Next Gen es un proyecto de
   **Unity 5.6.6f2** con shaders del pipeline integrado (`Ocean.shader`, `OceanL1-3.shader`).
   Este proyecto usa **URP**. Habrá que portar/reescribir los shaders del océano a URP o
   sustituir el render conservando solo la malla CPU + `GetWaterHeightAtLocation2` (la parte
   valiosa para gameplay). Presupuestar esto en la Fase 4.
4. **Bonus — KVLCC2 L7**: `shipmmg/tests/test_mmg_3dof.py:27-128` contiene el juego **completo**
   de parámetros calibrados del KVLCC2 escala L7 (básicos + maniobra), más tests de giro 35° y
   zigzag. Será el primer buque y la base de los tests de regresión en C#.
5. **Licencias**: la hipótesis del análisis se confirma — los cuatro repos a portar son MIT;
   ShipNetSim es GPL-3.0 y queda solo como herramienta externa de comparación.

---

## 3. Estado actual del proyecto y mapeo

`Assets/ShipBridgePrototype/` ya contiene un prototipo funcional con:

| Script actual | Qué hace hoy | Destino en la nueva arquitectura |
|---|---|---|
| `ShipControlState.cs` | Estado singleton: telégrafo (9 detentes), timón comandado/real (±35°, seguimiento a 12°/s), bow thruster, horn, e-stop, anclas | Se conserva como **BridgeControlState** (capa Controls). El seguimiento del timón se muda a `RudderActuator` con límite de velocidad angular real |
| `ExteriorWorldMotion.cs` | Integrador arcade (surge + yaw ∝ sin δ) y pose inversa del exterior por matrices | Se divide: la integración pasa al núcleo (`Mmg3DofModel` + `Rk4Integrator`); la pose inversa se conserva como base de `WorldMotionDriver` |
| `ExteriorWorldRoot.cs` | Raíz del mundo exterior con pivote | `WorldMotionRoot` |
| `SteeringWheelControl.cs`, `EngineTelegraphControl.cs`, `BowThrusterControl.cs` | Controles físicos VR | Se conservan; escriben en `BridgeControlState` |
| `RudderAngleIndicator.cs`, `NavigationPanel.cs`, `NavigationPanelSpawner.cs` | Instrumentación | Se amplían con los indicadores de §10 |
| `BridgeRoomMapper.cs`, `CoastalExteriorScenarioBuilder.cs`, `ExteriorScenarioCatalog.cs`, `ExteriorScenarioLoader.cs` | MRUK + escenarios | Se conservan |

El patrón de movimiento inverso **ya está resuelto y probado** en el prototipo (captura de pose
inicial, delta del buque virtual, `shipDelta.inverse * initialExteriorWorld`). El trabajo nuevo es
sustituir el integrador arcade por el núcleo MMG y añadir doble precisión + convención N/E/ψ.

---

## 4. Núcleo físico: MMG 3DOF portado a C#

### 4.1 Estado del buque

```
u    velocidad longitudinal (m/s, respecto al agua)
v    velocidad lateral (m/s)
r    velocidad angular de guiñada (rad/s)
N,E  posición global (m, double)
ψ    rumbo (rad)
δ    ángulo real de timón (rad)
n    revoluciones reales de hélice (rps)
```

Es exactamente el vector de estado de `mmg_3dof_eom_solve_ivp(t, X)` en
`shipmmg/mmg_3dof.py:862-955`, que es la función a portar línea a línea:

- `U = √(u² + (v − r·x_G)²)`, `β = asin(−(v − r·x_G)/U)`, `v' = v/U`, `r' = r·L/U`
- Estela: `w_P = w_P0 · exp(−4(β − x_P·r')²)`
- Hélice: `J = (1−w_P)·u / (n·D_p)`, `K_T = k_0 + k_1·J + k_2·J²`
- Timón: `β_R = β − l_R·r'`, `γ_R` asimétrico (γ_R⁻/γ_R⁺), `v_R = U·γ_R·β_R`,
  `u_R` con la fórmula de κ/ϵ/η (incluye la rama especial `J = 0` para arrancada desde parado),
  `F_N = ½·ρ·A_R·f_α·U_R²·sin(α_R)`
- Fuerzas: `X_H` (incluye `−R_0'` — ver §5), `X_P = (1−t_P)·ρ·K_T·n²·D_p⁴`,
  `X_R = −(1−t_R)·F_N·sin δ`, `Y_H`, `Y_R = −(1+a_H)·F_N·cos δ`, `N_H`,
  `N_R = −(x_R + a_H·x_H)·F_N·cos δ`
- Aceleraciones con acoplamiento sway/yaw (masas añadidas `m_x, m_y, J_z`, `x_G ≠ 0`)
- Cinemática global: `dN = u·cosψ − v·sinψ`, `dE = u·sinψ + v·cosψ`, `dψ = r`

### 4.2 Parámetros

Portar los dataclasses como structs C# serializables:

- `Mmg3DofBasicParams` (`mmg_3dof.py:27-113`): L_pp, B, d, x_G, D_p, m, I_zG, A_R, η, m_x, m_y,
  J_z, f_α, ϵ, t_R, x_R, a_H, x_H, γ_R±, l_R, κ, t_P, w_P0, x_P.
- `Mmg3DofManeuveringParams` (`mmg_3dof.py:117-168`): k_0..k_2, R_0', y las 15 derivadas
  hidrodinámicas X/Y/N (referencia: Yasukawa & Yoshimura 2015, MMG standard method).

### 4.3 Qué NO portar

SciPy/`solve_ivp`, los splines cúbicos de entrada (`CubicSpline` de δ y nps — en el simulador las
entradas vienen de los actuadores, no de listas temporales), gráficos y helpers de trayectoria.
Sustituir por `Rk4Integrator.cs` propio: **50 Hz, dt fijo = 0.02 s, double**. El render del Quest
(72–120 Hz) interpola visualmente entre estados físicos.

`shipmmg/kt.py` (modelo Nomoto K-T de 1er orden) se porta solo como modelo de pruebas del
piloto automático, no como maniobra principal.

A las sumas de fuerzas del MMG se añaden los términos externos: `X += X_wind + X_ext`,
`Y += Y_wind + Y_thruster`, `N += N_wind + N_thruster` (§8).

---

## 5. Regla crítica: no duplicar la resistencia

ShipMMG **ya incluye** la resistencia de marcha recta dentro de `X_H` (término `−R_0'`,
`mmg_3dof.py:902`). Por tanto la resistencia Holtrop de Vessel.js **nunca se suma por frame**.

`HullResistance.js` (Holtrop; propiedades `coefficients`, `calmResistance`, `totalResistance`,
`efficiency`) se usa **offline / en calibración**:

1. Generar la curva resistencia–velocidad del casco.
2. Estimar potencia necesaria y velocidad máxima de equilibrio.
3. **Ajustar `R_0'`** de ShipMMG para que empuje = resistencia a la velocidad de servicio.
4. Validar que el equilibrio empuje/resistencia ocurre a la velocidad correcta.

Su término de resistencia añadida por olas (`totalResistance` usa una corrección simple por
altura de ola) es demasiado básico para mar gruesa; no se convierte en modelo principal.

Síntomas de duplicación (test de aceptación negativo): aceleraciones bajas, velocidad máxima
irreal, consumo sobredimensionado, radios de giro incorrectos, motores sobredimensionados.

---

## 6. Propulsión: motor, eje, hélice y combustible

### 6.1 Cadena de cálculo

```
palanca → RPM objetivo → dinámica del motor → RPM reales
       → J → KT/KQ → empuje T y torque Q → potencia de eje → carga → SFOC → consumo
```

Fórmulas núcleo (C#):

```
J      = Va / (n·D)          Va = u·(1−w_P)
T      = ρ·n²·D⁴·KT(J)
Q      = ρ·n²·D⁵·KQ(J)
Pshaft = 2π·|n·Q|
```

ShipMMG trae KT (cuadrática `k_0..k_2`) pero **no KQ**. Fuentes para KQ:
- portar la curva lineal de `PropellerInteraction.js` (`KT = β1 − β2·J`, `KQ = γ1 − γ2·J`,
  `η0 = J·KT/(2π·KQ)`, eficiencia rotativa relativa `ηr` por Holtrop — verificado en
  `PropellerInteraction.js:37-79`, devuelve `{eta, Ps, n, Va}`);
- más adelante, polinomios de Wageningen B-series desde MSS.

### 6.2 Motor y eje: las RPM no son instantáneas

La palanca fija una **orden**, no un estado:

```
dn/dt = (nCommand − nActual) / τengine
```

con límites separados: aceleración/deceleración máxima de RPM, tiempo de inversión del eje,
retardo tras Stop, potencia máxima continua/temporal. Secuencia avante→atrás: reducir → cero →
retardo de inversión → RPM negativas → empuje inverso (permite crash stop coherente).
Evolución futura opcional: `Ishaft·dn/dt = Qengine − Qprop − Qloss`.

### 6.3 Combustible

De `FuelConsumption.js:51-153` (verificado): reparto de carga entre motores (`shareLoad`, ratio
de activación 0.8 del MCR), curvas SFOC polinómicas de orden 2 o 3, sistemas diésel-mecánicos
(ηs) y diésel-eléctricos (ηs·ηg), potencia auxiliar (`setAuxPower`). Conversión:

```
fuelFlowKgPerSec = SFOC_g_kWh · Power_kW / 3 600 000
fuelUsedKg      += fuelFlowKgPerSec · dt
```

El consumo es consecuencia de la potencia, nunca entra en la dinámica.

---

## 7. Dos niveles de fidelidad

```csharp
public enum ManeuveringModelType { Clarke83Generic, MmgCalibrated }
```

**Modo MmgCalibrated** — cuando existen coeficientes completos (KVLCC2 L7 de serie).

**Modo Clarke83Generic** — para buques con solo L, B, T, Cb, desplazamiento, velocidad y potencia.
Verificado en `shipClarke83.py:51-218`:
- `clarke83(U_r, L, B, T, Cb, R66, xg, T_surge)` (en `lib/gnc.py`) construye las matrices M y N
  a partir de dimensiones principales; `R66 ≈ 0.25·L` (0.27·L si L > 100 m).
- Timón por teoría de ala: `CN = 6.13·Λ/(Λ+2.25)`, fuerzas `Xdd·sin²δ`, `Yd·sin 2δ`, `Nd·sin 2δ`,
  con `t_R`, `a_H`, `x_R = −0.45·L`, `x_H = −1.0·L` estimados desde Cb.
- **Corriente por velocidad relativa** (patrón a copiar para ambos modos, §8).
- Actuador: `T_delta = 1.0 s`, saturación ±30°.
- `headingAutopilot`: PID con colocación de polos (wn=0.3, ζ=1) + modelo de referencia de 3er orden.

`frigate.py` añade el patrón de **límite de velocidad del timón** (`DdeltaMax = 10°/s`) y un
autopiloto sobre modelo Nomoto — referencia directa para `RudderActuator` y `HeadingAutopilot`.
`lib/mainLoop.py` muestra la separación controller → dynamics → integration → attitude que
reproduciremos en el ciclo de simulación.

---

## 8. Entorno: corriente, viento y oleaje

### 8.1 Corriente

La corriente no mueve el barco visualmente; cambia la velocidad relativa al agua
(patrón exacto de `shipClarke83.py:157-162`):

```
uc = Vc·cos(βc − ψ)      ur = u − uc
vc = Vc·sin(βc − ψ)      vr = v − vc
```

Fuerzas de casco/hélice/timón con `ur, vr`; integración de posición global con `u, v`.
Indicadores derivados: **STW** (agua), **SOG** (fondo), **Heading**, **COG**.

### 8.2 Viento (MSS)

Portar de `MSS/LIBRARY/environment/`:
- `blendermann94.m` — entradas verificadas: `γ_r, V_r, AFw, ALw, sH, sL, Loa, vessel_no`
  (17 tipos de buque tabulados). Salida `τ_w = [Xwind, Ywind, Nwind]` + coeficientes CX/CY/CK/CN.
- `isherwood72.m` — alternativa: `γ_r, V_r, Loa, B, ALw, AFw, A_SS, S, C, M`.

El perfil del buque necesita: área frontal AFw, área lateral ALw, centroides sH/sL, Loa y tipo.
Las fuerzas se suman al MMG (§4.3). El `LangMaoMethod` de ShipNetSim queda descartado como
autoridad (implementación simplificada, y GPL).

### 8.3 Oleaje: dos canales paralelos

- **Canal A (maniobra, 3DOF)**: surge/sway/yaw — el MMG. No se toca.
- **Canal B (visual)**: heave/roll/pitch — no altera la trayectoria en la fase inicial.

Primera versión del canal B: portar `WaveMotion.js` (verificado: `coefficients`,
`verticalMotion`, `rollAmp`, `bendingMoment`; fórmulas cerradas del app shipmotion de NTNU con
amortiguamiento crítico parametrizado — respuesta visual razonable con pocos parámetros).

Versión avanzada: `MSS/LIBRARY/environment/{encounter, waveresponse345, waveSpectrum,
waveDirectionalSpectrum}.m`, y después `{waveMotionRAO, waveForceRAO}.m`. Nota: las RAO de alta
fidelidad requieren datos de ShipX/WAMIT por buque; MSS procesa, no genera la hidrodinámica.
Prototipo → paramétrico; producción → tablas RAO por embarcación.

---

## 9. Mundo inverso y océano visual

### 9.1 Jerarquía de escena

```
SimulationScene
├── FixedBridge            (XR Origin, geometría del puente, controles físicos)
├── WorldMotionRoot        (puerto, terreno, boyas, otros buques)  ← pose inversa
├── OceanMotionRoot        (Ocean Community Next Gen)              ← rumbo inverso + fase + heave/roll/pitch inversos
└── SkyMotionRoot          (cielo/sol/estrellas)                   ← solo rotación
```

Posiciones globales en **double** (N/E); Unity recibe solo coordenadas relativas pequeñas.
Convención: `+Z = norte, +X = este, +Y = arriba`; `ψ = 0° norte, 90° este`.

```csharp
Vector3 shipGlobalUnity = new((float)shipEast, 0f, (float)shipNorth);
Quaternion inverseHeading = Quaternion.Euler(0f, -(float)headingDeg, 0f);
worldRoot.rotation = inverseHeading;
worldRoot.position = -(inverseHeading * shipGlobalUnity);
```

(El prototipo actual ya implementa esta idea con matrices y pivote de sala MRUK en
`ExteriorWorldMotion.ApplyInverseExteriorPose()` — se conserva esa mecánica de captura de pose
inicial y se le inyecta el estado del núcleo.)

### 9.2 Adaptador del océano

APIs verificadas en `Ocean.cs`: `Singleton` (:157), `followMainCamera` (:198),
`GetWaterHeightAtLocation2(x,y)` (:1976), `GetChoppyAtLocation2` (:2010),
`GetHeightChoppyAtLocation2` (:2042), `canCheckBuoyancyNow` (`byte[]`, :92),
`calculateCenterOffset` (:679). Patrón de consulta correcto en `Boyant.cs`.

Reglas:
- **No** colocar `BoatController`/`Boyant`/`Buoyancy` en el puente (moverían el barco real).
- `ocean.followMainCamera = false;`
- Todo acceso pasa por `OceanNextGenAdapter.cs`:

```csharp
double  SampleHeight(double east, double north);
Vector3 SampleNormal(double east, double north);
void    SetVirtualShipPosition(double east, double north, double heading);
```

- **La fase debe avanzar**: el océano recibe la posición virtual módulo el tamaño del tile
  (`phaseEast = Mod(shipEast, tileSize)`), con el rumbo inverso. Así las olas pasan más deprisa
  al acelerar, una ola frente al puente llega hasta él, y la dirección de encuentro cambia con
  el rumbo, sin coordenadas gigantes.
- ⚠️ El asset es de Unity 5.6 (built-in). En este proyecto URP los shaders no funcionarán tal
  cual: portar `Ocean.shader`/`OceanL1-3` a URP o conservar solo el cálculo CPU de la malla y
  la altura, con material URP propio. Decisión en Fase 4; el adaptador aísla ese riesgo.

---

## 10. Controles e instrumentación del puente

Primera embarcación: 1 motor, hélice de paso fijo, timón convencional, bow thruster opcional
(la configuración que encaja limpio con el MMG básico).

| Control físico | Tipo | Variable |
|---|---|---|
| Rueda de gobierno | Continua | `rudderCommandDeg` (±35°) |
| Selector de gobierno | Rotativo | HAND / NFU / AUTO |
| NFU port/stbd | Mantenido | mueve el timón mientras se acciona |
| Telégrafo | Detentes | `engineOrder` → `targetShaftRps` |
| Piloto automático | Selector + knob | `headingSetpointDeg` |
| Bow thruster | Palanca centrada | `bowThrusterCommand` (±1) |
| Bombas de gobierno 1/2 | Switches | habilitan `RudderActuator` |
| Engine Ready/Control | Switch | habilita respuesta del motor |
| Emergency Stop | Botón protegido | corta orden de potencia |

- La rueda pide **ángulo**, no rumbo: `dδ/dt = clamp((δcmd−δ)/Tδ, ±rudderRateMax)`
  (patrón frigate: ~10°/s). Indicador RUDDER ANGLE independiente de la rueda.
- Telégrafo → fracción de RPM configurada **por buque** (Full +1.00, Half +0.70, Slow +0.45,
  Dead Slow +0.25, Stop 0, y −0.20/−0.35/−0.55/−0.75 atrás). El enum de 9 detentes ya existe en
  `ShipControlState.TelegraphOrder`.
- Thruster: `Ybow = cmd·maxThrust`, `Nbow = Ybow·xThruster`, con atenuación progresiva por
  velocidad (el prototipo ya trae `bowThrusterFadeSpeed` — se conserva la idea con curva física).

**Instrumentación**: Heading, ROT, Rudder Angle, Engine Order, RPM reales, SOG, STW, COG,
profundidad, viento, corriente · Propulsión: carga %, kW de eje, RPM, empuje, torque, kg/h,
kg acumulados · Autopiloto: modo, set/actual heading, error, límites de timón y ROT.

**Panel del instructor** (nunca en el puente): corriente (V, dir), viento (V, dir), oleaje
(Hs, Tp, dir), profundidad, densidad, visibilidad, inyección de fallos (bombas, motor).

---

## 11. Estructura de código propuesta

```
Assets/NavigationSim/
├── Runtime/
│   ├── Core/         ShipState, ShipCommand, EnvironmentState, SimulationClock
│   ├── Dynamics/     IManeuveringModel, Mmg3DofModel, MmgForces, Clarke83Model,
│   │                 RudderActuator, EngineShaftModel, Rk4Integrator
│   ├── Propulsion/   PropellerModel, HullResistanceCalibration, FuelConsumptionModel
│   ├── Environment/  CurrentModel, WindLoadModel, WaveResponseModel,
│   │                 IOceanSurface, OceanNextGenAdapter
│   ├── GNC/          HeadingAutopilot, SteeringModeController
│   ├── Controls/     HelmWheelInput, NfuSteeringInput, TelegraphInput,
│   │                 BowThrusterInput, BridgeControlState
│   └── Rendering/    WorldMotionDriver, OceanMotionDriver, OtherShipPresenter, FloatingOrigin
├── Data/             VesselProfile, PropellerProfile, EngineProfile, Profiles/ (ScriptableObjects)
└── Tests/            MmgRegressionTests, PropellerTests, FuelConsumptionTests, WorldMotionTests
```

Principios: núcleo `Runtime/Core+Dynamics+Propulsion+Environment` **sin dependencia de
UnityEngine** donde sea posible (asmdef separado, testeable en editor), double en todo el estado,
50 Hz fijos, sin Rigidbody para el buque propio. El asset del océano jamás se referencia fuera
de `OceanNextGenAdapter`.

---

## 12. Qué tomar de cada repo (y qué no)

| Repo | Portar | No portar |
|---|---|---|
| ShipMMG | `Mmg3DofBasicParams`, `Mmg3DofManeuveringParams`, EOM completa (`mmg_3dof.py:862-955`), cinemática; params KVLCC2 L7 de los tests | SciPy, solve_ivp, splines de entrada, gráficos, helpers de trayectorias |
| Vessel.js | Holtrop (`HullResistance.js`), curvas KT/KQ + ηr (`PropellerInteraction.js`), shareLoad + SFOC (`FuelConsumption.js`), opcional `WaveMotion.js` | `Manoeuvring.js` (solapa con ShipMMG: getPropResult, hydroCoeff, dn) |
| PVS | Concepto Clarke83 + `clarke83()` de gnc.py, corriente relativa, autopilot PID + ref model, límites de timón (frigate), estructura mainLoop | Resto de vehículos, matplotlib |
| MSS | `blendermann94`, `isherwood72`, `encounter`, `waveresponse345`, `waveSpectrum`, `waveDirectionalSpectrum`, `rk4`; después `waveMotionRAO`, `waveForceRAO` | MATLAB/Octave runtime; el resto de la librería |
| Ocean CNG | Uso del asset + adaptador; malla/altura CPU | Nada de su código en el núcleo; sin `BoatController`/`Boyant` en el puente |
| ShipNetSim | **Nada de código (GPL-3.0)**. Solo ejecutar escenarios comparativos de consumo/resistencia | Todo el código |

Nota legal: portar **ecuaciones publicadas** (Yasukawa/Yoshimura 2015, Holtrop 1984,
Blendermann 1994, Isherwood 1972, Clarke 1983, Fossen 2021) es siempre seguro; portar código
MIT requiere solo atribución (mantener avisos de copyright en THIRD-PARTY-NOTICES).

---

## 13. Fases de implementación

### Fase 1 — Maniobra sin océano
`ShipState`, `ShipCommand`, `RudderActuator`, `EngineShaftModel`, `Mmg3DofModel`,
`Rk4Integrator`, `WorldMotionDriver` (adaptando el prototipo actual).
**Aceptación**: aceleración en recta, reducción de máquina, marcha atrás, giro 35° y crash stop;
`MmgRegressionTests` reproduce el turning test del KVLCC2 L7 de `test_mmg_3dof.py` (misma
trayectoria que Python con tolerancia < 1%).

### Fase 2 — Propulsión y combustible
`PropellerModel` (KT/KQ), `FuelConsumptionModel`, `HullResistanceCalibration` (offline).
**Aceptación**: RPM↔velocidad, potencia↔velocidad, consumo↔carga, velocidad máxima de
equilibrio coincide con la curva Holtrop calibrada (sin duplicar resistencia, §5).

### Fase 3 — Corriente, viento y thrusters
`CurrentModel`, `WindLoadModel` (blendermann94), `BowThruster`, indicadores SOG/STW/COG.
**Aceptación**: barco parado deriva con la corriente; abatimiento correcto con viento de través;
STW ≠ SOG coherentes.

### Fase 4 — Océano visual
`OceanNextGenAdapter`, fase espacial del océano, muestreo de altura, canal B (heave/roll/pitch
vía WaveMotion), estela. Decisión URP (portar shaders vs. render propio sobre malla CPU).
**Aceptación**: las olas pasan más rápido al acelerar y cambian de dirección de encuentro con
el rumbo; sin jitter a >10 km del origen (double + fase modular).

### Fase 5 — Piloto automático y escenarios
HAND/NFU/AUTO, `HeadingAutopilot` (PID + modelo de referencia de PVS), fallos de bomba/motor,
panel de instructor, otros buques (`OtherShipPresenter` con Clarke83 o cinemática simple).
**Aceptación**: zigzag 20/20 estable en AUTO; cambio HAND↔AUTO sin saltos de timón.

### Instrumentos de puente (referencia Bridge Command)
Ver [INSTRUMENTOS_PUENTE.md](INSTRUMENTOS_PUENTE.md): GeoDatum, TrafficWorld, chart-lite,
radar PPI + EBL/VRM + ARPA, AIS, demora visual, NMEA UDP. Sin copiar código GPL-2 de BC.

---

## 14. Riesgos y mitigaciones

| Riesgo | Impacto | Mitigación |
|---|---|---|
| Shaders Ocean (Unity 5.6, built-in) vs URP | Alto | Adaptador + decisión explícita en Fase 4; plan B: malla CPU + material URP propio |
| Doble resistencia (Holtrop + R_0') | Alto | Regla §5 + test de velocidad máxima de equilibrio |
| Singularidades MMG a U≈0 / n≈0 | Medio | Portar las ramas especiales de ShipMMG (J=0, U=0) y testear arrancada desde parado |
| Coeficientes inventados en buques genéricos | Medio | Modo Clarke83 obligatorio si no hay datos de pruebas de mar |
| Deriva float en recorridos largos | Medio | double en el núcleo + FloatingOrigin/fase modular |
| GPL de ShipNetSim | Alto (legal) | Nunca vincular ni copiar; solo comparación externa de resultados |
| RAO de alta fidelidad requieren ShipX/WAMIT | Bajo (fase tardía) | Canal B paramétrico en prototipo; RAO por buque solo en producción |

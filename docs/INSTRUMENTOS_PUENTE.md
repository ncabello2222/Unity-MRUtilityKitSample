# Instrumentos de puente (referencia Bridge Command)

Roadmap implementado sobre NavigationSim. No se copia código GPL-2 de
[bridgecommand/bc](https://github.com/bridgecommand/bc); solo UX/features.

## Atajos

| Panel | Teclado | Quest (Touch) |
|-------|---------|----------------|
| **Menú de instrumentos** | **X** | **Izq. X** (`Button.One` LTouch) |
| Conning | Y · o menú | **Izq. Y** (`Button.Two` LTouch) |
| Config instructor | B | **Der. B** (`Button.Two` RTouch) |
| Panel físico nav | — | **Der. A** (spawner) |
| Chart / Radar / AIS / Demora | C / R / I / V | **Menú (Izq. X)** |
| Binoculars (con bearing) | N | — |

Mapeo OVR: izq. **X/Y** = `One`/`Two` LTouch · der. **A/B** = `One`/`Two` RTouch.  
En Quest standalone: **X → menú → elegir panel** con el láser del mando derecho.

## Arquitectura

- **Core (sin Unity UI):** `GeoDatum`, `TrafficContact` / `TrafficWorld`, `RadarModel`,
  `ArpaTracker`, `NmeaOutput` bajo `Assets/NavigationSim/Runtime/Core/`.
- **Presentación:** `OtherShipPresenter`, paneles `BridgeChartDisplay`,
  `BridgeRadarDisplay`, `BridgeAisDisplay`, `VisualBearingOverlay`.
- **Host:** `NavigationSimRunner` posee Traffic/Radar/Arpa/Geo/Nmea y los actualiza
  cada tick. Bootstrap en `NavigationSimBootstrap`.

## Fases

| Fase | Estado | Entregable |
|------|--------|------------|
| 0 | Hecho | GeoDatum, TrafficWorld demo, OtherShipPresenter, land samples |
| 1 | Hecho | Lat/Lon + tide en conning OpenBridge y fallback |
| 2 | Hecho | Chart-lite VR (X) |
| 3 | Hecho | Radar PPI sintético (R) |
| 4 | Hecho | EBL / VRM en radar |
| 5 | Hecho | ARPA CPA/TCPA + vectores |
| 6 | Hecho | AIS list + visual bearing / binos |
| 7 | Parcial | Guard zone, PI, trial manoeuvre, NMEA UDP; radar realista / ENC diferidos |

## Diferidos (Fase 7 restante)

- Radar con gain/clutter/rain y retorno de costa realista.
- ECDIS con cartas ENC oficiales.
- MARPA / adquisición por eco (hoy ARPA usa IDs de `TrafficWorld`).

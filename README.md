# GTA: Punta del Este

Juego 3D de mundo abierto estilo GTA ambientado en la **Península de Punta del Este, Uruguay**, hecho con **Unity 6000.3.29f1 (URP Forward+)**. El mapa se generó a partir de datos reales de OpenStreetMap.

## Características

- **Mapa real**: península, Rambla perimetral, Av. Gorlero, calles céntricas, Playa Mansa y Playa Brava.
- **Lugares icónicos**: Los Dedos (La Mano), Puerto con marinas y yates, El Faro, Iglesia de la Candelaria, Plaza Artigas, Punta de las Salinas, Enjoy Resort y torres costeras.
- **Jugabilidad**: exploración a pie en tercera persona, conducción con `WheelCollider`, subir y bajar de cualquier auto, conductor visible, derrapes con marcas y humo.
- **Ciudad viva**: tráfico con IA y semáforos, peatones, gaviotas, policía con sistema de búsqueda de 1 a 5 estrellas.
- **Ambiente**: 3 cielos HDRI (tarde, atardecer en la Mansa, noche), mar con reflejos y espuma, haz rotatorio de El Faro.
- **HUD**: minimapa rotativo, velocímetro, estrellas de búsqueda, mapa completo con viaje rápido, radio con 2 estaciones.

## Controles

| Acción | Tecla |
|---|---|
| Mover / correr / saltar | `WASD` / `Shift` / `Espacio` |
| Subir / bajar de un auto | `F` o `E` |
| Acelerar, frenar, doblar | `W` `S` `A` `D` |
| Freno de mano / derrape | `Espacio` |
| Bocina / sirena (patrullero) | `H` / `Q` |
| Enderezar auto | `R` |
| Cambiar hora del día | `T` |
| Cambiar radio | `V` |
| Mapa y viaje rápido | `M` |
| Circuito por la Rambla | `K` |
| Pausa | `Esc` |

## Cómo abrir el proyecto

1. Instalar **Unity 6000.3.29f1**.
2. Clonar este repositorio y abrir la carpeta con Unity Hub.
3. Abrir la escena `Assets/Scenes/PuntaDelEste.unity` y dar Play.

Si hay que regenerar la escena: menú `Tools > GTA Punta del Este > Build Complete City Scene`.

## Estructura

- `Assets/Scripts/` — código del juego (`Core`, `Player`, `Vehicles`, `AI`, `Wanted`, `UI`, `World`, `Atmosphere`, `Editor`).
- `Assets/Art/` — modelos FBX, materiales, texturas PBR, HDRIs y shader del mar.
- `Assets/Resources/` — audio y datos del mapa (OSM, terreno y minimapa).
- `Assets/Scenes/` — escena principal.

## Versiones

- `checkpoint-v1-baseline`: primera versión jugable.
- `checkpoint-v2-peninsula`: versión pulida con arquitectura variada, cielos HDRI, puerto rehecho, autos y personaje nuevos.

## Créditos

- Datos de mapa: © colaboradores de OpenStreetMap (ODbL).
- Texturas, HDRIs y rocas fotogramétricas: [Poly Haven](https://polyhaven.com) (CC0).
- Modelos 3D propios hechos con Blender 5.3.

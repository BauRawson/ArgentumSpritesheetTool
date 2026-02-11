# Argentum Spritesheet Tool


https://github.com/user-attachments/assets/2c3e9d58-82eb-466b-867c-040ecbeb86b2



An automated tool to export 3D models as animated spritesheets specifically designed for **Argentum Online** projects.
<img width="256" height="1280" alt="Body_naked_human_female" src="https://github.com/user-attachments/assets/10226aa8-9ce6-4449-8407-641606a62b47" />
<img width="256" height="1280" alt="Body_clothing_green_human_female" src="https://github.com/user-attachments/assets/50532cce-c8b3-4cc3-8e94-2968ecea4f8d" />
<img width="256" height="1280" alt="Body_armor_leather_human_female" src="https://github.com/user-attachments/assets/2e9e607f-b7d0-4451-82d5-ff6c224d33e7" />

## Overview
The goal of this tool is to bridge the gap between 3D modeling and classic 2D pixel art games. It allows developers to create high-quality, high-frame-count animations (like 8-frame walk cycles or complex combat moves) and export them into spritesheets that fit the Argentum Online aesthetic.

## Features
*   **High Animation Density:** Support for numerous animations per character, including weapon-specific attacks, hit reactions, and smooth movement cycles.
*   **Design-First Rigging:** The provided animations are intentionally rigid. This ensures that manually drawn character heads remain consistent with the body without unnatural warping or perspective shifts.
*   **Pixel Art Compatibility:** While 3D-driven, the tool encourages keyframe techniques consistent with pixel art (for example, short "Hit" animations).
*   **Batch Export:** Process multiple armor sets, weapons, and variants in one go.

## Requirements & Workflow
1.  **Unity 6.2:** The project is built using Unity 6 (6.2).
2.  **3D Modeling Knowledge:** A basic understanding of 3D modeling and rigging is required.
3.  **Blender to Unity:** 
    *   Open the provided `.blend` file (e.g., `human_male.blend`).
    *   Create your armors/weapons, adjusting them to the different race/gender body types.
    *   Export as `.fbx` and import into Unity.
4.  **Rigging:** Please note that the current rig **does not support Unity's Humanoid system**. Pull requests are welcome if you'd like to implement humanoid compatibility!

## Future
If there is enough interest in the community, I might create a video tutorial explaining the full pipeline from Blender to the final exported spritesheet.

---

# Argentum Spritesheet Tool (Español)

Una herramienta automatizada para exportar modelos 3D como spritesheets animados, diseñada específicamente para proyectos de **Argentum Online**.

## Resumen
El objetivo de esta herramienta es facilitar la transición entre el modelado 3D y los juegos clásicos de pixel art en 2D. Permite a los desarrolladores crear animaciones de alta calidad y con muchos frames (como ciclos de caminata de 8 frames o movimientos de combate complejos) y exportarlas en spritesheets que encajen con la estética de Argentum Online.

## Características
*   **Densidad de Animación:** Soporte para muchísimas animaciones por personaje, incluyendo ataques específicos según el arma, reacciones de golpe (hit) y ciclos de movimiento fluidos.
*   **Rigging Orientado al Diseño:** Las animaciones proporcionadas son intencionalmente rígidas. Esto se hizo para permitir que las cabezas de los personajes (dibujadas a mano) se mantengan consistentes con el cuerpo sin deformaciones extrañas.
*   **Compatibilidad con Pixel Art:** Aunque se basa en 3D, la herramienta fomenta técnicas de keyframes consistentes con el pixel art (por ejemplo, animaciones de "golpe" cortas).
*   **Exportación por Lotes:** Procesa múltiples armaduras, armas y variantes de una sola vez.

## Requisitos y Flujo de Trabajo
1.  **Unity 6.2:** El proyecto está desarrollado utilizando Unity 6 (6.2).
2.  **Conocimientos de Modelado 3D:** Se requiere un conocimiento básico de modelado 3D y rigging.
3.  **De Blender a Unity:** 
    *   Abre el archivo `.blend` proporcionado (ej. `human_male.blend`).
    *   Crea tus armaduras/armas, ajustándolas a los diferentes tipos de cuerpo de cada raza/género.
    *   Exporta como `.fbx` e impórtalo en Unity.
4.  **Rigging:** Ten en cuenta que el rig actual **no es compatible con el sistema Humanoid** de Unity. ¡Los Pull Requests son bienvenidos si alguien quiere implementar esta compatibilidad!

## Futuro
Si hay suficiente interés en la comunidad, podría crear un tutorial explicando todo el flujo de trabajo desde Blender hasta el spritesheet final exportado.

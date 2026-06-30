# Roadmap de sobe

Editor de beatmaps para osu!
Version actual: v0.9.65-beta

Lo marcado con check esta hecho. Lo que falta lleva el porcentaje completado.

## Completado

- ✓ Editor base: playfield, timeline superior e inferior, colocar/mover/seleccionar objetos, undo/redo, snapping y stacking
- ✓ Lectura de los mapas instalados de osu!lazer (acceso directo al realm)
- ✓ Guardar, crear y borrar mapas directamente en el realm de lazer, sin abrir osu!
- ✓ Auto-actualizacion del programa via releases de GitHub
- ✓ Estadisticas de uso (tiempo de edicion por mapa)
- ✓ Calculo de star rating dentro de la app
- ✓ Preview visual de mods (HardRock y Hidden)
- ✓ Importar mapas (.osz), exportar a .osz y buscar/descargar mapas con la API de osu!
- ✓ Sistema de diseno y tema unificado del editor
- ✓ Rediseno del menu principal (barra superior, tarjeta de usuario y stats)
- ✓ Login con osu! (OAuth) y sincronizacion de estadisticas
- ✓ Modo modding: burbujas de discusion de osu! en la timeline, con filtros y mensajes
- ✓ Cursor "Humanize" del modo Auto, que imita el movimiento de un jugador real
- ✓ Hitsounds completos: reproduccion con samples del mapa y skin, loops de slider/spinner, sample sets e index, samples y volumen por objeto, copiar/pegar, editor de lanes en la timeline (pintar, atajos W/E/R, hover, feedback de reproduccion) y opcion de ignorar los hitsounds del mapa
- ✓ Discord Rich Presence en escritorio (muestra editando/navegando, activable desde ajustes)

## En desarrollo

### Pattern Gallery - 70%
- ✓ Guardar una seleccion de objetos como patron reutilizable
- ✓ Galeria deslizante para navegar y reutilizar patrones
- ✓ Pegado que conserva el ritmo, re-ajustando al BPM y SV del mapa destino
- ✓ Colecciones/carpetas y persistencia por usuario
- Compartir patrones con otros usuarios
- Editar patrones dentro de la galeria

### Collab mapping (git para mapas) - 85%
- ✓ Versionado por dificultad con historial lineal fast-forward
- ✓ Lado owner y lado colaborador: descargar, pull y push
- ✓ Invitaciones, timeline de revisiones y coloreado por autor
- Aviso de cambios disponibles dentro del editor
- Resolucion automatica de conflictos / merge

### Modo Review (modding compartible) - 80%
Capa de anotaciones que solo se ve dentro de sobe, exportable como archivo .sobemod.
- ✓ Notas flotantes ancladas a tiempo y posicion, con autor y color
- ✓ Cuatro tipos de nota (nota, praise, problem, suggestion) con icono propio
- ✓ Herramientas Select, Note y Draw (dibujo libre con suavizado)
- ✓ Control de duracion/inicio de cada trazo desde la timeline superior
- ✓ Timestamps clicables dentro de las notas con preview de patron
- ✓ Iconos en la timeline inferior cuando la nota esta fuera de tiempo
- ✓ Guardado junto al mapa y export/import .sobemod
- ✓ undo/redo sobre las anotaciones
- Editor de texto multilinea propio para las notas
- Sincronizacion de las revisiones con el backend para compartirlas online

### Rediseno estetico general - 60%
- ✓ Barra superior y tarjeta de usuario del menu principal
- Aplicar el rediseno al resto de pantallas del programa

## Planeado (sin empezar)

- Lista de amigos - 0%
- Burbujas de comentarios entre usuarios - 0%

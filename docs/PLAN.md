# Oxfall: hoja de ruta

Actualizada el 2026-11-13. Es la guía para decidir qué se construye después y en qué orden.
Quien retome el proyecto (persona o IA) debe leer esto primero.

## Dónde estamos

- **Reglas del juego** en C# puro (`Assets/Oxfall/Scripts/Core`): ítems, inventario y barra rápida,
  crafteo con cola, recolección, construcción en rejilla con niveles (ramas, madera, piedra) y puertas,
  ciclo de día y noche, vida, hambre y sed. 86 pruebas automáticas.
- **Juego jugable de un jugador** (`Assets/Oxfall/Scripts/Game`): isla generada por semilla, moverse,
  mirar, saltar, nadar, golpear para recolectar, inventario de 30 casillas con íconos, fabricar,
  construir y mejorar con el martillo, puertas, fogatas y antorcha que alumbran de noche, comer moras y
  beber en charcas, morir y despertar, sonidos, tres niveles de gráficos.
- **Todo se compila en la nube** sin PC: APK de Android y versión web, con cada cambio.
- **Pruebas automáticas** de la versión web en un navegador sin pantalla (`tools/webtest`).

Lo que **no** existe todavía: multijugador, combate, guardado, segundo piso, horno y metal, modelos
de arte reales (todo se genera por código con texturas CC0).

## Los tres riesgos que mandan el orden

1. **El multijugador cambia la arquitectura.** Hoy todo corre en el teléfono del jugador. Cada sistema
   que se construya así habrá que rehacerlo para que mande el servidor. Por eso la red va **antes** de
   llenar el juego de contenido, no al final.
2. **El rendimiento en el Redmi 12C no está medido.** Si la isla vacía no da 30 FPS ahí, hay que saberlo
   ya, antes de añadir bases, jugadores y efectos.
3. **El arte es provisional.** Árboles y rocas se generan con código. Sirve para probar, no para vender.
   Hace falta un flujo para meter modelos reales sin abrir el editor de Unity.

## Fases

### Fase 1. Que se sienta como un juego (un jugador)

Objetivo: 10 minutos divertidos en el teléfono. Cada punto se prueba primero en la versión web.

1. Medir FPS en el Redmi 12C y ajustar el nivel Bajo hasta 30 FPS estables.
2. Pantalla de inventario (30 casillas) y barra rápida para elegir qué llevas en la mano.
3. Construcción: colocar cimiento, pared, marco de puerta y techo con el plano (las reglas ya existen).
4. Fogata colocable, ciclo de día y noche, antorcha que alumbra.
5. Vida, hambre y sed, con comida básica.
6. Sonido: golpes, pasos, ambiente.
7. Primer pase de arte con modelos gratuitos de licencia libre (CC0), cargados por código.

### Fase 2. Multijugador base (la fase más importante y más difícil)

Objetivo: dos teléfonos en la misma isla, viéndose y recolectando los mismos árboles.

1. Fish-Net con servidor dedicado: una compilación de Linux sin gráficos, hecha también en la nube.
2. El servidor manda: movimiento con predicción en el cliente, recursos, inventario y construcción.
3. Guardado en el servidor y wipe semanal cambiando la semilla.
4. Servidor de pruebas en un VPS barato (unos 5 USD al mes), en la costa este de EE. UU. por la latencia
   hacia Venezuela y Latinoamérica.
5. Mantener la versión web como cliente (transporte WebSocket), para probar entre iPhone y Android.

### Fase 3. PvP y asalto de bases

Armas (lanza, arco, luego armas de fuego), daño, muerte y reaparición con saco de dormir, botín al morir,
armario de herramientas (quién puede construir), puertas con cerradura, vida de las piezas y asalto.

### Fase 4. Contenido y retención

Monumentos con cajas de botín, animales, horno y fundición, mesas de trabajo por niveles, mapa, chat y
equipos.

### Fase 5. Lanzamiento

Cuentas, tienda de skins, medidas contra trampas (el servidor valida todo), reportes de errores, ficha de
la tienda y prueba cerrada en Google Play.

## Presupuesto de rendimiento (Redmi 12C, nivel Bajo)

| Límite | Valor |
| --- | --- |
| FPS | 30 estables |
| Llamadas de dibujo | menos de 150 |
| Triángulos en pantalla | menos de 150 000 |
| Sombras en tiempo real | ninguna |
| Tamaño del APK | menos de 150 MB |

En gama media-alta y alta el mínimo es 60 FPS.

## Decisiones que solo Alejandro puede tomar

- **Probar el APK en el Redmi 12C** y pasar los FPS.
- **Activar GitHub Pages** (Settings, Pages, Source: GitHub Actions) para tener el enlace web del iPhone.
- **Repositorio público o privado.** Público: minutos de compilación ilimitados, pero cualquiera ve el
  código. Privado: 2000 minutos gratis al mes (unas 80 compilaciones). Recomendación: público mientras
  sea prototipo, privado antes de lanzar.
- **Identificador de la app** (`com.oxfall.survival`): se puede cambiar hasta la primera subida a Google
  Play, después es permanente.
- **Cuenta de Google Play** (25 USD, pago único). Las cuentas personales nuevas deben hacer una prueba
  cerrada con al menos 12 personas durante 14 días antes de publicar: la comunidad de Telegram sirve
  para eso, conviene ir juntando voluntarios.
- **iPhone nativo:** cuenta de Apple Developer (99 USD al año). La compilación se puede hacer en la nube
  con máquinas Mac de GitHub; no hace falta comprar una Mac.

## Reglas de trabajo

- Nunca se escribe a mano el YAML de Unity: los ajustes van en `Assets/Oxfall/Editor/ProjectSetup.cs`.
- Las reglas del juego van en `Core` (sin Unity) con sus pruebas; `Game` solo las muestra.
- Todo lo visible se construye por código al iniciar; la escena queda vacía.
- Cada cambio se prueba en la versión web antes de pedirle a Alejandro que lo mire.

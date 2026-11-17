# Oxfall: Survival Online. Estado completo del proyecto

Actualizado el **2026-11-17**. Este documento basta para retomar el proyecto (persona o IA) sin
ningún otro contexto. Léelo entero antes de tocar código. La hoja de ruta original está en
[`PLAN.md`](PLAN.md); lo que dice aquí manda donde los dos difieran.

> **Estado: PAUSADO.** El 2026-11-17 el dueño pidió frenar todo el desarrollo. No se sigue con
> mecánicas nuevas hasta que él lo diga. El último trabajo (fase 12) quedó subido, con las pruebas
> automáticas pasando, pero sin probar en la versión web (ver "Dónde quedó exactamente").

---

## 1. Qué es

Juego de supervivencia multijugador al estilo de Rust (Facepunch) para celular, ligero (tiene que
correr en un Redmi 12C de gama baja) pero que impresione. Se publica primero en Android (Google Play)
y se monetiza con skins.

- **Nombre:** "Oxfall: Survival Online". No usar "Rust" en el nombre ni en la ficha: es marca de
  Facepunch. Las búsquedas de "rust" se captan con anuncios y redes.
- **Competencia:** existe un Rust Mobile oficial (Tencent / Level Infinite, licencia de Facepunch), en
  beta, que pide gama alta (Snapdragon 8 Gen 1 + 6 GB). **Nicho de Oxfall:** celulares baratos y
  comunidad en español. No venderse como "Rust en celular".
- **Arranque:** un solo servidor PvP, 50 a 100 jugadores o menos, mapa pequeño, wipe semanal
  automático. PvE más adelante.
- **Rendimiento obligatorio:** 30 FPS estables en el Redmi 12C (nivel Bajo); 60 FPS mínimo en gama
  media-alta y alta. Presupuesto en `PLAN.md`.
- **Estilo visual:** realista estilizado (formas realistas, paleta apagada y oxidada) con la gradación
  de color del estilo semi-realista (post-proceso URP). Maqueta:
  <https://claude.ai/artifact/A6byGzWvrwPrD1PBYTdY5U>. Documento de diseño del MVP:
  <https://claude.ai/artifact/HkmmYJUBu7ie1Ds3Anj9Js> (los dos son privados del dueño).
- **Presupuesto:** todo gratis hasta producción. Arte con texturas CC0 de Poly Haven y modelos hechos
  por código. Comprar assets (Asset Store) o generar modelos con IA 3D (Tripo, Meshy) solo al final.
  Ojo: el repo es **público**, así que assets comprados no pueden subirse aquí (licencia): hará falta un
  repo privado antes.

## 2. Cómo se trabaja (reglas del dueño)

- El dueño (Ander) no programa juegos: la IA construye casi todo. Habla español; quiere respuestas
  cortas, directas y un paso a la vez.
- Trabaja muchas veces solo desde el teléfono (cortes de luz en Venezuela). Por eso **todo compila en
  la nube** (GitHub Actions) y **se prueba en la versión web** con un navegador sin pantalla.
- No pedirle que pruebe nada (ni el servidor) hasta que mecánicas, funcionalidad y jugabilidad estén al
  100%. Probar uno mismo antes con `tools/webtest`.
- La interfaz siempre al máximo: original, simple y profesional. Usar Rust de PC como referencia de lo
  mejor.
- Cada mecánica nueva se hace completa: **reglas en Core + pruebas**, **servidor**, **cliente**
  (vista, sonido, UI) y **prueba web con capturas**.
- Equipos: iPhone 15 (prueba la web) y Redmi 12C (piso de rendimiento). PC Windows 10, Ryzen 5 5600X,
  RX 570, 16 GB.

## 3. Arquitectura

```
Assets/Oxfall/Scripts/Core   Reglas del juego en C# puro (sin Unity). Las usan el juego y el servidor.
Assets/Oxfall/Scripts/Game   El juego en Unity: todo se crea por código al iniciar; la escena está vacía.
Assets/Oxfall/Editor         ProjectSetup (todos los ajustes de Unity), ArtSetup, AndroidBuilder, WebBuilder.
Assets/Oxfall/Shaders        Oxfall/Ground (terreno de 4 capas), Oxfall/Water, Oxfall/GradientSky.
Assets/Oxfall/Art            Texturas CC0 (Poly Haven) y fuentes.
server/Oxfall.Server         Servidor propio .NET 8 (WebSocket, autoritativo). Dockerfile en server/.
tests/Oxfall.Core.Tests      431 pruebas NUnit de las reglas (dotnet test, sin Unity).
tools/compilecheck           Compila Core+Game contra ensamblados de referencia de Unity en segundos.
tools/webtest                Pruebas de la versión web con Playwright (una por mecánica).
tools/netbot                 Jugador mínimo en C# para pruebas online (entra al servidor y escribe lo que oye).
tools/servertest/bots.js     Bots de carga para el servidor (Node 22+).
tools/art                    Lista de texturas de Poly Haven y scripts de arte.
deploy/oracle                Instalación del servidor en Oracle Cloud Always Free (LEEME.md + setup.sh).
render.yaml                  Plano para Render (descartado: el plan gratis se duerme y pierde el guardado).
```

Decisiones de stack (y por qué):
- **Unity 6 (6000.3.28f1) + URP.** Descartados Godot (multijugador inestable con muchos jugadores),
  Unreal (pesado en móvil) y Photon Fusion (cobra por jugador conectado).
- **Servidor propio en .NET** (reemplazó a Fish-Net el 2026-11-14): gratis, comparte las reglas de
  `Core` con el cliente, el servidor manda en todo (anti-trampas).
- **Hosting de pruebas:** Oracle Cloud Always Free (VM siempre encendida con disco). Aún no se creó la
  cuenta: guiar al dueño paso a paso con `deploy/oracle/LEEME.md` cuando toque probar con amigos.

Reglas de código:
- Nunca escribir a mano el YAML de Unity: los ajustes van en `Assets/Oxfall/Editor/ProjectSetup.cs`.
- Las reglas van en `Core` con sus pruebas; `Game` solo las muestra y las manda al servidor.
- Online, el cliente pide y el servidor decide (`GameActions` hace de puente: en solitario aplica la
  regla aquí mismo; en línea la manda al servidor).
- Cada cambio en los mensajes de red sube `Protocol.Version` (`Core/Net/Protocol.cs`, hoy **34**).
  Cliente y servidor con versiones distintas no se conectan.
- Los mensajes, enums y tipos que viajan por la red o se guardan son números: **solo se agregan al
  final**, nunca se reordenan.
- Comentarios del código en inglés; textos del juego en español.

## 4. Compilar, probar y desplegar

### En un contenedor nuevo (nube de Claude)
```
apt-get update; apt-get install -y dotnet-sdk-8.0      # dot.net está bloqueado por el proxy; apt sí funciona
cd tests/Oxfall.Core.Tests && dotnet test              # 431 pruebas, unos segundos
dotnet build tools/compilecheck                        # compila Core+Game (Android)
dotnet build tools/compilecheck -p:Platform=WebGL      # compila Core+Game (web)
```
`compilecheck` no cubre el código URP (`OXFALL_URP`) ni `Editor`; esos solo los compila la CI.

### Compilación en la nube (GitHub Actions)
- `core-tests.yml`: corre las pruebas de Core en cada push.
- `android-build.yml`: compila el APK y lo publica como release `build-N`. Descarga directa:
  <https://github.com/aleanfx/Rust-Mobile/releases/latest/download/Oxfall.apk>. Firma de pruebas en la
  caché de Actions (`oxfall-dev-keystore-v1`); si la caché expira hay que desinstalar la app una vez.
- `web-build.yml`: compila WebGL, publica en GitHub Pages (<https://aleanfx.github.io/Rust-Mobile/>) y
  sube `oxfall-web.zip` al pre-release `web-latest` (de ahí lo bajan las pruebas).
- `fetch-art.yml`: baja texturas de Poly Haven listadas en `tools/art/polyhaven.txt`.
- `unity-licence.yml`: prueba de la licencia.
- Licencia Unity Personal activada con correo y contraseña (secretos `UNITY_EMAIL` y `UNITY_PASSWORD`;
  la cuenta no debe tener 2FA). Siempre se desactiva al final.
- Cualquier rama compila; cada build tarda 12 a 15 min (Android más en frío). La CI **hace commits a la
  rama** (lo que genera Unity): hacer `git pull --rebase` antes de cada push.
- Saber si ya salió la web nueva:
  `curl -sSL -r 0-0 -D - -o /dev/null https://github.com/aleanfx/Rust-Mobile/releases/download/web-latest/oxfall-web.zip | grep -i last-modified`

### Probar la versión web
```
tools/webtest/fetch.sh                 # baja web-latest y la sirve en :8080 (si se cuelga, comprobar con curl localhost:8080)
node tools/webtest/heli.js /tmp/out    # una prueba: imprime PASS/FAIL y guarda capturas y consola
```
- `?solo` en la dirección entra directo a jugar solo; `?server=wss://…/ws` se une a un servidor.
- Las pruebas mandan comandos de depuración:
  `unityInstance.SendMessage('Oxfall','DebugCommand','<comando>')` (lista en la sección 7).
- La máquina de prueba va a unos 3 FPS (SwiftShader): esperar entre pasos.
- **Pruebas online:** dos navegadores ya no caben en la máquina de prueba. Usar un navegador +
  `tools/netbot` como segundo jugador. Servidor local:
  ```
  cd server/Oxfall.Server && OXFALL_DEV=1 OXFALL_SAVE=/tmp/oxfall-$(date +%s).sav dotnet run -- --port 7777
  dotnet run --project tools/netbot -- ws://localhost:7777/ws Bot 120
  ```
  Usar un `OXFALL_SAVE` nuevo cada vez (si no, quedan cosas de corridas viejas). El juego está en `/ws`.

### Servidor: variables
- `PORT` (7777), `OXFALL_SEED` (número, o `weekly` para isla nueva cada jueves 19:00 UTC: el servidor
  se detiene a la hora del wipe y su servicio lo vuelve a arrancar), `OXFALL_SAVE` (archivo de guardado,
  `none` para no guardar), `OXFALL_DEV=1` solo para pruebas (teletransporte, todos en la misma playa).
- Guarda la isla en `oxfall-world.sav`; las skins compradas en `oxfall-lockers.sav` (sobrevive al wipe).
- `GET /` responde "Oxfall server · protocol N · seed … · N online".

### Desplegar en Oracle
`deploy/oracle/LEEME.md` paso a paso. `setup.sh` baja la rama `claude/project-thread-53puct`
(o la que diga `OXFALL_BRANCH`); si se crea `main` u otra rama nueva, cambiarla en `setup.sh` y `LEEME.md`.
Después, dejar fija la dirección `wss://…/ws` en el
juego (botón JUGAR EN LÍNEA).

## 5. Ramas

No hay `main`. Cada hilo de trabajo creó una rama que sale de la anterior; la última tiene todo.

**Rama actual: `claude/project-thread-53puct`** (último commit propio 8db6e2b, 2026-11-17).
Cadena: q7p88t → jv5h3e → y1h3uh → x49aeb → 0hge1q → s92c2f → 5bhjvg → 7yctgz → jydyqj → 53puct
(todas `claude/project-thread-*`). Conviene crear `main` desde la rama actual al retomar.

## 6. Qué está hecho

### Base (fases 1 y 2, hasta 2026-11-14)
- Isla generada por semilla: terreno, mar, pasto, árboles y rocas por código con texturas CC0, ruinas y
  puestos (monumentos), día y noche, clima (lluvia, tormenta, niebla), mapa con marcadores.
- Jugador: moverse, mirar, saltar, nadar, controles táctiles, tres niveles de gráficos.
- Supervivencia: vida, hambre, sed, temperatura (frío de noche y con lluvia, fogata calienta),
  radiación en ruinas, daño por caída (desde 4 m; 19 m mata).
- Recolección con puntos débiles (X en el árbol x2 madera, brillo en la roca x1.5), crafteo con cola,
  mesa de trabajo y planos, recicladora, horno y fogata, desgaste de herramientas.
- Construcción en rejilla con pisos, escaleras, ventanas; niveles ramas/madera/piedra/metal; martillo
  para mejorar, reparar y demoler; puertas de madera y chapa; candados de código; armario (TC) con
  permisos y mantenimiento (upkeep); muros altos de madera/piedra con escalera; caja y caja grande
  (24 casillas, 2 páginas).
- Combate: lanza, arco, ballesta, machete, espada, trabuco, revólver, rifle semiautomático; tiros a la
  cabeza x2; armadura y ropa; vendas y jeringas; C4 (satchel) y granada de lata para asaltar.
- Mundo vivo: animales (jabalíes, ciervos, lobos en manada), bandidos PvE con IA, durmientes
  (los desconectados quedan dormidos y se pueden matar), bolsa con el botín al morir, saco de dormir para
  reaparecer, lanzamientos aéreos con avión, barriles y cajas de botín.
- Multijugador: servidor autoritativo, predicción de movimiento, chat, equipos, nombres sobre la cabeza.
- Tienda de skins con Oxcoins (sin pagos reales todavía).

### Mecánicas agregadas del 2026-11-15 al 2026-11-17 (cada una con pruebas Core + prueba web)

| # | Mecánica | Detalles clave | Prueba web |
|---|---|---|---|
| 1 | Granada de lata | `ChargeKind.Beancan`, se lanza lejos, daña poco las paredes | grenade.js |
| 2-8 | Muros altos + escalera, temperatura, radiación, machete/espada, ballesta, caja grande, pesca | caña `FishingRodTool`, ActKind Cast/Reel | phase3.js 18/18, walls.js |
| 9-11 | Botes de remos, lancha a motor con combustible, minicóptero | | boats.js, motorboat.js, copter.js |
| 12 | Cultivos, caballos, torreta automática, máquina expendedora | cultivos crecen más con lluvia y se marchitan; 6 caballos con galope y aliento; torreta gira 360° a <5 m; expendedora con 4 ofertas | crops.js, horse.js, turret.js, vending.js |
| 13 | Puntos débiles, cabeza x2, caídas; electricidad | generador con combustible, panel solar, batería, interruptor, lámpara, cable; la torreta pide 10 W | polish.js 7/7, electric.js 12/12 |
| 14 | Sensor de presencia, sirena, controlador de puerta | el sensor deja pasar energía si ve a un extraño a <10 m | alarm.js 13/13 |
| 15 | IA de bandidos | se avisan por radio a <40 m, cargador de 6 y recarga, se mueven de lado, con poca vida se cubren y se vendan, te buscan donde te vieron; animaciones y sonidos de otros jugadores (pasos, golpes, recarga) | bandits2.js 11/11, swing.js 7/7 |
| 16 | Lobos en manada | 2 manadas de 3, el que te ve aúlla y trae a los demás, entran por los lados, huyen con <30% | wolves.js 5/5 |
| 17 | Caja bloqueada | aparece en un puesto militar a los 20 min y cada ~1 h, hackeo de 5 min que despierta a los guardias, botín top | crate.js 8/8, crate-online.js 8/8 |
| 18 | Helicóptero de patrulla | llega a los 30 min y cada ~2 h, patrulla 15 min, te ve sin techo a <95 m, ametralladora y salvas de 4 cohetes, 2000 de vida (rotor x3), al caer deja restos y 3 cajas que arden 2 min | heli.js 13/13, heli-online.js 10/10 |
| 19 | Cohetes del heli dañan construcciones | ver abajo | rockets.js **sin correr** |

Capturas de cada fase: carpeta compartida del proyecto en claude.ai (`oxfall/fase2` … `oxfall/fase11`,
`oxfall/ui-v2`, `oxfall/capturas`). No están en el repo.

### Fase 12 (la última): cohetes contra bases
- `Explosives.RocketBlast` (`Core/Rules/Explosives.cs`): cada cohete quita hasta 90 a lo que esté a
  menos de 3.2 m (menos cuanto más lejos). Madera y ramas reciben todo (una pared de madera cae con una
  salva de 3 a 4 cohetes), piedra x0.45 (unas tres salvas), metal x0.25 (aguanta), puerta de chapa x0.4.
  Una pared o un techo protegen lo que está detrás. Lo colocado cerca (cajas, etc.) recibe la mitad de la
  onda de un C4.
- `Explosives.FirstStop`: el cohete revienta en la primera pared, techo o muro alto que encuentre en su
  camino.
- `PatrolHelis.RocketBurst`: el servidor (`ServerWorld.Helis.cs`) y el juego en solitario
  (`OxfallGame.Helis.cs`) aplican el daño; en línea los cambios viajan como `BuildOp.Health` normales.
- Si le disparas desde cubierto, el heli no te ve pero sabe de dónde vinieron los tiros y cohetea ahí
  (como en Rust de PC).
- Aviso en pantalla cuando los cohetes revientan cerca de una base.
- Arreglado de paso: `Explosives.Shielded` dejaba pasar la vista por el borde de una casilla con techo
  (el heli veía y disparaba a jugadores bajo techo).
- Pruebas: `tests/Oxfall.Core.Tests/HeliRocketTests.cs` (6). Prueba web: `tools/webtest/rockets.js`.

## 7. Comandos de depuración

`DebugCommand` en `Game/OxfallGame.cs` (y `HeliCommand` en `OxfallGame.Helis.cs`). Para ver la lista
exacta: `grep -ohE 'case "[a-z0-9:-]+"' Assets/Oxfall/Scripts/Game/*.cs`. Los más usados:

- **Mirar y moverse:** view-hill, view-forest, view-sea, view-pond, view-ruin:N, to-ruins, to-outpost,
  to-bandit, to-recycler, go-shore, go-boat, go-horse, go-copter, go-motorboat, go-crate, go-wreck,
  face-*, look-*, turn-*, fwd-*, back-*, tower-top, map.
- **Dar cosas:** give-all, give-guns, give-armour, wear-armour, give-raid, give-blades, give-walls,
  give-electric, give-turret, give-vending, give-seeds, give-fuel, give-coins, learn-all, heal, hungry,
  starve.
- **Mundo:** time-H (time-12, time-23), rain-*, fog-*, weather-auto, freeze-animals, thaw-animals,
  spawn-boar/deer/wolf, wolf-pack, wolves, bandits, hurt-bandit, close-bandit, airdrop, locked-crate,
  crate-here, crate-done, crate-state.
- **Construir:** build-hut, build-tower, build-compound, upgrade-all-1/2/3, place-door, place-lock,
  satchel, beancan, throw.
- **Helicóptero:** heli, heli-here, heli-hurt, heli-down, heli-clear, heli-state, view-heli,
  shoot-heli, heli-cool, open-heli-crate, y para la fase 12: rocket-hut (choza de madera con el jugador
  dentro), heli-aggro (como si le disparara desde donde está), hut-state, view-hut, into-hut.
- **Calidad y estado:** quality-low/medium/high, stats, hide-hud.

## 8. Dónde quedó exactamente y cómo seguir

1. **Pendiente inmediato:** con la versión web nueva de la rama `53puct` ya publicada en `web-latest`,
   correr `node tools/webtest/rockets.js` (solo). Debe dar todo PASS: choza en pie → el heli la cohetea
   → cae una pieza → aviso → sin errores. Si algo falla, arreglarlo antes de seguir. Mirar las capturas.
2. **Ideas de mecánicas siguientes** (estilo Rust de PC): animación del golpe de otros jugadores vista
   en la web, cohetes incendiarios que queman madera, el heli que ataca bases con luces encendidas de
   noche, tren/cargo, más monumentos.
3. **Para lanzar (pendiente, decide el dueño):**
   - Alojar el servidor en Oracle Free con `wss://` (ver sección 4).
   - Pagos reales de Oxcoins (Google Play Billing).
   - Skins con patrón (las mallas necesitan UVs).
   - Repo privado antes de meter assets comprados o de lanzar.
   - Cuenta de Google Play (25 USD) y prueba cerrada de 12 personas por 14 días (la comunidad de Telegram
     sirve). Identificador `com.oxfall.survival` (fijo tras la primera subida).
   - Medir FPS reales en el Redmi 12C.
4. **Cómo se agrega una mecánica:** reglas y constantes en `Core/Rules` (o `Wildlife`, `World`), su
   archivo de pruebas en `tests/Oxfall.Core.Tests`, la parte del servidor en `Core/Net/ServerWorld.*.cs`,
   mensajes nuevos en `Protocol.cs` (subir la versión), la vista en `Game/World` o `Game/UI`, comandos
   de depuración para probarla y una prueba en `tools/webtest/<mecánica>.js`. Correr `dotnet test` y
   `compilecheck` antes de cada push.

# Oxfall: Survival Online

Juego de supervivencia multijugador para celular (Unity 6 URP + servidor propio en .NET 8).

**Empieza por [`docs/ESTADO.md`](docs/ESTADO.md)**: qué es, qué está hecho, la arquitectura, cómo
compilar, probar y desplegar, y dónde quedó el trabajo. La hoja de ruta original está en
[`docs/PLAN.md`](docs/PLAN.md).

- APK de Android (última compilación): <https://github.com/aleanfx/Rust-Mobile/releases/latest/download/Oxfall.apk>
- Versión web: <https://aleanfx.github.io/Rust-Mobile/> (`?solo` para jugar solo)
- Pruebas de reglas: `cd tests/Oxfall.Core.Tests && dotnet test`
- Servidor local: `cd server/Oxfall.Server && OXFALL_DEV=1 dotnet run -- --port 7777`
- Servidor en Oracle Cloud gratis: [`deploy/oracle/LEEME.md`](deploy/oracle/LEEME.md)

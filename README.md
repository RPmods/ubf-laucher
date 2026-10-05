# UBF y UBFLauncher

El juego UBF (Unreal Engine 5.8.2) y UBFLauncher (WPF/.NET 8) son productos distintos y usan versionado, repositorios, tags y Releases independientes.

## Juego UBF

El juego se publica como ZIP en `RPmods/ubf`. `manifest.json` en la rama `main` identifica la versión del juego, la URL, tamaño y SHA-256 del ZIP, más los hashes y tamaños de los archivos instalados. El launcher lee el manifest para verificar la instalación; la opción VERIFICAR no descarga el ZIP. La descarga solo ocurre al instalar, actualizar o reparar.

El flujo de presentación construye el árbol UMG en `NativeOnInitialized`, antes de que Slate componga el primer frame. Los dos MP3 de la intro son `SoundWave` UAssets y se reproducen con componentes de audio 2D: `musicintro` inicia primero y su callback inicia `musicintrobucle`. `intro.mp4` conduce al menú; el video de fondo se abre solo al entrar al menú, con un fondo estático disponible si falla la reproducción. El empaquetado conserva los videos como archivos no UFS y cocina las SoundWave.

Guía de build, validación y publicación: [UBF_DISTRIBUTION_SETUP.txt](UBF_DISTRIBUTION_SETUP.txt).

## UBFLauncher

La Release pública del launcher es v1.0.18 y apunta al juego UBF v1.0.6-beta. Los números no se sincronizan: publicar una actualización del launcher no crea, reemplaza ni retira una Release del juego. El ZIP del launcher y su `version.json` pertenecen a `RPmods/ubf-laucher`; el ZIP del juego y `manifest.json` pertenecen a `RPmods/ubf`.

JUGAR inicia `UBF/Binaries/Win64/UBF-Win64-Shipping.exe`, cambia a `INICIANDO...` y bloquea clics repetidos. El audio del launcher se pausa mientras UBF está abierto.

```powershell
dotnet build .\UBFLauncher.slnx -c Release
dotnet test .\tests\UBFLauncher.Tests\UBFLauncher.Tests.csproj -c Release
.\publish.ps1
```

`publish.ps1` genera el launcher y updater self-contained en `publish/`, valida los ejecutables y los recursos, excluye símbolos PDB de depuración y crea `UBFLauncher-update.zip`. Solo limpia salidas dentro de ese directorio local; no cambia las Releases remotas.

## Repositorios

- Juego: https://github.com/RPmods/ubf
- Launcher: https://github.com/RPmods/ubf-laucher

Usa Git LFS para recursos de video grandes. No subas salidas generadas ni credenciales.

## Releases actuales

- Juego UBF v1.0.6-beta: https://github.com/RPmods/ubf/releases/tag/v1.0.6-beta. Consulta la Release para obtener el ZIP y su metadata vigentes.
- UBFLauncher v1.0.18: https://github.com/RPmods/ubf-laucher/releases/tag/launcher-v1.0.18. ZIP `UBFLauncher-update.zip`, 350094392 bytes, SHA-256 `12de979b94488206c829c2b858d0621930c94777b3b9bb1198273f83882bc685`.
- La compilación y 25 pruebas automatizadas pasaron. La inspección visual de esta build no se pudo hacer porque el control de escritorio de esta sesión no expone ventanas de Windows.

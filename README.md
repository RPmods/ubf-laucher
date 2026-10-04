# UBF y UBFLauncher

El juego UBF (Unreal Engine 5.8.2) y UBFLauncher (WPF/.NET 8) son productos distintos y usan versionado, repositorios, tags y Releases independientes.

## Juego UBF

El juego se publica como ZIP en `RPmods/ubf`. `manifest.json` en la rama `main` identifica la versión del juego, la URL, tamaño y SHA-256 del ZIP, más los hashes y tamaños de los archivos instalados. El launcher lee el manifest para verificar la instalación; la opción VERIFICAR no descarga el ZIP. La descarga solo ocurre al instalar, actualizar o reparar.

El flujo de presentación construye el árbol UMG en `NativeOnInitialized`, antes de que Slate componga el primer frame. Los dos MP3 de la intro son `SoundWave` UAssets y se reproducen con componentes de audio 2D: `musicintro` inicia primero y su callback inicia `musicintrobucle`. `intro.mp4` conduce al menú; el video de fondo se abre solo al entrar al menú, con un fondo estático disponible si falla la reproducción. El empaquetado conserva los videos como archivos no UFS y cocina las SoundWave.

Guía de build, validación y publicación: [UBF_DISTRIBUTION_SETUP.txt](UBF_DISTRIBUTION_SETUP.txt).

## UBFLauncher

Launcher v1.0.11 apunta al juego UBF v1.0.4-beta. Los números no se sincronizan: publicar una actualización del launcher no crea, reemplaza ni retira una Release del juego. El ZIP del launcher y su `version.json` pertenecen a `RPmods/ubf-laucher`; el ZIP del juego y `manifest.json` pertenecen a `RPmods/ubf`.

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

- Juego UBF v1.0.4-beta: https://github.com/RPmods/ubf/releases/tag/v1.0.4-beta. ZIP `UBF-v1.0.4-beta.zip`, 488806022 bytes, SHA-256 `5055229aed8d623e1da2131fc92df26029c052bd0e329837b355b29b5b5239e1`.
- UBFLauncher v1.0.11: https://github.com/RPmods/ubf-laucher/releases/tag/launcher-v1.0.11. ZIP `UBFLauncher-update.zip`, 350087498 bytes, SHA-256 `ab1167e5ec791f414a3f5673e143a802804279c0531936d6642ed713066b526c`.
- Tras verificar v1.0.11, se retiró la Release del launcher v1.0.10; su tag y sus fuentes siguen en Git. Se conservan las cuatro Releases del juego, desde v1.0.1-beta hasta v1.0.4-beta.

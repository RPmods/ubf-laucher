# UBF y UBFLauncher

Proyectos separados: juego UBF (Unreal Engine 5.8) y launcher WPF (.NET 8).

## Distribución del juego

UBF se publica como ZIP en una Release de RPmods/ubf. `manifest.json` se publica separado en la rama principal, con la versión, URL, tamaño y SHA-256 del ZIP, más hashes y tamaños por archivo extraído.

VERIFY solo lee el manifiesto y compara los hashes locales; no descarga el ZIP. INSTALL/UPDATE/REPAIR descarga y extrae el paquete únicamente al confirmar esa acción. Una instalación que coincide con la versión publicada queda lista sin descargarlo. Usa como carpeta de instalación la raíz del paquete que contiene `UBF.exe`, `Engine/` y `UBF/`; no selecciones `Binaries/Win64` del proyecto fuente.

Guía completa: [UBF_DISTRIBUTION_SETUP.txt](UBF_DISTRIBUTION_SETUP.txt).

## UBFLauncher

Launcher 1.0.10 y juego 1.0.3-beta tienen versionado separado. El launcher se publica en RPmods/ubf-laucher con tag/Release y ZIP propios. `version.json` contiene la URL directa y SHA-256 del ZIP publicado; `version.json.example` es plantilla.

JUGAR abre `UBF/Binaries/Win64/UBF-Win64-Shipping.exe`, muestra `INICIANDO...` y bloquea clics repetidos hasta que el juego cierre. El launcher mantiene su música pausada mientras UBF está abierto.

```powershell
dotnet build .\UBFLauncher.slnx -c Release
dotnet test .\tests\UBFLauncher.Tests\UBFLauncher.Tests.csproj -c Release
.\publish.ps1
```

`publish.ps1` crea las salidas self-contained bajo `publish/`. No modifica Releases remotas.

## Repositorios

- Juego: https://github.com/RPmods/ubf
- Launcher: https://github.com/RPmods/ubf-laucher

Usa Git LFS para videos grandes. No subir outputs generados ni credenciales.

## Releases publicadas

- Juego UBF v1.0.3-beta: [Release](https://github.com/RPmods/ubf/releases/tag/v1.0.3-beta), ZIP 435204941 bytes; SHA-256 `51a2b972429c617d59f9b1b655251f606b95f31b655a48c68fb6fbd3c8fd4664`.
- UBFLauncher v1.0.10: [Release](https://github.com/RPmods/ubf-laucher/releases/tag/launcher-v1.0.10), ZIP 350125627 bytes; SHA-256 `1a622a670b89329e004e6e51b9dc840205b9260d37a13cd0a7e837abf930ff98`.
- Se conservan UBF v1.0.1-beta y v1.0.2-beta porque son versiones independientes del juego. Tras verificar UBFLauncher v1.0.10, se retiró la Release anterior del launcher v1.0.9; su tag y fuentes siguen en Git.
- El manifiesto del juego y `version.json` del launcher están publicados en sus respectivas ramas `main`.



# UBF y UBFLauncher

Proyectos separados: juego UBF (Unreal Engine 5.8) y launcher WPF (.NET 8).

## Distribución del juego

UBF se publica como ZIP en una Release de RPmods/ubf. `manifest.json` se publica separado en la rama principal, con la versión, URL, tamaño y SHA-256 del ZIP, más hashes y tamaños por archivo extraído.

INSTALL/UPDATE/REPAIR valida y extrae el ZIP en staging. VERIFY compara archivos sin descargarlo. JUGAR usa UBF.exe local aunque GitHub no responda.

Guía completa: [UBF_DISTRIBUTION_SETUP.txt](UBF_DISTRIBUTION_SETUP.txt).

## UBFLauncher

Launcher 1.0.7 y juego 1.0.2-beta tienen versionado separado. El launcher se publica en RPmods/ubf-laucher con tag/Release y ZIP propios. `version.json` contiene la URL directa y SHA-256 del ZIP publicado; `version.json.example` es plantilla.

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

- Juego UBF v1.0.2-beta: [Release](https://github.com/RPmods/ubf/releases/tag/v1.0.2-beta), ZIP 465028352 bytes; SHA-256 `253b5eb3a78a2d3042e0b5136e3801c1109b261ca9e06419e11ec1915f56fb85`.
- UBFLauncher v1.0.7: [Release](https://github.com/RPmods/ubf-laucher/releases/tag/launcher-v1.0.7), ZIP 350125294 bytes; SHA-256 `f0adff975d889ab6b76eae3476e4f8894a36f37f8ad4e2b21c9d1e2d62426ff9`.
- Se conserva UBF v1.0.1-beta porque es una versión independiente del juego. La obsoleta Release del launcher v1.0.6 fue retirada después de verificar v1.0.7; su tag e historial Git se conservan.
- El manifiesto del juego y `version.json` del launcher están publicados en sus respectivas ramas `main`.

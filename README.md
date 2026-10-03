# UBF y UBFLauncher

Proyectos separados: juego UBF (Unreal Engine 5.8) y launcher WPF (.NET 8).

## Distribución del juego

UBF se publica como ZIP en una Release de RPmods/ubf. manifest.json se publica separado en la rama predeterminada, con versión, URL/tamaño/SHA-256 del ZIP y hashes/tamaños por archivo extraído.

INSTALL/UPDATE/REPAIR valida y extrae el ZIP en staging. VERIFY compara archivos sin descargarlo. JUGAR usa UBF.exe local aunque GitHub no responda.

Guía completa: [UBF_DISTRIBUTION_SETUP.txt](UBF_DISTRIBUTION_SETUP.txt).

## UBFLauncher

Launcher 1.0.6 y juego 1.0.1-beta tienen versionado separado. El launcher se publica en RPmods/ubf-laucher con tag/Release y ZIP propios. version.json debe tener URL directa y SHA-256 reales; version.json.example es plantilla.

```powershell
dotnet build .\UBFLauncher.csproj -c Release
dotnet test .\tests\UBFLauncher.Tests\UBFLauncher.Tests.csproj -c Release
.\publish.ps1
```

publish.ps1 crea las salidas self-contained bajo publish/. No modifica Releases remotas.

## Repositorios

- Juego: https://github.com/RPmods/ubf
- Launcher: https://github.com/RPmods/ubf-laucher

Usa Git LFS para videos grandes. No subir outputs generados ni credenciales.

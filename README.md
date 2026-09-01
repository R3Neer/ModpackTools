# ModpackTools

ModpackTools es una CLI pequeña para administrar varios modpacks de Minecraft Java basados en Packwiz y exportarlos a Modrinth. Su único comando público es `modpack`.

## Filosofía y fuentes de verdad

- Packwiz conserva los datos técnicos: nombre técnico, fichero, versión, side, proveedor, IDs, URLs y hashes.
- `.modpack` conserva solo identidad y decisiones editoriales: ID corto, nombre/versión de presentación, nombre del artefacto, categorías, notas y overrides de nombres.
- `config/defaultoptions-common.toml` conserva el orden de resource packs activos.
- `dist/` contiene resultados generados y nunca es fuente del modpack.

Un mod sin metadata sigue siendo válido y aparece en `MODS · SIN CLASIFICAR`. Una referencia a una categoría inexistente produce un warning y no se descarta.

## Requisitos e instalación

- Windows y PowerShell 7.
- Packwiz disponible en `PATH`.

Desde el directorio del código fuente:

```powershell
./Install-ModpackTools.ps1
```

El instalador copia la versión del módulo al primer directorio de usuario de `PSModulePath`. No modifica `$PROFILE`; PowerShell autocarga `modpack` al utilizarlo.

## Configuración inicial

```powershell
modpack config set root "D:\Minecraft"
modpack config get root
```

La configuración se guarda en la carpeta estándar `LocalApplicationData\ModpackTools\config.psd1` del usuario.

## Estructura de proyecto

```text
MiPack-1.21/
├── pack.toml
├── index.toml
├── README.md
├── .gitignore
├── .modpack/
│   ├── project.psd1
│   └── metadata.psd1
├── mods/
├── config/
├── resourcepacks/
├── shaderpacks/
└── dist/
```

### `project.psd1`

```powershell
@{
    SchemaVersion  = 1
    Id             = 'ejemplo'
    DisplayName    = 'Mi Pack'
    DisplayVersion = '1.21'
    OutputName     = 'MiPack-1.21.mrpack'
}
```

Minecraft, loader y versión técnica no se duplican: se leen de `pack.toml`. `DisplayName` y `DisplayVersion` son conceptos editoriales y pueden diferir de los valores técnicos.

### `metadata.psd1`

```powershell
@{
    Categories = @{
        performance = @{ Name = 'RENDIMIENTO'; Order = 10 }
    }
    Mods = @{
        'modrinth:AANobbMI' = @{ Category = 'performance' }
    }
    ResourcePacks = @{
        '$polymer-resources' = @{ Name = 'Polymer Resources' }
    }
}
```

Las claves de mods son IDs estables con namespace: `modrinth:<id>`, `curseforge:<id>`, `packwiz:<ruta>` o `local:<ruta>`.

## Comandos

```powershell
modpack help
modpack help build
modpack list
modpack use vp26
modpack use
modpack status
modpack status vp26 --full
modpack inventory
modpack inventory --category performance
modpack inventory --side host --source local
modpack inventory --type resourcepack --state active
modpack inventory --search sodium
modpack add mod sodium --category performance
modpack add mod sodium --project vp26 --category performance
modpack build
modpack build vp26 --keep-old --raw-log
```

Un ID explícito tiene prioridad sobre la selección de la sesión. `modpack use` solo afecta al proceso actual de PowerShell.

### Consultar y filtrar el inventario

`modpack inventory [id]` muestra mods, resource packs activos e inactivos y shaders. Los filtros se pueden combinar:

- `--type all|mod|resourcepack|shaderpack`
- `--category <id|unclassified>` o `--unclassified`
- `--side client|host|both|unknown`
- `--source packwiz|local|builtin|missing`
- `--state all|active|inactive`
- `--search <texto>` busca en nombre, ID y filename.

`host` corresponde al valor técnico `server`. Los filtros de categoría o side acotan automáticamente a mods; `--state` acota a resource packs.

### Crear un proyecto

```powershell
modpack new demo --name Demo --minecraft 1.21.1 --loader fabric
```

Por defecto usa el Fabric Loader más reciente compatible que seleccione Packwiz, versión técnica `0.1.0`, versión de presentación igual a Minecraft y carpeta `<Name>-<Minecraft>`. Se puede ajustar con `--loader-version`, `--pack-version`, `--display-version` y `--path`. La creación se realiza en un directorio temporal y el destino no se sobrescribe.

### Añadir un mod

`modpack add mod` delega la selección y escritura técnica en `packwiz modrinth add`. Después lee el `.pw.toml` generado. Si se indica `--category`, solo se escribe esa decisión en metadata; la categoría debe existir previamente.

### Construir

`modpack build` valida el proyecto, ejecuta `packwiz refresh` y `packwiz modrinth export`, y publica el resultado en `dist/`. Exporta primero a un fichero temporal para no destruir un artefacto correcto si Packwiz falla.

Un `.mrpack` que permanezca en la raíz de un proyecto migrado es un artefacto legado que ModpackTools conserva para no borrar datos. Los builds nuevos y sus reemplazos viven exclusivamente en `dist/`; el fichero legado puede eliminarse manualmente cuando ya no se necesite.

- `--no-refresh`: omite `packwiz refresh`.
- `--keep-old`: si existe el nombre habitual, genera el nuevo con timestamp.
- `--raw-log`: muestra también las líneas repetitivas `added to manifest`.
- `--open`: abre Explorer seleccionando el resultado.

## Inventario y sides

Los `.pw.toml` suministran `client`, `server` o `both`. Para JAR locales se lee `fabric.mod.json`; si no se puede determinar, se usa `unknown`.

```text
[C]    solo cliente
[H]    solo host/server
[C][H] ambos
[?]    desconocido
```

La UI dice “Host”, aunque el valor técnico de Packwiz sea `server`.

## Default Options

Si existe `config/defaultoptions-common.toml`, `defaultResourcePacks` es la fuente del orden activo. Default Options guarda de menor a mayor prioridad; ModpackTools invierte la lista para mostrar la prioridad real de la GUI. El parser respeta corchetes, apóstrofes, símbolos y escapes dentro de strings. Los IDs integrados sin override se muestran literalmente. Los ZIP físicos no incluidos en esa lista aparecen como presentes pero inactivos.

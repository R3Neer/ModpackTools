# Entrega nativa y registro de activación

## Actualización: activación ejecutada el16-oct-2026

El usuario autorizó instalar y configurar Nu. Ya está instalada la imagen de este
documento en `D:\Juegos\Minecraft\Herramientas\ModpackTools-App` y el bloque Nu
real importa su control. El resto del documento conserva el plan/estado previo
como registro histórico; sus referencias a activación pendiente quedan superadas.

Respaldo con hashes y restauración verificada en
`D:\Backup\Previos\ModpackTools-Native-20261016-Activacion-01`. Sólo cambió la línea
del import Nu; configuración/cache anterior473 archivos y módulo legacy67 archivos
mantienen hashes. Env.nu, scripts, Helix, Show-Tree y registro de plugins conservados.
No se cambió PATH ni se retiró legacy. Se añadió sólo el estado normal del instalador.

Pasan Nu nuevo con configuración real, versión/ayuda/catálogo (vp26), resolución D,
comandos con PATH vacío, launcher estable y self-update del mismo paquete. Un nuevo
proyecto aislado pasa clasificación/doctor/build/diff con Packwiz real: MRPack3917
bytes, seis entradas/overrides verificados, diff0 y dry-run sin cambios.
Evidencia `assessment/native-activation-20261016/`. Registro operativo separado:
`09-Logs/2026-10-16/ActivacionModpackToolsNativo.md` en la reorganización.

No push/publicación/merge. Abrir una sesión Nu nueva para cargar el import activo;
no se han cerrado sesiones ajenas. Retirada legacy queda fuera de esta activación.

Fecha: 2026-10-16. Este documento sustituye como procedimiento vigente las rutas
previas a la reorganización; no vuelve a autorizar ni ejecutar un traslado.
Implementación nativa y aceptación local cerradas; activación personal pendiente.
La prueba instalada en D y las medidas públicas están completadas, con los límites
de validación indicados abajo. No se ha hecho push, merge, tag ni publicación.

## Estado y destinos

| Componente | Ruta / decisión |
|---|---|
| Fuentes principales | `D:\Juegos\Minecraft\Herramientas\ModpackTools`, main025b7f3 limpio; ya trasladado por el otro agente. |
| Desarrollo nativo | `C:\Users\Usuario\Desktop\ReorganizacionPC\work\ModpackTools`, rama codex/hybrid-core; worktree vinculado reparado. No fusionar toda su historia en main. |
| Instalación legacy activa | `D:\Desarrollo\Toolchains\PowerShell\Modules\ModpackTools`. Se conserva; no activar una imagen mezclada ni reinstalar aquí el paquete nativo. |
| Aplicación nativa propuesta | `D:\Juegos\Minecraft\Herramientas\ModpackTools-App`; no existe al recapturar. Raíz independiente explícita, fuera de fuentes, Toolchains y OneDrive. |
| Binarios instalados | `bin/modpack.exe`, `bin/modpacktools-setup.exe`; versiones inmutables y control dentro de la raíz de aplicación. |
| Configuración/datos/cache | `%LOCALAPPDATA%\ModpackTools`, actualmente C; Root=`D:\Juegos\Minecraft`, PackwizPath sin override. Conservar. |
| Recuperación | `%LOCALAPPDATA%\ModpackTools\native-state`, salvo MODPACKTOOLS_STATE_HOME explícito. No cambiar el namespace con journals pendientes. |
| Nushell | `D:\Desarrollo\Toolchains\Nushell\bin\nu.exe`, 0.116.2. Configuración AppData C, modal/scripts/plugins sin modificar. |
| R3CLI | Paquete Nu0.5.2 emparejado y verificado dentro de cada versión; no depende del módulo PS externo. |
| Packwiz | Configurable o instalación gestionada bajo configuración/tools/packwiz/dfd8b68. Nunca se instala en Documentos ni se modifica PATH automáticamente. |
| PowerShell/perfiles | PS7 y Windows PowerShell se conservan donde están. La nueva aplicación no los necesita, instala ni ofrece instalarlos. Otros consumidores/perfiles pertenecen a la reorganización general. |

Las rutas antiguas `D:\Tools\ModpackTools` y el módulo de
`C:\Users\Usuario\Documents\PowerShell\Modules\ModpackTools` están ausentes.
No hay submódulos. El payload nativo contiene17 archivos más su manifiesto,
sin scripts PowerShell, código fuente, documentación de migración ni una copia
del módulo legacy. No se ha activado la aplicación personal ni publicado versiones.

## Paquete preparado

`C:\Users\Usuario\Desktop\ReorganizacionPC\assessment\native-final\ModpackTools-Native-win-x64-4.0.2.zip`

SHA256: `D85A63443B3F0EC387B2DFC286CD0E1F4749E4DD8AF53A25ABEF72DFE4C2EFE5`.
Build: `0D475640FB32D7E67C46F0EC58815E59304C1F3C15D970AD08A30769A6C1F401`.
CLI196, Launcher193 y Setup194 son Native AOT win-x64; no SDK/runtime .NET,
Python o PowerShell se distribuye ni ejecuta para el uso normal.
La release pública estable4.0.2 aún carece de un asset nativo elegible. El check
remoto funciona; la aplicación real de actualizaciones se prueba con ZIP local
y transporte controlado. No se afirma una descarga pública que no existe.

## Pruebas y resultado final

- Contratos nativos, protocolo15, versión y suite Nu completa pasan sobre CLI196.
  Incluye rutas con espacios/Unicode, configuración privada, selección de sesión,
  operaciones de contenido/clasificación/recursos, doctor, planes y exportación
  con Packwiz real. Las pruebas específicas de instalación/update/rollback,
  retirada reanudable, hash inválido y aprobación obsoleta también pasan.
- Revisor negro:2 escenarios y15 operaciones públicas sobre instalación temporal
  REAL en D, con preparación/configuración/estado/TEMP en C y PATH vacío.
  Import generado, versión/ayuda, registro/selección, categoría, inventario/status,
  doctor, build dry-run/aplicado y diff pasan. Doctor:0 fallos obligatorios y3
  recomendaciones opcionales. MRPack real3917bytes,6 entradas de índice y6
  overrides verificados; diff sin diferencias. No fue una simulación de Packwiz.
- La carpeta D de QA fue retirada tras verificar identidad fsutil, marcador,
  creación, ausencia de enlaces y handles. Proyecto y evidencia permanecen sólo
  bajo assessment/review-blackbox/runs/final-062717-5b230ae0. No se retiró el origen
  legacy ni se cerró una sesión ajena. Informe: final-installed-native-acceptance-report.md.
- Dos revisores independientes cerraron instalación, guardas, actualización,
  retirada y transporte. La revisión blanca también encontró/corrigió incoherencias
  de errores, preparación temporal, receipts, guía de instalación y CI.
- Rendimiento medido: inventario Nu20 mods locales1473→104ms (14,15x),100 mods
  gestionados/caché verificada2052→300ms (6,83x), versión942→43ms. Medianas de31
  muestras alternadas, caches privadas, datos de máquina iguales. Humano20 mods
  p95418ms. El launcher estable verifica más integridad: versión mediana226ms,
  p95247ms; el core directo23/33ms. No se debilitó hashing para acelerar.
- Auditoría blanca final:256 muestras públicas y64 de proceso fresco coinciden
  con los datos crudos;31+primera observación y orden alternado por serie.
  Cuatro preflights completos son saludables e idénticos por pareja; paquete18
  archivos, proyectos24/104 archivos y cuatro anclas legacy conservan hashes.
  Informe assessment/review-whitebox/public-native-benchmark-final-review.md.

Limitaciones explícitas: no publicación/descarga de un asset nativo público aún
inexistente; CI hospedado no ejecutado sin push; no QA de arranque de Minecraft,
disco físicamente frío, memoria máxima del árbol de procesos, escalado500/1000
ni ensayo destructivo real de disco lleno. Las pruebas de fallos/journals cubren
los casos documentados, no todos los estados posibles de Windows. Estas reservas
no representan una ruta de ejecución pendiente hacia PowerShell.

Los objetivos de latencia medidos pasan en los fixtures declarados. Datos crudos
en docs/migration/benchmarks/public-native-results.json y assessment/benchmarks/
public-native-759bafc4848148dfab7791852f9c8c30. El primer preflight incompleto se
retuvo y corrigió en el fixture, sin modificar el producto ni ocultar muestras.

**Es seguro preparar la activación con el paquete identificado y el procedimiento
de respaldo/validación siguiente. No hay otro traslado de fuentes que ejecutar.**
La retirada de legacy sigue supeditada a validar los consumidores reales después
del cambio autorizado. Un paquete probado no sustituye esa comprobación personal.

## Consumidores que cambiarán al activar

| Consumidor | Estado actual / cambio acotado |
|---|---|
| `C:\Users\Usuario\AppData\Roaming\nushell\config.nu`, líneas33–35 | Importa `D:/Desarrollo/Toolchains/PowerShell/Modules/ModpackTools/Nushell/modpack.nu` main. Sustituir sólo el import del bloque marcado por el nuevo control import. |
| `env.nu` | No necesita cambio. Conservar conversiones, PATH y scripts. |
| Registro personal de plugins | No se crea, activa ni modifica. Configuración actual no necesita plugin para ModpackTools. |
| Comando externo | Usar la ruta completa al nuevo bin/modpack.exe o añadir sólo ese bin a PATH con autorización de activación. Nu usa el import; no requiere ese cambio. |
| PowerShell importado en sesiones previas | Es otro consumidor. Cerrar/reabrir tras la activación; identificar imports explícitos legacy antes de retirar el módulo. No migrar perfiles ajenos. |
| IDE/Codex | Fuente principal y worktree ya reparados; no cambiar sus rutas por la activación de una imagen runtime. |
| Tareas/accesos directos | La auditoría anterior no detectó consumidores; recapturar antes de retirar legacy. No asumir que una captura antigua cubre consumidores nuevos. |

Hashes recapturados de sólo lectura: config.nu
`86230E0E596B6EF8B51EE87E4564E9D69A8522C3913CB1EB56A7E7E2F1BE34E5`;
env.nu `4B6DC8934500D665164775A5CD188D59910F50C3089E9ACBB04047B125A52C3F`.
Si cambian antes de activar, volver a leer y aplicar el cambio al contenido actual.
No reemplazar un perfil entero con esta captura. No se modificaron estos archivos.
No hay theme.toml personal en el directorio actual al cierre; la presentación
probada usa los recursos emparejados del paquete.

## Ejecución posterior, sólo tras indicación del usuario

1. Pausar operaciones ModpackTools y escritores sobre los proyectos durante la
   copia de configuración y validación. Esperar procesos modpack/Packwiz propios;
   no matar procesos ajenos. Guardar trabajo y cerrar Nu/PowerShell que hayan
   cargado ModpackTools antes de retirar una instalación. No hace falta cerrar
   Minecraft ni otras aplicaciones sin archivos implicados. Si sólo se activa
   el runtime no es necesario mover fuentes ni cerrar el editor de fuentes.
2. Recapturar Git/consumidores/configuración/journals y las identidades físicas.
   Respaldar fuera de los árboles implicados, por ejemplo un lote nuevo bajo
   `D:\Backup\Previos\ModpackTools-Native-<fecha>`. Conservar configuración,
   cache, recuperación, instalación legacy y archivos Nu a cambiar; generar
   manifiesto SHA256 por archivo y probar restauración en una carpeta separada.
   No hacer una copia inconsistente de journals activos. Si se archiva o mueve
   desarrollo, incluir .git principal, worktree, commits, untracked e ignorados
   necesarios; git bundle solo no conserva esos archivos. No limpiar ni restaurar.
3. Verificar SHA del ZIP contra este registro antes de ejecutar cualquier helper.
   Extraer en un directorio temporal de preparación. Desde Nu:

   ```nu
   ^'C:/Users/Usuario/Desktop/ReorganizacionPC/assessment/native-final/package/modpacktools-setup.exe' install --root 'D:/Juegos/Minecraft/Herramientas/ModpackTools-App' --nu 'D:/Desarrollo/Toolchains/Nushell/bin/nu.exe' --package 'C:/Users/Usuario/Desktop/ReorganizacionPC/assessment/native-final/ModpackTools-Native-win-x64-4.0.2.zip' --sha256 'D85A63443B3F0EC387B2DFC286CD0E1F4749E4DD8AF53A25ABEF72DFE4C2EFE5'
   ```

   Exigir receipt activo correcto y smoke sin error. No reutilizar una raíz ya
   existente como instalación nueva, no copiar por encima del módulo legacy y
   no invocar un gestor PowerShell para instalar este paquete.
4. Cambiar únicamente el import Nu marcado a:

   ```nu
   use 'D:/Juegos/Minecraft/Herramientas/ModpackTools-App/control/Nushell/modpack.nu' *
   ```

   Abrir sesión nueva y verificar resolución, versión, ayuda, project list/current,
   inventario y doctor sin reparación sobre una copia de proyecto aislada. Repetir
   el ciclo de modificación/exportación del ensayo final antes de usar proyectos
   reales. Verificar que los demás scripts/modo modal siguen presentes.
5. Durante esta validación conservar la instalación legacy como origen de rollback.
   Una vez todas las comprobaciones pasen y el usuario autorice su retirada,
   eliminar sólo la instalación legacy exacta y sus imports ya sustituidos,
   conservar el respaldo verificado fuera del runtime y comprobar sesiones nuevas.
   No dejar junctions, un segundo runtime viejo activo ni aliases de compatibilidad
   permanentes. El origen de fuentes no se retira: ya es el destino permanente.

## Rollback

Antes de retirar legacy: cerrar sesiones nuevas que importaron Native, restaurar
únicamente el bloque Nu cambiado desde el respaldo y su entorno/PATH si se cambió.
Abrir sesión nueva y comprobar la versión legacy. Conservar evidencia del fallo y
los journals; no mezclar datos de dos motores ni restaurar cache/configuración de
forma parcial mientras hay operaciones activas. La imagen Native fallida se puede
retirar sólo tras comprobar su identidad y la ausencia de handles propios.

Para una actualización entre versiones nativas, usar el helper instalado:

```nu
^'D:/Juegos/Minecraft/Herramientas/ModpackTools-App/bin/modpacktools-setup.exe' rollback --root 'D:/Juegos/Minecraft/Herramientas/ModpackTools-App'
```

Volver a abrir Nu. No ejecutar cleanup hasta aceptar la versión: la retirada
explícita elimina candidatos de rollback y conserva versiones abiertas o inciertas.

## Entrega al agente de reorganización

Puede anotar fuentes y perfiles/módulos ya reorganizados como hechos verificados;
la dependencia legacy restante se libera al cambiar el import y retirar su módulo
tras la aceptación. El Native no exige bootstrap PowerShell en Documentos.
El cargador mínimo de perfiles PowerShell existente puede seguir siendo necesario
para otras herramientas; no pertenece a este trabajo y no debe borrarse por esto.
No hay que mover Nu, su configuración, plugins, modpacks, partidas o fuentes.
Estas rutas/resultados se entregan para actualizar sus documentos; este agente no
sobrescribe los planes de reorganización que otro agente mantiene.

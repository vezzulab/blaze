<p align="center">
  <img src="docs/assets/blaze.svg" alt="Blaze" width="112" height="112">
</p>

<h1 align="center">Blaze</h1>

<p align="center">
  <b>Un navegador basado en Firefox, pensado para la velocidad y la ligereza.</b><br>
  Menos bytes por página, descargas que se reanudan, privado por defecto y una interfaz que no estorba.
</p>

<p align="center">
  <a href="https://vezzulab.github.io/blaze/es/">Sitio web</a> ·
  <a href="PRIVACY.md">Privacidad</a> ·
  <a href="SECURITY.md">Seguridad</a> ·
  <a href="docs/BENCHMARKS.md">Mediciones</a> ·
  <a href="README.md">English</a>
</p>

<p align="center">
  <img alt="Licencia: MIT" src="https://img.shields.io/badge/licencia-MIT-ff6a1a?style=flat-square">
  <img alt="Plataforma: Linux x86_64" src="https://img.shields.io/badge/plataforma-Linux%20x86__64-5b5b69?style=flat-square">
  <img alt="Estado: alfa" src="https://img.shields.io/badge/estado-alfa-ff6a1a?style=flat-square">
</p>

<p align="center">
  <img src="docs/assets/blaze-ember.png" alt="Blaze, tema oscuro (Ember): barra de direcciones redondeada, el interruptor Lite y los iconos de extensiones arriba a la derecha" width="860">
</p>

---

## Qué es Blaze

Blaze es un **navegador basado en Firefox**: una distribución del motor oficial de Firefox (Gecko), ajustada para quienes tienen conexiones lentas o con datos limitados y para equipos con poca memoria. Ejecuta el Firefox real, así que toda la web moderna funciona, incluidos YouTube, Spotify y tus extensiones. Encima, Blaze agrega:

- un perfil ajustado (menos procesos, cachés pequeñas en memoria, una caché grande y persistente en disco, sin tráfico de fondo),
- un **modo Lite** de un clic,
- un **motor de descargas que se reanuda**,
- una interfaz mínima con dos temas,
- un lanzador que fija un Firefox probado y un actualizador firmado que te dice qué cambió antes de que decidas,
- la marca Blaze en lugar de la de Firefox: el nombre en menús y diálogos, y páginas de error con ilustraciones de Blaze en vez de la mascota zorro de Firefox.

Blaze **no** es un fork a nivel de código fuente y no modifica el código de Gecko. En tu máquina solo cambia el nombre de Firefox, unos textos y las ilustraciones de las páginas de error por las de Blaze (ver [Límites conocidos](#límites-conocidos)). Todo lo que no es Firefox vive en este repositorio y es lo bastante pequeño para leerse en una tarde.

> **Software en alfa.** Funciona de punta a punta (ver [Estado](#estado)), pero es joven. Lee los límites antes de depender de él.

## Por qué existe

Una página liviana en una línea rápida puede tardar minutos en una mala. Blaze ataca lo que sí se puede controlar: **cuántos bytes cuesta una página** y **qué pasa cuando la conexión se cae**.

| Abrir cnn.com, sin caché | Datos | Tiempo calculado a 256 kbps |
|---|---:|---:|
| Firefox, tal como viene | 6,8 MB | 3 min 41 s |
| Blaze | 4,7 MB (**-31 %**) | 2 min 33 s |
| Blaze con Lite | 3,8 MB (**-44 %**) | 2 min 5 s |

| Memoria, todos los procesos juntos | Firefox | Blaze | |
|---|---:|---:|---:|
| Una pestaña en blanco | 815 MB | 522 MB | **-36 %** |
| Una pestaña, Wikipedia | 878 MB | 637 MB | **-27 %** |
| Cuatro páginas abiertas | 1021 MB | 689 MB | **-33 %** |

**Léelo con honestidad.** Una sola corrida por caso, en una sola máquina (Fedora 44, Firefox 159 sin ventana, memoria como PSS). Los tiempos se calculan a partir de los bytes, no se midieron, porque el tiempo de red varía demasiado entre corridas. Unos 500 MB para una sola pestaña es el piso que encontramos con Gecko, y Blaze no puede abaratar una pestaña de video. Solo comparamos contra Firefox: **no** medimos otros navegadores y no afirmamos ser el navegador más ligero que existe. Método y límites: [docs/BENCHMARKS.md](docs/BENCHMARKS.md).

## Funciones

| | |
|---|---|
| **Modo Lite** | Un interruptor arriba a la derecha evita que se carguen imágenes, fuentes web y video. Entre 44 % y 61 % menos bytes en páginas de noticias pesadas. |
| **Descargas que se reanudan** | Motor multiconexión con estado persistente. Sobrevive a un cierre inesperado, un corte de luz o una conexión caída. Verificado con un `kill -9` a mitad de descarga. Mira el [límite](#límites-conocidos) más abajo. |
| **Extensiones** | uBlock Origin y Privacy Badger preinstalados. Funciona cualquier extensión de addons.mozilla.org. |
| **DRM** | Widevine está activado, así que Spotify y Netflix funcionan. Un ajuste lo apaga. |
| **Privado por defecto** | Sin telemetría, sin cuentas, solo HTTPS, protección estricta contra rastreo, sin notificaciones push web, sin consulta de región. Cada servidor con el que Blaze se conecta está en [PRIVACY.md](PRIVACY.md). |
| **A prueba de actualizaciones** | Blaze fija un Firefox concreto (verificado por checksum) y apaga el actualizador de Firefox, así que una actualización de origen no puede romper la interfaz. Una prueba de contrato de la interfaz vigila cada cambio de motor. |
| **Actualizaciones que decides tú** | Consulta GitHub una vez al día, muestra las notas de la versión y descarga solo si dices que sí. Las versiones se firman con ECDSA; Blaze rechaza lo que no se verifique. |
| **Dos temas** | **Ember** (oscuro) y **Ash** (claro), según tu sistema. Planos, compactos, sin desenfoques ni sombras, con el naranja como único acento. |
| **Se ve como Blaze** | Menús, diálogos y páginas de error dicen Blaze y usan ilustraciones de Blaze, no la mascota de Firefox. Ver abajo. |
| **Pequeño** | La aplicación Blaze es un AppImage de menos de 5 MB. Su agente es un binario nativo AOT (unos 10 MB de RAM mientras descarga). |

<p align="center">
  <img src="docs/assets/blaze-ash.png" alt="Blaze, tema claro (Ash): la misma barra sobre fondo claro" width="860">
</p>
<p align="center"><sub>Capturado de una ventana real de Blaze. Solo se ve la interfaz; el área de la página sale vacía por la forma en que se toman las capturas.</sub></p>

### Los errores también se ven como Blaze

<p align="center">
  <img src="docs/assets/blaze-error-ember.png" alt="Una página de error de Blaze: 'Blaze can't connect to the server', una llama con un cable desconectado y un botón naranja Try Again" width="640">
</p>

## Instalar

**AppImage (Linux x86_64)**

```bash
# 1. descarga Blaze-0.1.1-x86_64.AppImage (y SHA256SUMS y SHA256SUMS.sig para verificarlo) desde la página de Releases
chmod +x Blaze-*-x86_64.AppImage
./Blaze-*-x86_64.AppImage
```

La versión actual, **0.1.1** (software en alfa), publicada en la [página de Releases](https://github.com/vezzulab/blaze/releases) con su firma. **No uses la 0.1.0**: trae un Firefox desactualizado. ¿Prefieres compilarlo tú? Mira abajo. El primer arranque descarga Firefox 160.0.1 desde archive.mozilla.org (87 MB), lo verifica contra el SHA-256 fijado en [`engine.env`](engine.env) e instala uBlock Origin y Privacy Badger. Si lo interrumpes, se reanuda. Después Firefox baja unos 43 MB en segundo plano (Widevine 17, Safe Browsing 10, listas de bloqueo de Mozilla 8, listas de uBlock/Privacy Badger 5). Luego un arranque es casi silencioso.

**Desde el código** (necesita el SDK de .NET 10 con soporte AOT, `python3`, `curl`, `tar`, `xz`)

```bash
git clone https://github.com/vezzulab/blaze.git && cd blaze
dotnet publish src/Blaze.Agent -c Release -r linux-x64 -o dist/agent
scripts/blaze                      # ejecutar desde el repositorio
packaging/build-appimage.sh        # o construir dist/Blaze-<versión>-x86_64.AppImage
```

### Ajustes

Blaze mantiene sus interruptores deliberadamente simples: una variable de entorno o un archivo vacío.

| Qué | Cómo apagarlo | Notas |
|---|---|---|
| DRM (Widevine) | `BLAZE_DRM=0` o `touch ~/.config/blaze/no-drm` | Spotify, Netflix y similares dejan de funcionar. No se contacta a Google para el módulo. |
| Safe Browsing | `BLAZE_SAFEBROWSING=0` o `touch ~/.config/blaze/no-safebrowsing` | Más privado y ligero (unos 10 MB menos al primer arranque), pero sin avisos de phishing. |
| Consulta diaria de actualizaciones | `BLAZE_NO_UPDATE_CHECK=1` o `touch ~/.config/blaze/no-update-check` | Compruébalo a mano con `blaze --update`. |
| Botones de ventana | `BLAZE_SYSTEM_TITLEBAR=1` o `touch ~/.config/blaze/system-titlebar` | Por defecto Blaze dibuja minimizar / maximizar / cerrar como un grupo propio en el extremo derecho, aparte de la navegación. Este ajuste se los devuelve a la barra de título de tu escritorio. |
| Idioma de Firefox | `BLAZE_LOCALE=es-MX` | Están fijados `en-US`, `es-ES`, `es-MX`, `es-AR`. Por defecto sigue `$LANG`. |

## Cómo funciona

```mermaid
flowchart LR
    A[AppImage / scripts/blaze] --> B[blaze-agent<br/>AOT nativo, C#]
    A --> C[Firefox fijado<br/>verificado por checksum]
    B -- descarga reanudable --> C
    B -- socket Unix privado --> D[Extensión Lite<br/>cargada al arrancar]
    A --> E[Perfil + temas<br/>Ember / Ash]
    B -- diaria, firmada --> F[(GitHub Releases)]
    C --> G[uBlock Origin<br/>Privacy Badger]
```

- **`src/Blaze.Core`**: el motor de descargas (segmentos, `Range` + `If-Range`, fsync antes del estado, reintentos, despertar al volver la red), el actualizador (firma, checksum, reemplazo atómico) y el cliente del depurador que carga la extensión.
- **`src/Blaze.Agent`**: el punto de entrada de línea de comandos, compilado a binario nativo.
- **`scripts/blaze`**: el lanzador: instala el motor, prepara el perfil, pregunta por actualizaciones y arranca.
- **`profile/`**: `user.js` (las preferencias ajustadas) y el CSS de interfaz Ember/Ash.
- **`extension/`**: la extensión Lite (Manifest v2, declara que no recoge datos).
- **`tools/ui.py`**: maneja un Firefox real sin ventana para comprobar, y capturar, la interfaz.

## Estado

| Área | Estado |
|---|---|
| Perfil de Firefox ajustado, modo Lite, temas | Funciona |
| Motor de descargas | Funciona, cubierto por la suite de 22 pruebas. Se usa desde la línea de comandos. |
| Motor fijado + a prueba de actualizaciones + prueba de contrato | Funciona |
| Actualizador firmado con notas de versión | Funciona, probado contra un GitHub simulado (incluidas versiones falsificadas y sin firma) |
| AppImage | Se construye y arranca con y sin FUSE |
| Interruptores de DRM, Safe Browsing y actualizaciones | Funciona |
| Marca Blaze: nombre, menús, páginas de error y sus ilustraciones | Funciona. El motor arranca igual de rápido que el original (0,4 s en estar listo, ambos). Cubierto por 1 prueba unitaria y el contrato de la interfaz. |
| Tira de pestañas verticales a la derecha | Funciona con Firefox 160.0.1 (confirmado con captura). **Faltaba en la compilación 159.0** que se distribuyó en la 0.1.0. |
| Motor de descargas dentro del botón de descargas del navegador | Planeado |
| Excepciones de Lite por sitio | Planeado |
| Página de nueva pestaña y panel de descargas propios | Planeado |
| Instalación permanente de la extensión | Planeado (requiere firma en addons.mozilla.org) |
| Integración como navegador predeterminado, ARM64, otros paquetes | Planeado |

### Límites conocidos

- El motor de descargas todavía no está conectado al botón de descargas de Firefox. Hoy descarga los componentes de Blaze (el motor, las extensiones, las actualizaciones) y se maneja desde una terminal: `blaze-agent get <url> --dir <carpeta>`.
- Firefox de versión final solo acepta extensiones firmadas por Mozilla. Por eso Blaze carga su extensión Lite como complemento temporal en cada arranque, por un socket Unix privado que se cierra enseguida. Funciona, pero no es una instalación permanente.
- Fijar Firefox significa que las correcciones de seguridad de Firefox te llegan **a través de las versiones de Blaze**, no por sí solas. Seguir el ritmo de versiones de Firefox es una regla de mantenimiento del proyecto ([docs/MAINTAINING.md](docs/MAINTAINING.md)), y el aviso de actualización es la forma de recibir esas correcciones.
- **Blaze 0.1.0 distribuyó un Firefox desactualizado (159.0)** al que le faltan las correcciones de más de 100 CVE publicados desde entonces, incluidos escapes del sandbox. La versión 0.1.1 fija Firefox 160.0.1. Si instalaste la 0.1.0, actualiza y evita inicios de sesión sensibles hasta hacerlo. Es el costo de fijar el motor; ver [SECURITY.md](SECURITY.md).
- Privacy Badger abre su propia pestaña de bienvenida la primera vez que se instala.
- El cambio de marca reescribe dos de los archivos de recursos del motor en tu máquina al instalarlo, como zips limpios. Firefox no se recompila.
- Solo Linux x86_64. Los flujos de CI están escritos pero todavía no se han ejecutado.

## Desarrollo

```bash
dotnet build
(cd tests/Blaze.Tests && dotnet run -c Release)      # 22 pruebas: reanudación, cortes, actualizaciones falsas, límites de uso...
python3 tools/ui.py contract                         # necesita el motor: ejecuta scripts/blaze una vez antes
python3 tools/ui.py shot --scheme dark --out ember.png
```

Ejecuta el contrato de la interfaz antes de cambiar `FIREFOX_VERSION` en `engine.env`. Firefox renombra elementos internos entre versiones (la 159 convirtió `#urlbar-background` en `.urlbar-background`); el contrato lo detecta antes que los usuarios.

## Seguridad y privacidad

- Las versiones se firman (ECDSA P-256); la clave privada nunca está en el repositorio. Blaze rechaza actualizaciones sin firma, falsificadas o con checksum distinto.
- El motor se descarga por HTTPS y se verifica contra un checksum fijado en este repositorio.
- No se abre ningún puerto TCP. El único canal local es un socket Unix en una carpeta `0700`, que se elimina tras cargar la extensión.
- El estado de las descargas es `0600` y nunca conserva cookies cuando una descarga termina.

Detalles y la lista medida de conexiones de red: [SECURITY.md](SECURITY.md) y [PRIVACY.md](PRIVACY.md). Para reportar una vulnerabilidad, usa el reporte privado de GitHub en este repositorio.

## Licencia

[MIT](LICENSE). Blaze no está afiliado ni respaldado por Mozilla. Firefox y los logotipos de Firefox son marcas de la Mozilla Foundation. Las fuentes del sitio web están bajo la SIL Open Font License.

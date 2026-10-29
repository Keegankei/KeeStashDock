<h1 align="center">KeeStashDock</h1>

<p align="center">
  <img src="docs/assets/app-icon.png" width="112" alt="Icono de la aplicación KeeStashDock">
</p>

<p align="center">
  <strong>Un panel portátil para Windows con el que guardar fragmentos, capturas de pantalla y reutilizar texto.</strong><br>
  Captura lo que necesites, organízalo localmente y tradúcelo cuando quieras.
</p>

<p align="center">
  <a href="README.md" lang="en">English</a> ·
  <a href="README.zh-CN.md" lang="zh-Hans">简体中文</a> ·
  <a href="README.zh-TW.md" lang="zh-Hant">繁體中文</a> ·
  <a href="README.ja-JP.md" lang="ja">日本語</a> ·
  <a href="README.ko-KR.md" lang="ko">한국어</a> ·
  <a href="README.de-DE.md" lang="de">Deutsch</a> ·
  <a href="README.fr-FR.md" lang="fr">Français</a> ·
  <a href="README.es-ES.md" lang="es">Español</a> ·
  <a href="README.pt-BR.md" lang="pt-BR">Português (Brasil)</a>
</p>

<p align="center">
  <img alt="Versión 1.3.0" src="docs/assets/badges/version.svg">
  <img alt=".NET 10" src="docs/assets/badges/dotnet.svg">
  <img alt="Windows 10 y 11" src="docs/assets/badges/windows.svg">
  <img alt="Licencia Apache-2.0" src="docs/assets/badges/license.svg">
</p>

**Versión 1.3.0** · [Notas de la versión](docs/releases/1.3.0-notes.md) · [Validación y estado de publicación](docs/releases/1.3.0.md)

**Preparación de la publicación:** esta rama documenta 1.3.0. La publicación oficial sigue pendiente; la [última versión publicada](https://github.com/Keegankei/KeeStashDock/releases/latest) es actualmente **1.2.3**. El registro de validación detalla las comprobaciones pendientes.

Las nuevas vistas previas muestran la **interfaz en inglés de 1.3.0** con datos de ejemplo aislados. Las capturas y GIF anteriores siguen identificados como **1.2.3**; los temas ilustrados y las acciones combinadas de las burbujas son opcionales.

[Primeros pasos](#get-started) · [Funciones](#features) · [Atajos](#shortcuts) · [Datos y actualizaciones](#data-and-upgrades) · [Desarrollo](#development) · [Licencia](#license)

<a id="get-started"></a>
## Primeros pasos

**[Descargar la última versión publicada](https://github.com/Keegankei/KeeStashDock/releases/latest)**

La versión 1.3.0 ofrecerá `KeeStashDock-1.3.0-win-x64.zip` y `KeeStashDock-1.3.0-win-x64.zip.sha256` en **Assets**. Estos nombres describen los archivos previstos, no una descarga ya publicada.

1. Descarga el ZIP de la aplicación en **Assets**, en lugar del archivo **Source code** de GitHub, destinado al desarrollo.
2. Extrae el paquete completo en una carpeta con permisos de escritura y ejecuta `KeeStashDock.exe`. Mantén juntas todas las carpetas que lo acompañan.
3. Pulsa **Alt+Q** para mostrar el panel rápido. Escribe un fragmento y pulsa **Enter** para guardarlo; haz clic en su tarjeta para copiarlo.
4. Abre **Configuración** desde el panel o la bandeja del sistema para elegir un idioma, un tema y los componentes opcionales.

El paquete para Windows x64 incluye .NET; no es necesario instalar .NET por separado. Windows 11 es la plataforma principal, con compatibilidad con Windows 10 22H2 en la medida de lo posible. El OCR y la traducción sin conexión pueden necesitar el entorno de ejecución de Visual C++ de Microsoft, que puede instalarse mediante el instalador de componentes integrado. Consulta el [registro de aceptación de la versión 1.3.0](docs/releases/1.3.0.md) para conocer el alcance de las pruebas.

<details>
<summary>Verificar el ZIP descargado</summary>

Ejecuta lo siguiente en la carpeta de descargas y compara el resultado con el archivo `.sha256` adjunto:

```powershell
Get-FileHash .\KeeStashDock-1.3.0-win-x64.zip -Algorithm SHA256
```

Una suma de comprobación verifica la integridad del archivo; no es una firma del editor. El paquete actual no está firmado.

</details>

**Valores iniciales · 1.3.0:** inglés; **Alt+Q** abre el panel y **Alt+W** inicia una captura. El OCR y la traducción local no vienen preinstalados. Las actualizaciones conservan las preferencias guardadas.

La burbuja de selección está activada en **Solo copiar**, con **Google** como traductor. Estilo de burbuja predeterminado. Inicio automático, recopilación del portapapeles y pegado directo desactivados. La ocultación en los bordes está activada, con **2 s** para contraer y **0,1 s** para expandir. Tema del sistema, modo **Clásico**, **inspección arrastrando con el botón derecho**, colores de texto automáticos y opacidad de los controles del **90 %**. Sin imagen de fondo; superposición al **20 %** y desenfoque desactivado, con radio guardado de **4 px**. Copias de seguridad automáticas activadas cada **24 horas**.

El tamaño del texto y la opacidad de las superficies de Configuración empiezan en **100 %**. El vidrio esmerilado de Configuración está **desactivado**. Los temas incluidos son opcionales y no se aplican automáticamente.

<details>
<summary>Ajustes de una instalación nueva · 1.2.3 · interfaz en inglés</summary>

<p align="center"><img src="docs/images/default-settings.png" width="720" alt="Ajustes de 1.2.3 con inglés, Alt+Q y Alt+W como valores iniciales"></p>

</details>

<a id="features"></a>
## Funciones

### Novedades de 1.3.0

| Función | Uso |
| --- | --- |
| Búsqueda en el panel rápido | Con el panel activo, pulsa **Ctrl+F**. Busca títulos y texto guardado, incluidos resultados OCR existentes, y combina filtros de grupo, etiqueta y texto/imagen/plantilla. Los resultados se cargan en páginas de 50. **Intro** copia el resultado seleccionado y **Esc** vuelve a recopilar conservando el borrador. |
| Organización por lotes | Selecciona elementos en el área de trabajo para moverlos de grupo, añadir/quitar etiquetas, fijarlos/desfijarlos, enviarlos a la papelera o restaurarlos. **Ctrl+A solo selecciona los resultados cargados.** Cada acción se aplica en su totalidad; si hay conflictos, no cambia ningún elemento. Puedes deshacer el envío a la papelera mientras los elementos no hayan cambiado. |
| Plantillas de texto | Marca explícitamente un elemento de texto como plantilla. Usa `{{date}}`, `{{time}}` y variables como `{{name}}`; rellena los valores y revisa la vista previa antes de copiar. El original se conserva y no se ejecutan scripts ni expresiones. Los paquetes de grupos con plantillas usan el formato v3. |
| Temas completos | Previsualiza y aplica colores y fondos claros/oscuros coordinados en los paneles. Importa o exporta ambas variantes y sus imágenes en un solo ZIP de tema. Los temas son independientes del modo Clásico/Espacial; cancelar la vista previa restaura la apariencia anterior. |
| Tamaño global del texto | En **Ajustes > Apariencia > Tamaño del texto**, elige entre **90 % y 150 %** para el panel rápido, el área de trabajo, los ajustes y otros textos de la aplicación. **100 %** restaura el tamaño original. |
| Área de trabajo renovada | La navegación, la lista de elementos y el editor siguen el diseño de los ajustes. Las listas y las filas de copias de seguridad usan fondos coordinados y texto legible en modo oscuro. |
| Burbujas compactas | Acciones de copia junto al original y al resultado, con una distribución más compacta del idioma de destino, el grupo y el guardado. El estilo predeterminado y Wisteria Atelier comparten controles con paletas coordinadas. |
| Opacidad y vidrio esmerilado | Ajusta de forma independiente las superficies de Configuración entre **10 % y 100 %**. El desenfoque opcional de Configuración suaviza solo el fondo entre **1–18 px**; el texto permanece nítido. El estilo y la opacidad de las burbujas se ajustan por separado. |

Las instalaciones nuevas mantienen el inglés, el tema del sistema, el modo Clásico y ningún fondo de imagen. La actualización no aplica automáticamente un tema ilustrado ni restablece las preferencias guardadas.

**Configuración:** navega entre **General**, **Captura y traducción**, **Apariencia**, **Componentes locales**, **Copias y datos** y **Acerca de**. Las opciones dependientes aparecen cuando corresponden; al ocultarlas se conservan sus valores guardados.

En **Captura y traducción**, puedes mostrar u ocultar por separado el selector del idioma de destino y los controles para guardar en un grupo. Ambos están activados de forma predeterminada y aparecen al terminar el OCR o la traducción. Al desactivarlos, el resultado y las acciones de copia siguen disponibles.

Elige la carpeta de copias en **Copias y datos**, y las carpetas independientes del OCR y de la traducción sin conexión en **Componentes locales**. Cambiar de carpeta **no mueve los archivos existentes**; reinstala el componente si la nueva carpeta está vacía. **Acerca de** muestra la ubicación de datos activa en modo de solo lectura. Usa la exportación e importación para trasladar la biblioteca de forma segura.

Las vistas previas **1.3.0** del área de trabajo, los temas y las burbujas son renderizados fuera de pantalla de controles WPF reales con contenido ficticio. Las demás capturas y GIF de **1.2.3** muestran funciones existentes. El puntero del GIF de inspección y la versión de la vista de actualización son simulados. Las rutas de instalación y de datos están ocultas; no aparece contenido personal del escritorio ni del portapapeles. [Origen y reproducción de las imágenes](docs/images/README.md).

<p align="center"><img src="docs/images/standard-panel-workflow.gif" width="400" alt="Flujo del panel Estándar de 1.2.3: escribir, guardar, fijar y desfijar una tarjeta"></p>

*1.2.3 · Modo Estándar · escribir, guardar, fijar y desfijar · interfaz en inglés. Interacción automatizada con controles reales y datos de ejemplo.*

### 1. Capturar, buscar y organizar

<p align="center"><img src="docs/images/1.3.0/workbench-light.png" width="900" alt="Área de trabajo 1.3.0 con búsqueda, selección múltiple y editor de plantillas, con el tema claro opcional Wisteria Atelier"></p>

*1.3.0 · Área de trabajo y editor de plantillas de texto · Tema claro opcional Wisteria Atelier.*

Utiliza el panel rápido para capturas breves y el **área de trabajo** para gestionar tu colección. Abre el área de trabajo con el botón de ampliación del panel o desde la bandeja del sistema.

| Herramienta | Qué puedes hacer |
| --- | --- |
| Entrada rápida y búsqueda | Guarda con Enter; inserta saltos de línea con Alt+Enter o Shift+Enter. Ctrl+F abre la búsqueda del panel rápido. El panel y el área de trabajo permiten filtrar por grupo, etiqueta y tipo. |
| Arrastrar al panel | Suelta texto para guardarlo, o archivos y carpetas para guardar sus rutas. Haz clic en una tarjeta de ruta para abrir su destino; elige **Copiar** en el menú del clic derecho para copiar la ruta. No se importa el contenido de los archivos. |
| Grupos, etiquetas y elementos fijados | Agrupa entradas relacionadas, edita su título, texto y etiquetas, y fija el contenido que utilices con frecuencia. |
| Caducidad y papelera | Elige sin caducidad, un día, siete días o eliminación tras la lectura; las entradas caducadas y eliminadas pasan a la papelera y pueden recuperarse. De forma predeterminada, las entradas de la papelera se eliminan permanentemente después de 30 días. |
| Notas adhesivas en el escritorio | Arrastra una tarjeta de texto o de ruta fuera del panel para conservarla como nota independiente en el escritorio durante la sesión actual. Al salir de la aplicación se cierran estas notas; las entradas guardadas permanecen en tu colección. Las tarjetas de imagen siguen siendo entradas de imagen. |
| Códigos QR | Haz clic con el botón derecho en una tarjeta de texto para mostrar un código QR de hasta 1800 caracteres. |
| Herramientas de texto | Usa el menú Transformar del área de trabajo para convertir mayúsculas y minúsculas, limpiar espacios en blanco, dar formato a JSON, codificar o descodificar URL y utilizar Base64. |
| Importación y exportación de grupos | Comparte paquetes `.ksdgrp` o carga comandos predefinidos para Git, Docker, intérpretes de comandos y otras herramientas. Los paquetes de plantillas usan el formato 3 y requieren 1.3.0 o posterior; los formatos 1 y 2 se siguen importando. |
| Historial del portapapeles | Recopila automáticamente el **texto** copiado si activas esta opción; se ignoran los textos vacíos o demasiado grandes y no se añaden duplicados. Desactivado de forma predeterminada. |
| Pegado directo | Pega una entrada de texto copiada en la aplicación anterior si activas esta opción y puede confirmarse de forma segura la ventana de destino. Desactivado de forma predeterminada. |

### 2. Paneles Clásico y Espacial

En 1.3.0, Estándar pasa a llamarse **Clásico**; Espacial conserva su nombre. Las capturas antiguas de 1.2.3 mantienen las etiquetas originales.

*Las capturas del panel y el GIF de movimiento de esta sección muestran 1.2.3.*

Selecciona **Configuración > Apariencia > Modo del panel**. Ambos modos mantienen los mismos controles, tipografía, colores y distribución de tarjetas.

<table>
  <tr><th>Clásico</th><th>Espacial</th></tr>
  <tr>
    <td><img src="docs/images/classic-panel.png" width="290" alt="Panel Clásico con vista plana y sombras suaves"></td>
    <td><img src="docs/images/floating-panel.png" width="290" alt="Panel Espacial con perspectiva, tarjetas en capas y un borde lateral fino"></td>
  </tr>
  <tr><td>Una presentación plana y estable con sombras suaves.</td><td>Perspectiva, respuesta independiente de las tarjetas y profundidad visible entre las capas.</td></tr>
</table>

<p align="center"><img src="docs/images/floating-panel-motion.gif" width="400" alt="Modo Espacial: respuesta de las tarjetas a la proximidad del puntero, seguida de inspección mediante arrastre con el botón pulsado"></p>

*El puntero blanco muestra la respuesta de las tarjetas cercanas; el naranja indica la inspección mediante arrastre con el botón pulsado. Es una animación ilustrativa, no una prueba de velocidad de fotogramas.*

- **Perspectiva fija:** conserva una vista inclinada estable mientras las tarjetas cercanas responden al puntero.
- **Arrastrar para inspeccionar:** mantén pulsado el **botón derecho del ratón** sobre el cuerpo de una tarjeta o el fondo del panel y arrastra. Suelta para mantener el ángulo. Un clic derecho sin arrastrar sigue abriendo el menú de la tarjeta. La configuración también permite usar el botón central o una combinación personalizada de botón y teclas modificadoras.
- **Barra compacta:** contrae el panel a seis acciones: minimizar, expandir/contraer, bloquear, captura de pantalla, área de trabajo y Configuración. Al expandirlo, se recupera el tamaño anterior del panel.
- **Ocultar en el borde del escritorio:** activado por defecto; arrastra el panel a uno de los cuatro bordes de la pantalla y aleja el puntero. Quedará un pequeño tirador; pasa el puntero sobre él o haz clic para recuperar el panel. El bloqueo lo mantiene visible.

En ambos modos, arrastra los bordes o las esquinas del panel expandido para cambiar su tamaño; se restaura al reiniciar. La barra compacta de seis botones mantiene un tamaño fijo.

Hay temas claro, oscuro y del sistema, con ajustes de fondo personalizado, opacidad y efecto esmerilado. Las preferencias del sistema de alto contraste y reducción de animaciones ofrecen alternativas estáticas. [Vista previa oscura](docs/images/floating-panel-dark.png) · [Vista previa fija](docs/images/floating-panel-fixed.png).

La configuración también ofrece paletas de texto para el panel, un color de texto personalizado y contraste automático, además de controles independientes de estilo y opacidad para la burbuja de selección.

<details>
<summary>Ajustes de apariencia predeterminados</summary>

<p align="center"><img src="docs/images/default-appearance.png" width="720" alt="Ajustes de apariencia con modo Estándar y valores de una instalación nueva"></p>

</details>
En **Ajustes > Apariencia > Imagen de fondo**, elige **Panel rápido** o **Página de ajustes** en **Personalizar fondo**. Selecciona o borra una imagen y ajusta la superposición y el desenfoque de cada área por separado. **Usar fondo del tema** restaura la imagen predefinida del área seleccionada. La exportación del tema conserva ambos fondos por separado y pide confirmación antes de incluir imágenes personales.

#### Temas de apariencia completos

Abre **Configuración > Apariencia > Temas completos**, selecciona un tema y elige **Vista previa y aplicar**. Selecciona Claro, Oscuro o Sistema, y decide si las burbujas de selección y los resultados siguen el tema. **Cancelar** restaura la apariencia anterior. En modo Sistema, la variante de día o noche sigue el tema claro u oscuro de Windows.

| Tema | Ambiente |
| --- | --- |
| **Sunlit Gallery** | Galería tranquila en tonos crema con ventanales, luz de día y una variante nocturna iluminada por la luna; sin personajes. |
| **Twilight Study** | Estudio en tonos oliva profundos y nogal, con variantes de día y noche; sin personajes. |
| **Wisteria Atelier** | Pianista de estilo anime en un taller luminoso, con variantes clara y oscura coordinadas. |
| **Wisteria Reverie** | Pianista de estilo anime en un invernadero luminoso de tonos lavanda, con variantes coordinadas de día y noche en formato vertical y horizontal. |

Todos los temas son opcionales. Las instalaciones nuevas mantienen el tema del sistema sin imagen de fondo.

[Guía de temas y vistas previas de fondos](docs/THEMES.md).

<p align="center"><img src="docs/images/1.3.0/theme-preview-light.png" width="680" alt="Vista previa 1.3.0 de Wisteria Atelier con opciones de apariencia, controles de ejemplo y botones fijos Aplicar y Cancelar"></p>

Usa **Importar tema** para cargar un ZIP de tema o **Exportar apariencia actual** para compartir las variantes clara y oscura, las imágenes y los ajustes. La inclusión de fondos personales debe elegirse explícitamente. Los temas no contienen entradas, claves API, atajos ni instalaciones de componentes. Los archivos están en `theme/`, junto al ejecutable; las actualizaciones conservan los temas importados en `theme/custom/`.

La **opacidad de las superficies de Configuración** modifica tarjetas, navegación y campos sin atenuar el texto. El **fondo esmerilado de Configuración** desenfoca solo la imagen de fondo de los ajustes y empieza desactivado. Configuración sin imagen o con contraste alto usa superficies opacas legibles.

<p align="center"><img src="docs/images/1.3.0/settings-frosted-dark.png" width="760" alt="Configuración de apariencia 1.3.0 con tema Wisteria oscuro opcional, superficies al 60 por ciento y desenfoque del fondo de 8px"></p>

*1.3.0 · Tema Wisteria oscuro opcional, superficies de Configuración al 60 % y desenfoque de fondo de 8 px. No es la apariencia inicial ni una demostración del desenfoque nativo del escritorio.*

### 3. Capturas de pantalla y reconocimiento local de texto

*Las imágenes del flujo de capturas de esta sección muestran 1.2.3.*

<p align="center"><img src="docs/images/screenshot-entry.png" width="720" alt="Entrada de captura de pantalla guardada con su imagen original y texto reconocido editable"></p>

Pulsa **Alt+W** o un botón de captura y arrastra para seleccionar una zona. Las capturas guardadas se convierten en entradas de imagen dentro del grupo seleccionado. Abre una para verla, copiarla, guardarla como PNG o **Reconocer texto**. Puedes editar y buscar el texto reconocido conservando la imagen original.

Después de seleccionar una zona, una barra de herramientas ofrece tres acciones independientes:

| Acción | Resultado |
| --- | --- |
| Guardar captura | Guarda una entrada de imagen. Enter selecciona esta acción. |
| Extraer texto | Ejecuta el OCR local y muestra texto temporal sin guardar una entrada ni copiarlo automáticamente. |
| Extraer y traducir | Ejecuta el OCR local y envía el texto reconocido al proveedor de traducción elegido. La captura de pantalla no se sube. |

<p align="center"><img src="docs/images/screenshot-text.png" width="480" alt="Resultado temporal de OCR con controles explícitos para copiar texto en la versión 1.2.3"></p>

Arrastra de nuevo para cambiar la selección; **Esc** o un clic derecho cancela la captura. Los resultados temporales permiten copiar de forma explícita, conservar el texto original si la traducción falla y volver a intentar la traducción. El OCR es un paso de reconocimiento; no traduce automáticamente. [Flujos de captura de pantalla](docs/SCREENSHOTS.md) · [Compatibilidad y límites del OCR](docs/LOCAL_OCR.md).

Los resultados temporales de OCR y traducción ofrecen **Guardar original**, **Guardar traducción** y **Guardar ambos**, con el grupo actual seleccionado inicialmente. Guardar ambos crea una sola entrada con secciones identificadas. El guardado es explícito, no cambia el portapapeles ni guarda además la captura. Si la traducción falla, aún puedes guardar el original; si falla el guardado, el resultado se conserva para reintentarlo. El texto duplicado reutiliza la entrada existente y la coloca en el grupo elegido.

### 4. Acciones de selección y traducción

<table>
  <tr><th>1.3.0 · Estilo predeterminado</th><th>1.3.0 · Estilo Wisteria oscuro</th></tr>
  <tr>
    <td><img src="docs/images/1.3.0/selection-default.png" width="345" alt="Burbuja de selección 1.3.0 predeterminada con las acciones opcionales Copiar y Traducir"></td>
    <td><img src="docs/images/1.3.0/selection-wisteria-dark.png" width="345" alt="Burbuja de selección 1.3.0 Wisteria oscura con las acciones opcionales Copiar y Traducir"></td>
  </tr>
</table>

*Ambas vistas previas de 1.3.0 activan el modo opcional Copiar y traducir. Las instalaciones nuevas siguen usando Solo copiar.*

Activa la burbuja de selección en **Configuración > Captura y traducción** y selecciona texto en otra aplicación. **Copiar** guarda la selección en el grupo actual del panel; **Traducir** muestra una traducción sin añadir una entrada.

Elige **Copiar y traducir** para mantener ambos botones visibles. Después de copiar, puedes traducir la misma selección antes de que se cierre la burbuja. Si la traducción falla, se conserva el texto original.

Selecciona en Configuración un idioma de traducción independiente de la interfaz. **Seguir el idioma de la interfaz** sigue siendo el valor predeterminado en instalaciones nuevas; las actualizaciones conservan tu elección. Las selecciones, capturas y pruebas de traducción usan esa misma preferencia. La ventana de resultados muestra el destino efectivo y permite volver a traducir temporalmente el original conservado sin cambiar los ajustes globales. Los destinos dependen del proveedor o de los modelos instalados. Las combinaciones no admitidas generan un error sin cambiar de proveedor ni subir el texto sin conexión.

<table>
  <tr><th>1.3.0 · Estilo predeterminado</th><th>1.3.0 · Estilo Wisteria oscuro</th></tr>
  <tr>
    <td><img src="docs/images/1.3.0/translation-default.png" width="420" alt="Resultado de traducción compacto 1.3.0 predeterminado con copia, idioma de destino y guardado"></td>
    <td><img src="docs/images/1.3.0/translation-wisteria-dark.png" width="420" alt="Resultado de traducción compacto 1.3.0 Wisteria oscuro con las mismas acciones"></td>
  </tr>
</table>

*1.3.0 · Texto de ejemplo predefinido en controles de resultado reales; no se ejecutó OCR ni ningún servicio de traducción. El idioma de destino y las acciones de guardado aparecen en ambas vistas y se pueden ocultar por separado en Configuración.*

| Proveedor | Configuración y disponibilidad |
| --- | --- |
| Bing (China), Google, Lingva | No requieren clave. La disponibilidad no está garantizada. Las nuevas instalaciones usan Google de forma predeterminada; se conservan las preferencias existentes. |
| Baidu, Alibaba Cloud, DeepL | Proporciona tus propias credenciales. Las cuotas gratuitas y los costes dependen del proveedor y de tu cuenta. Alibaba Cloud requiere activar el servicio Machine Translation y disponer del permiso `alimt:TranslateGeneral`. |
| Local sin conexión | Instala el motor local opcional; utiliza un servicio de bucle local sin necesidad de cuenta ni tarifas de API. |

La traducción en la nube envía únicamente el texto solicitado al proveedor seleccionado, sin cambiar automáticamente de proveedor. El OCR no sube imágenes. Las credenciales de los proveedores se almacenan **en texto plano** en el archivo local `settings.json`; ocultar los caracteres de los campos no los cifra.

<details>
<summary>Ajustes de selección y traducción predeterminados</summary>

<p align="center"><img src="docs/images/translation-settings.png" width="720" alt="Ajustes de captura y traducción de 1.2.3 con Solo copiar, controles de resultados, Google e idioma de destino independiente"></p>

</details>

### 5. Instalar componentes opcionales desde la aplicación

<p align="center"><img src="docs/images/component-setup.png" width="720" alt="Configuración de componentes opcionales con botones de instalación y reparación de OCR y traducción sin conexión"></p>

*Los ajustes de componentes muestran el estado de una instalación nueva de 1.2.3: OCR y traducción sin conexión no están instalados. Estos botones integrados permiten instalarlos sin comandos de terminal.*

<details>
<summary>Carpetas de instalación de los componentes</summary>

<p align="center"><img src="docs/images/component-folders.png" width="720" alt="Ajustes de las carpetas de componentes en la interfaz en inglés de 1.2.3; las rutas están ocultas."></p>

*Ajustes de las carpetas de componentes en la interfaz en inglés de 1.2.3; las rutas están ocultas.*

</details>

1. Abre **Configuración > Componentes locales**.
2. Antes de usar el OCR por primera vez, elige **Instalar / reparar** en **Reconocimiento de texto (chino / inglés)**. El paquete estándar no preinstala motores ni modelos de OCR o traducción local. La instalación iniciada explícitamente descarga y verifica los archivos necesarios y comprueba el entorno de ejecución nativo.
3. Para la traducción local, elige **Instalar / reparar** en **Traducción sin conexión (chino / inglés)**. Mantén abierta la configuración durante la descarga inicial; reserva aproximadamente **1 GB de espacio libre en disco** y **2 GB de memoria disponible**.
4. Sigue el instalador de Microsoft si aparece. Si la instalación indica **Reinicio necesario**, reinicia Windows y vuelve a intentarlo. Cancelar la instalación en la aplicación no revierte una instalación del entorno de ejecución de Windows que ya haya comenzado.
5. Selecciona **Local sin conexión (Argos / LibreTranslate)** como motor de traducción. Mantén `http://127.0.0.1:5000` y utiliza **Probar traducción**. El servicio se inicia cuando hace falta; también están disponibles **Iniciar servicio** y **Detener servicio**.

La instalación inicial necesita acceso a Internet a los servidores de descarga correspondientes de NuGet, GitHub, Python, paquetes, modelos y Microsoft. Se puede reintentar una descarga fallida; se reutilizan los archivos válidos de la caché. Una vez instalados, el OCR y la traducción local funcionan sin conexión. El servicio de traducción sigue ejecutándose tras cerrar la aplicación hasta que se detiene.

**Idiomas compatibles:** el OCR admite texto impreso en inglés y chino simplificado. Los modelos de traducción de descarga opcional admiten **inglés ↔ chino simplificado**. Los **20 idiomas de la interfaz** no implican que el OCR o la traducción sin conexión admitan 20 idiomas. La ausencia de modelos nunca provoca un cambio a la traducción en la nube. [Instalación sin conexión y resolución de problemas](docs/OFFLINE_TRANSLATION.md).

### 6. Buscar actualizaciones

<p align="center"><img src="docs/images/update-check.png" width="720" alt="Diálogo de actualización con una versión más reciente simulada y la ruta de instalación oculta"></p>

*La publicación mostrada es simulada y la ruta de instalación está oculta. Esta imagen no anuncia una versión disponible.*

Utiliza **Buscar actualizaciones** en el menú de la bandeja o en **Configuración > Acerca de**. La búsqueda no descarga ni instala nada automáticamente. La descarga verifica el SHA-256 y el contenido del paquete; **Reiniciar y actualizar** requiere una confirmación independiente. El actualizador sustituye los archivos administrados de la aplicación en la ruta existente, conserva los datos del usuario y los componentes instalados, y mantiene información de recuperación si el inicio falla.

El paquete organizado contiene tres archivos en la raíz: `KeeStashDock.exe`, `StartHere.md` y `update-manifest.json`. Las guías están en `Docs` y los avisos legales en `Licenses`; los idiomas y los componentes opcionales conservan sus carpetas específicas. Mantén unido el paquete completo. [Actualizaciones y recuperación](docs/UPDATING.md).

<a id="shortcuts"></a>
## Atajos

Si un atajo del panel o de captura no puede registrarse al inicio, una notificación lo identifica y ofrece acceso a Configuración, donde su estado sigue visible. Los botones y la bandeja continúan funcionando. Si falla el cambio de atajo, se conserva la asignación anterior que funcionaba.

| Acción | Atajo o gesto predeterminado |
| --- | --- |
| Mostrar/ocultar el panel rápido | **Alt+Q**; configurable |
| Buscar en el panel rápido | **Ctrl+F** mientras el panel está activo; **Esc** vuelve a la captura |
| Seleccionar resultados del área de trabajo | **Ctrl+A** con el foco en la lista; selecciona solo las filas cargadas |
| Guardar el texto escrito | **Enter** |
| Insertar un salto de línea | **Alt+Enter** o **Shift+Enter** |
| Captura de pantalla | **Alt+W**; configurable por separado |
| Guardar la zona de captura seleccionada | **Enter** en la barra de captura |
| Cancelar la captura | **Esc** o clic derecho |
| Inspeccionar el panel Espacial | Mantener pulsado el **botón derecho del ratón** y arrastrar; configurable |
| Copiar/traducir desde la burbuja de selección | **Ctrl+Alt+Shift+C** / **Ctrl+Alt+Shift+T**, solo mientras la burbuja esté visible y los atajos estén disponibles |

<a id="data-and-upgrades"></a>
## Datos, copias de seguridad y actualizaciones

**Compatibilidad de la base de datos en 1.3.0:** la aplicación actualiza la biblioteca al **esquema 5**. Antes de modificar una base anterior, crea y verifica una copia de seguridad coherente que incluye los datos WAL confirmados. Si falla la copia, se detiene la migración. **1.2.3 y las versiones anteriores no pueden abrir el esquema 5.** Restaurar archivos antiguos de la aplicación no revierte el formato de la base de datos. Conserva la carpeta de datos activa y la copia previa a la actualización. Para volver atrás, restaura una copia independiente compatible sin sobrescribir los datos más recientes.

Los temas están en `theme/`, junto al ejecutable: `builtin/` contiene recursos gestionados por la aplicación y `custom/` temas importados por el usuario. Las actualizaciones conservan `theme/custom/`. **Las copias de seguridad de la base de datos no incluyen temas.** Usa la exportación de temas en los ajustes para trasladar o guardar por separado la apariencia clara/oscura completa y sus imágenes.

Configuración muestra la última copia correcta, la última comprobación, la próxima comprobación, el estado, la ubicación y la cantidad de instantáneas. Permite **Crear copia ahora**, abrir la carpeta y restaurar desde la lista mediante el proceso protegido. Intervalos: **15 / 30 / 60 minutos**, 6 / 12 / 24 / 48 horas o 7 días; el valor predeterminado sigue siendo **24 horas**. Se conservan los intervalos personalizados y la retención existentes. Siete instantáneas cada 15 minutos abarcan solo **90 minutos** entre la más antigua y la más reciente; elige la retención teniendo en cuenta el espacio estimado. Cada copia programada crea una instantánea completa aunque la base no haya cambiado; los intervalos cortos reemplazan antes el historial conservado. Se respalda la base de entradas y sus imágenes, **no todos los ajustes, componentes o fondos**. La limpieza automática no elimina exportaciones manuales ni copias de recuperación.

<details>
<summary>Copias de seguridad · cada 24 horas por defecto</summary>

<p align="center"><img src="docs/images/backup-settings.png" width="720" alt="Ajustes de datos con copias automáticas cada 24 horas y rutas ocultas"></p>

<p align="center"><img src="docs/images/backup-status.png" width="720" alt="Estado de las copias de seguridad, instantáneas y acciones manuales con datos de ejemplo aislados. Las rutas están ocultas; el intervalo predeterminado sigue siendo de 24 horas."></p>

*Estado de las copias de seguridad, instantáneas y acciones manuales con datos de ejemplo aislados. Las rutas están ocultas; el intervalo predeterminado sigue siendo de 24 horas.*

</details>

<details>
<summary>Acerca de · versión de la aplicación y ubicación de los datos</summary>

<p align="center"><img src="docs/images/about-settings.png" width="720" alt="Página Acerca de de 1.2.3 con información de la aplicación y la ruta de datos oculta"></p>

</details>

Las entradas, imágenes, textos reconocidos y copias de seguridad se almacenan localmente. Los datos portátiles suelen estar en `Data`, junto al ejecutable. Si esa carpeta no permite escritura, la aplicación utiliza `%AppData%\KeeStashDock` e informa de la ubicación alternativa en la configuración. Los componentes instalados desde la aplicación suelen utilizar `%LOCALAPPDATA%\KeeStashDock`, separados de tu colección.

Usa **Configuración > Copias y datos** para exportar o importar una copia de seguridad y configurar copias programadas. La primera actualización desde **1.2.1 es manual**:

1. Exporta una copia de seguridad y sal de la aplicación desde el menú de la bandeja.
2. Extrae el nuevo ZIP en una carpeta aparte. Con la aplicación cerrada, copia **toda la carpeta de datos activa** a la carpeta `Data` de la nueva instalación. Si la configuración indica una ubicación alternativa en `%AppData%`, utiliza esa carpeta como origen.
3. Conserva la instalación original y la copia de seguridad. Inicia la nueva versión y comprueba tus entradas antes de eliminar las copias antiguas. Guarda una copia de seguridad anterior a la actualización para poder volver atrás; los esquemas antiguos de la base de datos no son intercambiables.
4. Si **Iniciar con Windows** estaba activado y has cambiado de carpeta, desactiva y vuelve a activar la opción en la configuración de la nueva aplicación. Así se registra la nueva ruta; de lo contrario, Windows podría iniciar la copia anterior al volver a iniciar sesión.

Desde **1.2.2**, usa **Buscar actualizaciones** o sigue los pasos manuales anteriores. Las **versiones candidatas 1.3.0 (r1–r5)** requieren sustitución manual porque comparten el número de la versión final. Conserva la carpeta de datos activa completa y las copias de seguridad; cierra el proceso anterior antes de iniciar la nueva copia.

No adjuntes tu carpeta personal `Data`, credenciales, copias de seguridad privadas ni registros a una incidencia. Consulta [asistencia e informes sin datos sensibles](SUPPORT.md), [recuperación tras una actualización](docs/UPDATING.md) y la [política de seguridad](SECURITY.md).

<a id="development"></a>
## Desarrollo y documentación

Compila en Windows con el SDK seleccionado por [`global.json`](global.json) (10.0.401 o una revisión compatible 10.0.4xx):

```powershell
dotnet restore KeeStashDock.sln
dotnet build KeeStashDock.sln --configuration Release --no-restore --disable-build-servers
dotnet test --solution KeeStashDock.sln --configuration Release --no-build
```

El inglés es la referencia de la documentación del repositorio. Los README traducidos comparten la misma estructura de funciones; las traducciones de la interfaz están en `Locales/*.json`. Las correcciones de traducción son bienvenidas siguiendo la [guía de contribución](CONTRIBUTING.md).

| Documentación | Contenido |
| --- | --- |
| [Registro de cambios](CHANGELOG.md) | Historial de versiones |
| [Arquitectura](docs/ARCHITECTURE.md) | Proyectos, responsabilidades y almacenamiento |
| [Localización](docs/LOCALIZATION.md) | Convenciones de traducción de la interfaz y de los README |
| [Proceso de publicación](docs/RELEASING.md) | Compilación, empaquetado y comprobaciones de aceptación |
| [Asistencia](SUPPORT.md) · [Código de conducta](CODE_OF_CONDUCT.md) | Preguntas, incidencias y participación en la comunidad |
| [Avisos de terceros](THIRD_PARTY_NOTICES.md) | Licencias de dependencias, entornos de ejecución y modelos |

<a id="license"></a>
## Licencia

Copyright © 2026 KeeganKei. Bajo licencia [Apache-2.0](LICENSE); consulta [NOTICE](NOTICE) para las atribuciones. Las dependencias, los entornos de ejecución opcionales y los modelos conservan sus propias licencias, recogidas en [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). La licencia del proyecto no concede derechos sobre marcas comerciales.

El icono y las ilustraciones de los temas incluidos son recursos generados por IA. Atribución y licencias: [icono](docs/assets/README.md), [Sunlit Gallery](theme/builtin/sunlit-gallery/SOURCE.md), [Twilight Study](theme/builtin/twilight-study/SOURCE.md), [Wisteria Atelier](theme/builtin/wisteria-atelier/SOURCE.md), [Wisteria Reverie](theme/builtin/wisteria-reverie/SOURCE.md).

# ADR-0012: adjuntos en almacenamiento de objetos con URL prefirmada y metadatos en la base

## Metadatos

| Campo | Valor |
|---|---|
| ID | ADR-0012 |
| Fecha | 2026-11-08 |
| Estado | **Accepted** |
| Participantes | Jhonny Estiven Benítez Caro |
| Curso | Ingeniería del Software II — UCO, semestre 2-2026 |
| Profesor evaluador | Wider Farid Sánchez Garzón |
| Building blocks | ABB de Gestión Documental y de Archivos |

## Contexto

RF-08 y DRV-004 permiten adjuntar archivos a un Kudo. El enunciado es inusualmente explícito: pide diseñar cuatro estrategias por separado —**almacenamiento, seguridad, recuperación y trazabilidad**— así que este ADR las responde una por una.

Un adjunto de un Kudo es contenido de personas: la foto de un reconocimiento, un diploma, una captura. No puede quedar accesible a cualquiera que adivine una URL, ni debe perderse si se recrea el servidor.

El presupuesto es cero (RT-07): no hay un servicio gestionado de almacenamiento en la nube de pago, y el despliegue es una sola máquina (ADR-0016).

## Decisión

Guardar los archivos **fuera de la base de datos**, en un **almacenamiento de objetos compatible con la API de S3** autoalojado en la misma máquina, y guardar en PostgreSQL únicamente los **metadatos**.

### Estrategia de almacenamiento

- Un servicio de objetos compatible con S3, en un contenedor, con su volumen persistente. Compatible con S3 **por decisión explícita**: si el día de mañana hay presupuesto, migrar a un servicio gestionado en la nube es cambiar tres variables de configuración, no reescribir el adaptador (QA-MANT-01).
- El archivo **nunca** se guarda en la base de datos. Un archivo en una columna binaria infla los respaldos, satura la memoria caché del motor con datos que nadie consulta y hace que cada lectura del Kudo arrastre megabytes que la pantalla no necesita.
- El nombre del objeto es un identificador generado, **no el nombre que puso el usuario**. Un nombre de archivo controlado por el usuario es un vector de recorrido de rutas y de colisiones.
- Validación en la carga: lista blanca de tipos permitidos (imágenes y PDF), tamaño máximo por archivo y cantidad máxima por Kudo. El tipo se determina por el **contenido real del archivo**, no por la extensión ni por lo que declare el cliente: las dos cosas las controla quien sube el archivo.

### Estrategia de seguridad

- **El contenedor de objetos es privado.** No hay acceso anónimo de lectura. Esta es la decisión central: un contenedor público haría irrelevante todo lo demás.
- La descarga se hace con una **URL prefirmada de vida corta**, generada por la API **después** de comprobar que quien pide el archivo tiene derecho a ver ese Kudo. La autorización ocurre en la plataforma, que es la que conoce la jerarquía (ADR-0004); el almacenamiento solo honra una firma.
- La URL vence en minutos. Compartirla fuera no da acceso permanente (QA-SEG-02).
- La subida también usa URL prefirmada, así que el archivo viaja del navegador al almacenamiento sin pasar por la API. Eso evita que la API sea el cuello de botella en una red móvil.
- Las credenciales del almacenamiento son secretos (ADR-0018). El cliente nunca las ve: solo recibe URL firmadas.

### Estrategia de recuperación

- El volumen del almacenamiento se respalda junto con la base, en el mismo procedimiento y con la misma periodicidad, porque un respaldo de la base sin los archivos deja Kudos que apuntan a adjuntos que ya no existen.
- Se guarda el **hash del contenido** de cada archivo, así que al restaurar se puede verificar que el archivo recuperado es el mismo que se subió.
- El borrado de un Kudo **no borra el objeto de inmediato**: lo marca. Así un borrado accidental es reversible, y la limpieza definitiva es un proceso aparte.

### Estrategia de trazabilidad

- Los metadatos en la base son: identificador del objeto, nombre original declarado por el usuario (para mostrarlo, nunca para construir rutas), tipo detectado, tamaño, hash del contenido, quién lo subió, cuándo, y a qué Kudo pertenece.
- Cada **carga y cada descarga** generan un evento de auditoría (ADR-0013). Con eso se responde la pregunta que importa: quién vio qué archivo y cuándo.
- El hash permite además detectar duplicados y comprobar integridad.

## Alternativas consideradas

| Criterio | **Objetos S3 privado + URL prefirmada (elegida)** | A. Columna binaria en PostgreSQL | B. Carpeta del sistema de archivos servida por la API | C. Servicio gestionado en la nube | D. Contenedor de objetos público |
|---|---|---|---|---|---|
| Autorización efectiva (QA-SEG-02) | Cumple — firma de vida corta tras comprobar permisos | Cumple | Cumple | Cumple | **No cumple** — cualquiera con la URL entra |
| No infla los respaldos de la base | Cumple | **No cumple** | Cumple | Cumple | Cumple |
| La API no transporta los archivos | Cumple — sube y baja directo | No cumple | No cumple | Cumple | Cumple |
| Costo (RT-07) | Cumple — autoalojado | Cumple | Cumple | **No cumple** | Cumple |
| Portabilidad a la nube (QA-MANT-01) | Cumple — misma API | No cumple | No cumple | Cumple | Parcial |
| Trazabilidad de descargas | Cumple — la API firma, así que ve cada petición | Cumple | Cumple | Cumple | **No cumple** |
| Esfuerzo (RN-02) | Cumple — un contenedor y un adaptador | Cumple | Cumple | Parcial | Cumple |

- **A. Columna binaria en la base** — descartada: es lo más simple de programar y lo peor de operar. Los respaldos crecen con cada foto, la memoria caché del motor se llena de datos binarios y cada consulta del Kudo arriesga arrastrar el archivo.
- **B. Carpeta del sistema de archivos servida por la API** — descartada: funciona, pero obliga a que cada byte de cada descarga atraviese la aplicación, y ata los archivos a esa máquina concreta, lo que bloquea cualquier escalado horizontal (QA-ELAS-01).
- **C. Servicio gestionado en la nube** — descartada **hoy** por RT-07, no por mérito técnico: es la opción correcta en producción. La decisión de usar una API compatible con S3 es precisamente lo que deja esa puerta abierta.
- **D. Contenedor público** — descartada sin matices: es el atajo que convierte los adjuntos en información pública y elimina toda posibilidad de trazabilidad de las descargas. Es el error que este ADR existe para no cometer.

## Consecuencias

**Positivas**

- Los adjuntos no están accesibles sin autorización, y cada acceso queda trazado.
- La base de datos se mantiene liviana, así que los respaldos y el rendimiento de lectura no se degradan por los archivos.
- La API no transporta archivos: menos carga y menos latencia en red móvil.
- Migrar a un almacenamiento gestionado es un cambio de configuración.

**Negativas**

- Son **dos sistemas que respaldar de forma coordinada**. Un respaldo de uno sin el otro produce referencias roscas, y hay que probar la restauración de los dos juntos.
- Las URL prefirmadas vencen, lo que significa que una pestaña abierta mucho tiempo dará un error al descargar. Es el comportamiento correcto y hay que explicarlo en la interfaz.
- Un contenedor más en una máquina gratuita, con su consumo de memoria y su superficie que escanear con Trivy.
- Detectar el tipo por el contenido real exige leer los primeros bytes del archivo, que es un paso más en la carga.

## Tácticas de Bass aplicadas

- **Limitar el acceso** — contenedor privado y firma de vida corta.
- **Autorizar actores** — la API decide antes de firmar.
- **Validar la entrada** — lista blanca de tipos por contenido, tamaño y cantidad.
- **Auditar** — carga y descarga generan evento.
- **Mantener la integridad de los datos** — hash del contenido.
- **Reducir acoplamiento** — API compatible con S3 detrás de un puerto.

## Escenarios y requisitos relacionados

- `QA-SEG-02` — el enlace vencido no da acceso una semana después.
- `QA-OBS-01` — se puede saber quién descargó qué.
- `QA-MANT-01` — migración a un almacenamiento gestionado sin tocar el dominio.
- `RF-08`, `DRV-004`, `DRV-005`, `RT-07`.

## ADRs relacionados

- **ADR-0004** — quién tiene derecho a ver un Kudo y, por tanto, sus adjuntos.
- **ADR-0013** — la auditoría de cargas y descargas.
- **ADR-0016** — la máquina donde vive el contenedor y su volumen.
- **ADR-0018** — dónde viven las credenciales del almacenamiento.

## Notas de implementación

- La prueba de seguridad que hay que escribir no es que la descarga funcione, sino que **falle**: pedir el objeto directamente al almacenamiento sin firma debe ser rechazado, y una URL expirada también. Sin esas dos pruebas, la estrategia de seguridad es una afirmación.
- Las pruebas de integración corren contra el almacenamiento real en un contenedor desechable, no contra un doble: la firma y su expiración son justamente lo que hay que verificar.
- El procedimiento de respaldo y restauración se prueba de punta a punta al menos una vez antes de la entrega, y esa prueba es una de las evidencias.
- El nombre original del usuario se escapa al mostrarlo en la interfaz: un nombre de archivo puede contener etiquetas.

## Referencias

- Amazon Web Services. *Amazon S3 API Reference — Presigned URLs.*
- OWASP. *File Upload Cheat Sheet* y *Top 10 — A01 Broken Access Control.*
- Microsoft. *Valet Key pattern.* Cloud Design Patterns.

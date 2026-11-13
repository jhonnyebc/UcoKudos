# Matriz de cumplimiento de The Twelve-Factor App

El enunciado pide que **cada factor sea analizado, evaluado y justificado**. Esta matriz lo hace factor por factor.

La evaluación usa tres estados, y conviene ser honesto con ellos: un documento donde los doce factores salen «cumple» suele significar que no se evaluó nada.

| Estado | Significado |
|---|---|
| **Cumple** | La práctica está aplicada y se puede mostrar |
| **Cumple parcialmente** | Aplicada con una limitación conocida que se declara |
| **No aplica** | El factor supone un contexto que esta plataforma no tiene |

## Resumen

| # | Factor | Estado |
|---|---|---|
| I | Código base | Cumple |
| II | Dependencias | Cumple |
| III | Configuración | Cumple |
| IV | Servicios de respaldo | Cumple |
| V | Construir, publicar, ejecutar | Cumple |
| VI | Procesos | Cumple parcialmente |
| VII | Asignación de puertos | Cumple |
| VIII | Concurrencia | Cumple parcialmente |
| IX | Desechabilidad | Cumple |
| X | Paridad de entornos | Cumple |
| XI | Historiales | Cumple |
| XII | Procesos de administración | Cumple |

Dos factores salen parciales, y son justamente los dos que un despliegue en una sola máquina no puede cumplir del todo. Decirlo es más defendible que forzar un «cumple».

---

## I. Código base — un código base con control de versiones, muchos despliegues

**Estado: Cumple**

Dos repositorios, uno de back-end y otro de front-end, como exige RT-02. Cada uno con su historial en Git, su rama `main` y su rama `develop`, ambas protegidas con el pipeline como condición de entrada.

**Justificación de los dos repositorios:** el factor habla de *una aplicación, un código base*. El back-end y el cliente web son dos aplicaciones desplegables, no una, así que dos repositorios es lo correcto, no una violación. Lo que el factor prohíbe es que una misma aplicación viva repartida en varios repositorios.

El mismo código base produce el despliegue local y el de producción. Lo que los distingue es la configuración (factor III), nunca el código.

**Evidencia:** historial de Git y el workflow en `.github/workflows/ci.yml`.

---

## II. Dependencias — declararlas y aislarlas explícitamente

**Estado: Cumple**

Las dependencias del back-end se declaran en los archivos de proyecto con **versión exacta**, nunca con un rango. El pipeline restaura desde esa declaración y no desde nada instalado en la máquina.

En el cliente web, las dependencias se fijan igual y se instalan desde el archivo de bloqueo, **sin ejecutar los scripts de instalación de los paquetes**: es una decisión de seguridad de cadena de suministro (ADR-0017), y ninguna dependencia los necesita.

**Aislamiento:** la imagen de contenedor es la frontera. No hereda nada del sistema anfitrión, y la variante *chiseled* no trae ni consola ni gestor de paquetes, así que no hay forma de instalar algo al margen de la declaración.

**Lo que hubo que corregir en el camino:** la etiqueta de la imagen base del SDK se había puesto como la versión del SDK instalado en el equipo de desarrollo, y esa etiqueta no existe publicada. Es exactamente el tipo de dependencia implícita que este factor evita: se detectó porque la construcción de la imagen falla, no porque funcionara por casualidad.

**Evidencia:** los `.csproj`, el `Dockerfile` y el paso de restauración del pipeline.

---

## III. Configuración — en el entorno, no en el código

**Estado: Cumple**

Es el factor que gobierna ADR-0018, y aquí se aplica con una regla estricta: **lo secreto no tiene valor por omisión**. Si falta, la aplicación no arranca y el mensaje nombra la variable que falta.

| Qué | Dónde vive |
|---|---|
| Configuración no secreta | Archivo de configuración versionado, con valores por omisión razonables |
| Secretos en desarrollo | Almacén de secretos del entorno de desarrollo, **fuera** del árbol del proyecto |
| Secretos en el pipeline | Secretos del repositorio, inyectados solo en los trabajos que los necesitan |
| Secretos en producción | Archivo de entorno en la máquina, permisos restringidos al usuario del servicio, **no versionado** |

**Por qué un secreto no lleva valor por omisión:** un valor por omisión para un secreto es una contraseña en el repositorio con otro nombre, y además oculta el error de configuración hasta que algo falla en producción. Negarse a arrancar es molesto la primera vez y correcto siempre.

La contraparte obligatoria es `.env.example`, versionado, que lista todas las variables con valores de muestra y marca cuáles son secretas. Sin él, nadie podría levantar el entorno.

**Verificación:** hay dos escaneos de secretos, uno en un gancho antes del envío y otro en el pipeline (ADR-0017). El que importa es el local: detectar un secreto en el pipeline significa que ya está en el repositorio remoto, y borrarlo del archivo no lo saca del historial.

**Limitación declarada:** no hay auditoría de quién accedió a cada secreto, que es lo que un gestor dedicado aportaría. Se descartó por costo (RT-07) y el camino está abierto como adaptador de configuración.

**Evidencia:** `.env.example`, el trabajo de escaneo de secretos y el gancho `.githooks/pre-push`.

---

## IV. Servicios de respaldo — tratarlos como recursos conectables

**Estado: Cumple**

Los siete servicios de respaldo de la plataforma se consumen **a través de un puerto**, con su dirección en configuración. Ninguno está incrustado en el código.

| Servicio de respaldo | Puerto que lo abstrae | Qué cambia al sustituirlo |
|---|---|---|
| PostgreSQL | Repositorios | La cadena de conexión |
| Almacenamiento de objetos | `IAlmacenObjetos` | Tres variables; la API es compatible con S3 |
| Proveedor de correo | `IEnviadorCorreo` sobre SMTP | Tres variables |
| WhatsApp Cloud API | `IWhatsAppGateway` | Un adaptador nuevo |
| Servicio de push | `IEnviadorPush` | Nada: VAPID es un estándar |
| Proveedor de identidad | Validación de token | Emisor y audiencia |
| Recolector de observabilidad | Exportador OTLP | Una variable |

Esta es la forma concreta de QA-MANT-01, y es lo que permite el reparto de ADR-0008: el mismo puerto de correo tiene tres adaptadores, uno simulado en memoria para las pruebas, uno de captura en desarrollo y el proveedor real en producción. Que los tres sean intercambiables es la prueba de que el servicio es un recurso conectable y no una dependencia incrustada.

**Evidencia:** los puertos en `UcoKudos.Application/*/Salida/Puertos` y sus adaptadores en `UcoKudos.Infrastructure`.

---

## V. Construir, publicar, ejecutar — separar las tres etapas estrictamente

**Estado: Cumple**

Las tres etapas están separadas y la frontera se puede señalar en el pipeline:

1. **Construir.** El pipeline compila y produce una imagen etiquetada `versión-sha`.
2. **Publicar.** Esa imagen, **la misma que se escaneó**, se sube al registro. No se reconstruye: reconstruir después de escanear invalidaría el escaneo, y la imagen que llegaría al servidor no sería la que pasó la puerta.
3. **Ejecutar.** En la máquina se descarga esa imagen y se ejecuta con el archivo de entorno de producción.

**La imagen es inmutable y no lleva configuración.** Nunca se etiqueta como `latest`: se despliega una versión concreta, identificable por el commit que la produjo. Eso es lo que hace posible volver a una versión anterior sin reconstruir nada.

**No se puede modificar el código en ejecución.** La imagen *chiseled* no tiene consola, así que entrar al contenedor a «arreglar algo» no es una opción ni por accidente.

**Evidencia:** los trabajos `imagenes` y `publicar` del pipeline, y las etiquetas OCI del `Dockerfile`.

---

## VI. Procesos — ejecutar la aplicación como procesos sin estado que no comparten nada

**Estado: Cumple parcialmente**

La parte que **cumple**, y es la más importante: **no hay estado de sesión en el proceso**. La autenticación es por token (ADR-0004), el usuario interno se resuelve en cada petición, y nada queda en memoria entre peticiones. Dos instancias atenderían peticiones alternas sin que nadie lo note. Todo lo que debe persistir está en PostgreSQL o en el almacenamiento de objetos.

La parte **parcial**, declarada: hay dos estados en memoria del proceso.

| Estado en memoria | Consecuencia | Mitigación |
|---|---|---|
| **La caché de lectura** (ADR-0010) | Con varias instancias, cada una tendría su propia copia y la invalidación por evento no llegaría a las demás | Está detrás de un puerto: el paso a una caché distribuida es un cambio de adaptador |
| **Las conexiones SSE abiertas** (ADR-0009) | Cada instancia conoce solo sus conexiones, así que la política de «si está conectado, no enviar Web Push» no se podría coordinar | Anotado como punto de revisión al escalar. Hoy hay una sola instancia |

Ninguno de los dos es estado **de negocio**: perderlos al reiniciar no pierde datos, solo obliga a recalcular la caché y a que el navegador reconecte. Pero sí impiden decir que el factor se cumple por completo, y por eso figura como parcial.

---

## VII. Asignación de puertos — exportar los servicios mediante asignación de puertos

**Estado: Cumple**

La aplicación es **autocontenida**: trae su propio servidor HTTP y expone el servicio en un puerto. No se despliega dentro de un servidor de aplicaciones ni necesita que alguien la «instale» en un contenedor web.

El puerto se configura por variable de entorno: 8082 en local y 8080 dentro de la imagen, donde el proxy inverso lo reenvía. La aplicación no sabe que hay un proxy delante, y el proxy puede cambiarse sin tocarla.

**Consecuencia práctica:** la API es un servicio de respaldo para cualquier otro proceso que la consuma. El cliente web la consume por HTTP igual que lo haría otro sistema.

**Evidencia:** el `EXPOSE` del `Dockerfile` y el `launchSettings.json`.

---

## VIII. Concurrencia — escalar mediante el modelo de procesos

**Estado: Cumple parcialmente**

Lo que **cumple**: la plataforma distingue dos tipos de trabajo y los separa, que es el reparto que este factor pide.

| Tipo de proceso | Qué hace | Por qué separado |
|---|---|---|
| **Web** | Atiende peticiones HTTP | Trabajo corto y síncrono |
| **Trabajador** | Recorre la bandeja y despacha los avisos | Trabajo largo que no puede hacer esperar a nadie |

Y el trabajador está **preparado para escalar horizontalmente**: toma eventos con `FOR UPDATE SKIP LOCKED`, así que varias instancias nunca procesan el mismo evento y no se duplican envíos. Eso es exactamente QA-ELAS-01, y es verificable con una prueba.

Lo **parcial**, declarado en dos puntos:

1. **Los dos tipos de proceso corren en el mismo contenedor**, no como dos unidades escalables por separado. Es coherente con el monolito modular (ADR-0001) y no hay driver que pida escalarlos de forma distinta, pero significa que no se puede añadir capacidad de trabajador sin añadir capacidad web.
2. **No hay escalado horizontal real**: una sola máquina y una sola instancia (ADR-0016). El diseño no lo impide, pero el despliegue no lo ejerce, y el factor habla del modelo de procesos en ejecución, no de la intención.

Lo honesto es decir que el diseño respeta el factor y el despliegue de la línea base no lo aprovecha.

---

## IX. Desechabilidad — maximizar la robustez con arranque rápido y cierre elegante

**Estado: Cumple**

**Arranque rápido.** La aplicación arranca en segundos, y es la razón de fondo para haber descartado una plataforma gratuita que suspende el servicio por inactividad: esperar de 30 a 60 segundos a que despierte habría convertido el arranque en un problema del usuario (ADR-0016).

**Cierre elegante.** Es donde este factor se cruza con la funcionalidad obligatoria. El proceso de notificaciones toma **un evento a la vez** dentro de una transacción, y lo marca procesado **dentro de esa misma transacción**. Si el proceso muere a mitad de camino:

- La transacción no confirmada se deshace y el bloqueo se libera.
- El evento queda pendiente, no a medio procesar.
- Al reiniciar, se vuelve a tomar.

Eso es lo que hace que QA-CONF-01 se cumpla: **ninguna notificación se pierde aunque el proceso muera en el peor momento posible**, y es demostrable con una prueba que mate el proceso entre el commit y el envío.

**Robustez ante una muerte súbita.** No hay trabajo acumulado en memoria que se pierda: todo lo pendiente está en la tabla de eventos. Un `docker kill` no es un escenario distinto de un apagado ordenado, solo más rápido.

**Evidencia:** la transacción del proceso de notificaciones y las pruebas de idempotencia y de recuperación.

---

## X. Paridad de entornos — mantener desarrollo, preproducción y producción lo más parecidos posible

**Estado: Cumple**

| Pieza | Desarrollo | Producción | ¿Paridad? |
|---|---|---|---|
| Base de datos | PostgreSQL en contenedor | PostgreSQL en contenedor | Sí, misma versión mayor |
| Objetos | Compatible con S3 en contenedor | Compatible con S3 en contenedor | Sí, misma API |
| Correo | Contenedor de captura | Proveedor por SMTP | Mismo protocolo, destino distinto |
| WhatsApp | Adaptador simulado | Meta Cloud API | Mismo puerto |
| Observabilidad | Recolector en contenedor | Recolector en contenedor | Sí |
| Topología | `docker compose` | `docker compose` | **Sí, el mismo mecanismo** |

**Las pruebas son el punto fuerte de este factor aquí**, y por dos razones:

1. Las de integración corren contra **PostgreSQL y almacenamiento reales** en contenedores desechables, no contra dobles en memoria. Una base en memoria no reproduce índices únicos parciales, ni el comportamiento de `SKIP LOCKED`, ni el comportamiento de un servidor de objetos real: justo lo que hay que verificar.
2. **Las pruebas se ejecutan en un contenedor Linux**, y el pipeline invoca **el mismo script** que el entorno local. Así un resultado local y uno del pipeline son comparables, y el umbral de cobertura está definido en un único lugar.

**Brecha de paridad conocida:** el desarrollo es Windows y la producción Linux. El contenedor de pruebas la cierra para lo que importa, que es ejecutar el código sobre el mismo sistema en el que correrá.

**Paridad temporal:** se despliega en cada integración a `main`, no una vez al final.

---

## XI. Historiales — tratarlos como flujos de eventos

**Estado: Cumple**

La aplicación **no escribe archivos de registro ni los rota**. Escribe a la salida estándar, en formato JSON estructurado, y quien la ejecuta decide a dónde va eso.

| Qué | Dónde |
|---|---|
| Logs de la aplicación | A la salida estándar, recogidos por el contenedor y exportados por OTLP |
| Trazas y métricas | Exportadas por OTLP al recolector |
| Auditoría | **No es un log.** Va a una tabla de solo inserción, en PostgreSQL |

**Cada línea lleva el identificador de traza**, nunca texto libre concatenado: un log que hay que leer con expresiones regulares no se puede consultar. Y el identificador es lo que une los logs de la petición con los del proceso en segundo plano que publica los avisos segundos después, que es lo que hace posible QA-OBS-01 (ADR-0014).

**La distinción que este factor obliga a hacer explícita:** los logs son efímeros por diseño, y el recolector elegido no los persiste al reiniciarse. Eso es correcto para diagnosticar y sería inaceptable para rendir cuentas, que es la razón por la que la auditoría de RF-17 es un ABB aparte con una tabla propia e inmutable (ADR-0013). «Está en los logs» no es una auditoría.

**No se registran datos personales.** El texto de un Kudo, un correo o un teléfono no van a los logs: se diagnostica con identificadores.

---

## XII. Procesos de administración — ejecutar las tareas de administración como procesos puntuales

**Estado: Cumple**

Las tareas de administración se ejecutan **con la misma imagen y la misma configuración** que la aplicación, como procesos puntuales, no con scripts sueltos conectados a la base.

| Tarea | Cómo se ejecuta |
|---|---|
| Migraciones del esquema | Como paso del despliegue, con la misma versión del código, **antes** de que la nueva versión reciba tráfico |
| Respaldo de la base y de los objetos | Procedimiento documentado, probado al menos una vez antes de la entrega |
| Verificación de la cadena de auditoría | Expuesta como operación de la plataforma, no como proceso oculto |

**Por qué las migraciones van con el despliegue y no a mano:** el esquema es parte del código (ADR-0003). Una migración aplicada a mano a una versión distinta de la que la generó es la forma más fiable de dejar producción en un estado que nadie puede reproducir.

**La verificación de la cadena de auditoría es un caso interesante** de este factor: podría haber sido un script de administración, y se decidió exponerla como una consulta de la plataforma, restringida a los roles que rinden cuentas. Así se puede ejecutar en vivo durante la sustentación para demostrar la inmutabilidad, en lugar de describirla.

---

## Conclusión

Diez factores se cumplen y dos se cumplen parcialmente, y los dos parciales son **el mismo problema visto desde dos lados**: la línea base se despliega en una sola instancia en una sola máquina (ADR-0016), así que el estado en memoria no molesta (factor VI) y el escalado horizontal no se ejerce (factor VIII).

Lo que permite decir que esto es una limitación del despliegue y no del diseño:

- El trabajador ya usa `SKIP LOCKED`, así que una segunda instancia no duplicaría envíos.
- La caché está detrás de un puerto, así que pasar a una distribuida es un cambio de adaptador.
- No hay estado de sesión en el proceso.

El punto de revisión está nombrado, en lugar de descubrirse el día que haga falta escalar.

# Evidencias de la entrega

El enunciado pide doce evidencias. Esta carpeta las reúne y este índice dice, para cada una, **qué demuestra**, **cómo se obtiene** y **en qué estado está**.

Las capturas se guardan aquí con el nombre que indica cada fila, para que el orden del archivo sea el orden de esta tabla.

**Una idea que conviene tener presente al recogerlas.** Una captura de algo en verde prueba poco por sí sola: lo que convence es la captura **más la explicación de qué pasaría si fallara**. Por eso cada evidencia lleva abajo una frase de qué impide exactamente.

## Estado

**Las doce están conseguidas**, todas contra el sistema desplegado y no contra un entorno local.
Cada una está explicada en [00-registro-de-verificaciones.md](00-registro-de-verificaciones.md),
con el dato que la respalda y qué demuestra exactamente.

| # | Evidencia | Estado | Archivo |
|---|---|---|---|
| 1 | CI | **Conseguida** | `01-ci-*.png` |
| 2 | CD | **Conseguida** | `02-cd-*.png` |
| 3 | Sonar | **Conseguida** | `03-sonar-*.png` |
| 4 | Trivy | **Conseguida**, con la ejecución bloqueada | `04-trivy-*.png` |
| 5 | Publicación | **Conseguida** | `05-publicacion-*.png` |
| 6 | Despliegue | **Conseguida** desde datos móviles | `06-despliegue-*.jpg` |
| 7 | Correo | **Conseguida** con SMTP real | `07-correo-recibido.png` |
| 8 | WhatsApp | **Conseguida** en el celular | `08-whatsapp.jpg` |
| 9 | Notificaciones push | **Conseguida** en pantalla bloqueada | `09-push-*.jpg` |
| 10 | Auditoría | **Conseguida** con los dos roles | `10-auditoria-*.png` |
| 11 | Observabilidad | **Conseguida** con la frontera asíncrona | `11-observabilidad-*.png` |
| 12 | Pruebas | **Conseguida** | `12-pruebas-*.png` |

## Cómo se obtiene cada una

### 1. CI

Las ejecuciones en verde de los dos repositorios. En el back-end son seis trabajos y en el cliente cuatro.

`https://github.com/jhonnyebc/UcoKudos/actions` y `https://github.com/jhonnyebc/UcoKudosWeb/actions`

**Qué impide:** que entre código que no compila, sin formato o con pruebas en rojo. Cada trabajo bloquea; ninguno es informativo, que es el antipatrón contra el que escribe ADR-0017.

**Con una excepción que hay que decir, no esconder:** la puerta de **cobertura** tiene el umbral en 0, así que está escrita y no bloquea. La cobertura real es del 18,34 % de líneas sobre toda la solución. Es el antipatrón del propio ADR-0017 dentro del pipeline que lo aplica, y queda declarado en la sección 16.5 del DAS.

### 2. CD

El trabajo que publica la imagen, y la ejecución del guion de despliegue con su comprobación final.

En la máquina: `./scripts/desplegar.sh <etiqueta-api> <etiqueta-web>`

**Con una precisión que hay que decir:** hasta el 2026-11-13 el pipeline solo **publicaba** la imagen escaneada y el despliegue era ese guion, ejecutado a mano por SSH. Desde entonces un trabajo `desplegar` lo ejecuta por SSH con una llave de comando forzado (ver la revisión de ADR-0016 y la sección 8.5 del manual). **Evidencia del despliegue disparado por el pipeline:** `02-cd-despliegue-desde-el-pipeline.png`, la ejecución #123 del CI del back-end (2026-11-13), con todos los trabajos en verde y el último, «Desplegar la API en el servidor», terminado en 22 s tras publicar la imagen. Lo mismo para el cliente: `02-cd-despliegue-del-cliente-desde-el-pipeline.png`, la ejecución #52 del CI del repositorio del cliente, con «Desplegar el cliente en el servidor» en verde (10 s).

**Qué impide:** desplegar algo distinto de lo que pasó las puertas de calidad, y dirigir tráfico a una versión cuyas migraciones no se aplicaron.

### 3. Sonar

El tablero del proyecto con la cobertura y la puerta de calidad.

`https://sonarcloud.io/project/overview?id=jhonnyebc_UcoKudos`

**Qué impide:** que la deuda técnica crezca sin que nadie la mire. Conviene capturar también que el trabajo **falla** si falta el token, no que se omite en silencio.

### 4. Trivy

Los dos escaneos: dependencias e imagen de contenedor. En el cliente, además, la auditoría de paquetes de npm.

**Qué impide:** desplegar una imagen con vulnerabilidades conocidas. Vale la pena contar que la primera ejecución del cliente encontró **dieciocho**, dos de ellas críticas, y que se corrigieron antes de desplegar.

### 5. Publicación

Los paquetes en el registro, con sus etiquetas de versión y commit.

`https://github.com/jhonnyebc?tab=packages`

**Qué impide:** desplegar una etiqueta móvil. Con `latest` nadie sabe qué está corriendo; con `0.1.0-<sha>` se sabe exactamente.

### 6. Despliegue

La URL pública funcionando, **vista desde datos móviles y no desde la red de casa**, con el candado del certificado. Y la lista de contenedores mostrando que solo el proxy publica puertos.

`https://kudos.20.83.117.236.sslip.io`

**Qué impide:** que la base de datos o el almacenamiento queden accesibles desde internet.

### 7. Correo

El correo recibido, **abierto en un celular**, con su HTML.

Hoy el servidor lo tiene en modo simulado porque no hay proveedor transaccional contratado. Dos caminos: contratar uno —diez minutos— o demostrarlo en local con el contenedor de captura, declarando la diferencia.

**Qué impide:** que un reconocimiento se quede sin avisar a quien lo recibió. Y hay una anécdota útil: el HTML viajaba etiquetado como texto plano y ningún cliente lo pintaba; se encontró mirando el correo capturado, no leyendo el código.

### 8. WhatsApp

El mensaje recibido en el celular, con la plantilla aprobada.

Requiere poner el token en el servidor y cambiar el proveedor a `MetaCloud`.

**Qué impide:** nada menos que el canal que el enunciado pone como ejemplo. Conviene explicar por qué **siempre** va por plantilla: el destinatario de un Kudo nunca escribió primero, así que no hay ventana de veinticuatro horas.

### 9. Notificaciones push

La notificación del sistema, con el navegador cerrado. Ahora que hay HTTPS, también se puede capturar **en la pantalla bloqueada de un celular**, que es mucho más convincente.

**Qué impide:** que un aviso se pierda cuando la persona no tiene la aplicación abierta. Merece contarse que el sistema **elige el canal solo**: si hay conexión en vivo entrega por ahí y no envía push, para no notificar dos veces.

### 10. Auditoría

La operación de verificación de la cadena de hashes, ejecutada en vivo contra la base del servidor.

`GET /api/auditoria/verificacion`

**Qué impide:** que alguien modifique el registro de lo ocurrido sin que se note. Vale la pena enseñar la cadena rompiéndose al alterar una fila a mano, que es la demostración de verdad.

### 11. Observabilidad

La traza completa de una publicación, con su identificador, **cruzando la frontera asíncrona**: la petición que publica el Kudo y el proceso que envía los avisos después, unidos por la misma correlación.

El panel está en el servidor y no se publica a internet: se llega por un túnel.

**Qué impide:** tener que adivinar qué pasó. Aquí entra bien el caso real: un 500 del muro que no dejaba rastro porque el manejador de errores no registraba nada.

### 12. Pruebas

El resumen de los cuatro niveles y el reporte de cobertura.

Hoy: **96 unitarias**, **6 de arquitectura**, **46 de integración** contra PostgreSQL real y **35 del cliente**, con una omitida y documentada.

**Qué impide:** cada nivel impide algo distinto, y eso es lo que hay que contar. Las de arquitectura detectaron una violación real de la regla de dependencias que el compilador permitía. Las de integración destaparon la trampa que ADR-0013 anticipaba con el formato de la columna de auditoría.

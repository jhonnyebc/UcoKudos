# Desarrollo seguro

El enunciado pide análisis y justificación sobre **autenticación, autorización, gestión de secretos, validaciones, APIs, integraciones con terceros y riesgos de seguridad**. Este documento recorre esos siete puntos y cierra con una matriz de riesgos y el mapeo contra el OWASP Top 10.

El contexto importa para calibrar las decisiones: la plataforma maneja contenido sobre personas identificables de una institución. Un Kudo influye en la reputación de alguien, los adjuntos pueden ser documentos personales, y la jerarquía de siete roles significa que «quién puede hacer qué» no es un detalle sino el corazón del problema.

## 1. Autenticación

### Decisión y justificación

**La plataforma no custodia contraseñas.** La autenticación se delega a un proveedor OIDC/OAuth2 externo, y la API actúa como validador de token (ADR-0004).

No es solo una decisión de plazo: la gestión de credenciales es el componente donde más fácil se introducen vulnerabilidades. Delegándola desaparecen de golpe el almacenamiento de hashes, la política de contraseñas, el bloqueo por intentos, el restablecimiento por correo y la gestión de sesiones, que son cinco superficies de ataque que no hay que escribir ni auditar.

### Qué se valida en cada petición

| Qué | Por qué |
|---|---|
| Firma del token | Que lo emitió quien dice |
| Emisor y audiencia | Que es un token para **esta** aplicación, y no uno válido emitido para otra |
| Vigencia | Que no caducó |
| Que el correo del token corresponda a una persona **activa** | Que la cuenta no se desactivó después de emitirse el token |

La última es la que no suele estar y aquí es deliberada: **el estado de la cuenta se consulta en cada petición, sin caché**. Si se desactiva a alguien, deja de tener acceso de inmediato y no cuando expire su token. El costo es una consulta de una fila con índice.

### En el cliente

- Flujo de **código de autorización con PKCE**, no el implícito.
- **Los tokens viven en memoria, nunca en el almacenamiento local del navegador.** Un token en almacenamiento local es accesible a cualquier script inyectado, y basta una dependencia comprometida del cliente para robarlo.
- El cliente no decide nada de autorización: oculta lo que el usuario no puede hacer por comodidad, pero la regla la aplica la API. **Un menú escondido no es un control de acceso.**

### Riesgo residual declarado

Dependencia de un servicio externo para iniciar sesión: si está caído, nadie entra. Es aceptable frente a custodiar contraseñas, y el enunciado no fija un objetivo de disponibilidad para el inicio de sesión.

## 2. Autorización

Es el punto más delicado de esta plataforma, porque las reglas del enunciado no son un mapa de permisos plano.

### Las cuatro clases de regla

| Clase | Regla del enunciado | Dónde se aplica |
|---|---|---|
| **Por rol** | Solo Administrador, Rector y Coordinador Académico administran categorías (RF-06). Solo Rector y Coordinador Académico vetan (RF-12) | Servicio de dominio `JerarquiaInstitucional` |
| **Por ámbito** | Un Coordinador de Programa administra a los estudiantes y docentes **de sus programas**, no a todos | El mismo servicio, consultando la estructura académica |
| **Por jerarquía** | Cada solicitud la aprueba el nivel inmediatamente superior (RF-03) | El mismo servicio, con el nivel del rol |
| **Por invariante** | Un rol activo por persona; un Rector, un Coordinador Académico, uno por facultad y uno por programa (RF-04, RN-04) | Dominio **e** índices únicos parciales en la base |

### Dos decisiones que conviene poder defender

**El rol no viaja en el token.** El token aporta identidad; el rol y el ámbito se resuelven contra la base en cada petición. Razón: un rol en un claim no se puede revocar antes de que el token expire, y el ámbito por facultad y programa no cabe de forma mantenible en un token. Es el error más común en este tipo de sistemas.

**Las invariantes se defienden dos veces, en el dominio y en la base.** Una validación en la aplicación no resiste dos peticiones simultáneas: dos intentos de nombrar Rector, procesados a la vez, pasarían los dos la comprobación. Un índice único parcial rechaza el segundo (QA-SEG-03). La duplicación es deliberada, y el ADR-0003 la declara como contrapartida: es una regla escrita en dos lugares que hay que mantener de acuerdo, verificada con pruebas de integración.

### Y lo que se audita

Los accesos **rechazados** se registran (QA-SEG-01, ADR-0013). Un intento fallido suele informar más que uno exitoso: es lo que permite detectar a alguien tanteando permisos.

### Riesgo residual declarado

Es código propio de seguridad, así que hay que probarlo bien. La mitigación: el servicio de dominio se prueba con una tabla de casos que recorre las seis filas del flujo de aprobaciones del enunciado, incluida la del Administrador que se aprueba a sí mismo, y las invariantes se prueban contra una base real.

## 3. Gestión de secretos

La plataforma maneja seis secretos: contraseña de la base, token de WhatsApp, credenciales SMTP, credenciales del almacenamiento, clave privada VAPID y el secreto del cliente de identidad.

### Reglas

1. **Ningún secreto en el repositorio.** Ni cifrado: cifrarlo desplaza el problema a custodiar la clave, y si esa se filtra, todo el historial queda expuesto de una vez.
2. **Los secretos entran por variables de entorno y no tienen valor por omisión.** Si falta, la aplicación **no arranca** y el mensaje nombra la variable. Un valor por omisión para un secreto es una contraseña en el repositorio con otro nombre, y oculta el error hasta que algo falla en producción.
3. **Tres lugares, uno por entorno:** almacén del entorno de desarrollo fuera del árbol del proyecto, secretos del repositorio en el pipeline, y archivo de entorno con permisos restringidos en producción.
4. **Doble escaneo:** un gancho antes del envío y una puerta en el pipeline. El que importa es el local, porque detectar un secreto en el pipeline significa que ya está en el remoto, y borrarlo del archivo no lo saca del historial.
5. **Rotación documentada** para cada secreto: qué hay que cambiar y qué hay que reiniciar. Un secreto que nadie sabe rotar es un secreto que no se rota nunca.
6. **Nada secreto en el cliente.** Todo lo que se compila en el cliente es público. Una regla de análisis estático lo impide activamente.

### La confusión que hay que evitar

**La clave pública VAPID no es un secreto; la privada sí.** El cliente necesita la pública. Publicar la privada permitiría a cualquiera enviar notificaciones a los usuarios de la plataforma. Es el error típico de Web Push.

### Riesgo residual declarado

**No hay auditoría de quién accedió a cada secreto**, que es lo que un gestor dedicado aportaría, y el archivo de entorno en la máquina lo lee quien entre al servidor. Se descartó por costo (RT-07), y se mitiga con permisos del sistema de archivos y con la superficie expuesta mínima.

## 4. Validaciones

### Principio

**Toda entrada se valida en el servidor.** Lo que valida el cliente es por comodidad del usuario, y la API lo repite siempre: el cliente lo controla quien lo usa.

### Por tipo de entrada

| Entrada | Validación |
|---|---|
| Correo institucional | Formato y normalización antes de comparar, para que no entren dos cuentas que difieren en mayúsculas |
| Celular | Normalización a formato internacional E.164 **en el borde de entrada**, no en el adaptador, porque es el formato que exige la API de WhatsApp |
| Contenido de un Kudo o réplica | No vacío. **Sin límite de longitud** (RN-03): una columna de texto no tiene límite práctico |
| Identificadores | Tipados; un identificador mal formado es 400, no 500 |
| Paginación | Tamaño de página acotado, para que ninguna consulta pueda devolver una cantidad arbitraria de datos |
| Calificación | Entre 1 y 5, validado en el dominio |
| Adjuntos | Ver el apartado siguiente |

### Las invariantes se validan donde no se pueden eludir

Las entidades **validan sus invariantes en el constructor**, de modo que no exista una instancia inválida. Un Kudo cuyo autor es su propio destinatario no se puede construir. Esto es distinto de validar en el endpoint: una validación en el endpoint se olvida en el segundo endpoint que crea Kudos.

### Protección contra las inyecciones

| Vector | Mitigación |
|---|---|
| Inyección SQL | Consultas parametrizadas siempre, incluidas las escritas a mano para la bandeja y el árbol de réplicas. Ninguna cadena se concatena |
| Cross-site scripting | El contenido de un Kudo es texto de usuario y se escapa al mostrarlo. Nunca se interpreta como HTML. El nombre original de un adjunto también se escapa: puede contener etiquetas |
| Recorrido de rutas | El nombre del objeto en el almacenamiento es generado, **no el nombre del usuario** |
| Falsificación de petición entre sitios | La autenticación es por token en cabecera, no por cookie de sesión, así que el vector no aplica. Si se añadiera algún flujo con cookie, habría que reintroducir la protección |

## 5. Seguridad de las APIs

| Medida | Detalle |
|---|---|
| **HTTPS obligatorio** | Único puerto expuesto, TLS renovado automáticamente. No es una mejora: el service worker y la API de notificaciones solo funcionan sobre un origen seguro |
| **Token exigido por grupo de rutas**, no con una política global de reserva | Una política global convierte las rutas inexistentes en 401 y oculta los errores de enrutamiento detrás de un problema de autenticación |
| **Errores sin filtración** | Formato estándar de problema HTTP con el identificador de traza. Nunca una traza de pila ni un mensaje del motor de base de datos |
| **Códigos coherentes** | 400 para una invariante, 403 para autorización, 409 para conflicto de concurrencia, 404 para lo inexistente. No se usa 404 para ocultar un 403 en contenido que el usuario sabe que existe |
| **Límite de tasa** en el proxy | Contención básica ante abuso automatizado |
| **Superficie mínima** | La base, el almacenamiento y el recolector de observabilidad **no** se publican a internet |
| **Contrato explícito** | OpenAPI, del que se generan los tipos del cliente: una ruptura de contrato falla al compilar en lugar de en producción |
| **Sin webhook entrante** | WhatsApp es solo de salida, así que no hay punto de entrada público para ese canal. Es superficie que no existe en lugar de superficie protegida |

## 6. Integraciones con terceros

Cuatro integraciones, todas detrás de un puerto. Lo que sigue es el análisis de riesgo de cada una.

### WhatsApp Cloud API

| Aspecto | Análisis |
|---|---|
| Credencial | Token de usuario del sistema, sin caducidad, guardado como secreto. **No** el token temporal del portal, que caduca en horas |
| Dirección | Solo salida. Sin webhook: no hay firma que verificar ni punto de entrada que proteger |
| Datos que salen | Nombre del autor, categoría y un enlace. **No** se envía el contenido del Kudo, que puede ser extenso y es información de personas |
| Riesgo | El número de prueba solo envía a destinatarios registrados. Es una limitación funcional, no de seguridad |

### Proveedor de correo

| Aspecto | Análisis |
|---|---|
| Credencial | Usuario y clave SMTP como secretos. **No** la credencial personal del autor, que sería una mala práctica |
| Transporte | SMTP sobre TLS |
| Datos que salen | Lo mínimo del aviso. **El adaptador nunca registra el cuerpo del correo en los logs**: puede contener el texto del Kudo |
| Riesgo | Sin dominio propio verificado, algunos clientes muestran un aviso de envío en nombre de otro |

### Servicio de push del navegador

| Aspecto | Análisis |
|---|---|
| Credencial | Clave privada VAPID como secreto; la pública va al cliente y no lo es |
| Datos que salen | Mínimos. **Una notificación aparece en la pantalla de bloqueo**, donde la puede leer cualquiera que tenga el teléfono a la vista, así que no se incluye el contenido del Kudo |
| Riesgo | Suscripciones caducadas: se depuran al recibir el rechazo del servicio |

### Proveedor de identidad

| Aspecto | Análisis |
|---|---|
| Credencial | Secreto del cliente, guardado como secreto. El identificador del cliente no es secreto |
| Validación | Emisor, audiencia, firma y vigencia en cada petición |
| Riesgo | Hay dos fuentes de verdad sobre la persona: el proveedor tiene el correo y la plataforma el rol. Hay que mantenerlas de acuerdo al cambiar un correo |

### La regla transversal

**Ninguna integración recibe un correo o un teléfono desde un módulo de negocio.** El gateway resuelve el destinatario a partir de su identificador interno (ADR-0006). Así, una falla de autorización en un módulo no se convierte en un envío a la dirección equivocada.

## 7. Seguridad de los adjuntos

Es el punto con más superficie, y ADR-0012 lo desarrolla. Resumen de las decisiones de seguridad:

| Medida | Por qué |
|---|---|
| **Contenedor privado**, sin lectura anónima | Es la decisión central. Un contenedor público haría irrelevante todo lo demás |
| **URL prefirmada de vida corta**, emitida tras comprobar permisos | La autorización la hace la plataforma, que conoce la jerarquía; el almacenamiento solo honra una firma |
| El enlace **vence en minutos** | Compartirlo fuera no da acceso permanente (QA-SEG-02) |
| **Tipo determinado por el contenido real**, no por la extensión ni por lo que declare el cliente | Las dos las controla quien sube el archivo |
| Lista blanca de tipos, tamaño máximo y cantidad por Kudo | Contención de abuso |
| **Nombre del objeto generado**, no el del usuario | Evita recorrido de rutas y colisiones |
| **Hash del contenido** guardado | Verificación de integridad al restaurar |
| Carga y **descarga** auditadas | Responde «quién vio qué archivo y cuándo» |

**La prueba que hay que escribir no es que la descarga funcione, sino que falle:** pedir el objeto sin firma debe ser rechazado, y una URL expirada también. Sin esas dos pruebas, la estrategia de seguridad es una afirmación.

## 8. Seguridad en el ciclo de desarrollo

La seguridad no es una revisión al final (DRV-007). Las puertas del pipeline que son de seguridad:

| Puerta | Qué evita |
|---|---|
| **Escaneo de secretos** antes del envío y en el pipeline | Que un secreto llegue al historial |
| **Trivy sobre dependencias y configuración** | Vulnerabilidades conocidas en lo que se usa |
| **Trivy sobre la imagen** | Vulnerabilidades en el sistema base. Se escanea y se publica **la misma** imagen: reconstruir después invalidaría el escaneo |
| **Análisis estático** | Patrones peligrosos en el código propio |
| **Acciones fijadas por identificador de commit**, no por etiqueta | Ataque a la cadena de suministro: una etiqueta se puede mover a código distinto |
| **Dependencias instaladas sin ejecutar sus scripts** | Lo mismo, en el cliente |
| **Imagen base mínima**, sin consola ni gestor de paquetes, usuario sin privilegios | Menos paquetes, menos hallazgos, y nada que ejecutar si alguien entra |
| **Compilación sin advertencias** | Una advertencia aceptada una vez se acepta siempre |

**Las vulnerabilidades que bloquean son las altas y críticas con corrección disponible.** Bloquear por algo sin parche detiene el trabajo sin ofrecer salida, y así es como se acaba desactivando la puerta.

## 9. Matriz de riesgos

| # | Riesgo | Probabilidad | Impacto | Mitigación | Residual |
|---|---|---|---|---|---|
| R-01 | Un secreto llega al repositorio | Media | Alto | Doble escaneo; sin valores por omisión; `.env` ignorado | Bajo |
| R-02 | Acceso a un adjunto sin autorización | Media | Alto | Contenedor privado; URL firmada de vida corta tras comprobar permisos; pruebas negativas | Bajo |
| R-03 | Escalada de privilegios por un hueco de la jerarquía | Media | Alto | Reglas en un servicio de dominio probado; invariantes también en la base; intentos rechazados auditados | Medio |
| R-04 | Dos roles únicos activos por concurrencia | Baja | Alto | Índices únicos parciales | Muy bajo |
| R-05 | Robo de token desde el navegador | Baja | Alto | Tokens en memoria, nunca en almacenamiento local; HTTPS | Bajo |
| R-06 | Alteración del registro de auditoría | Baja | Alto | Tabla de solo inserción por disparador; cadena de hashes verificable | Muy bajo |
| R-07 | Inyección SQL | Baja | Crítico | Consultas parametrizadas siempre, también las escritas a mano | Muy bajo |
| R-08 | Contenido malicioso en un Kudo | Media | Medio | Escapado al mostrar; nunca interpretado como HTML | Bajo |
| R-09 | Carga de un archivo peligroso | Media | Medio | Tipo por contenido real; lista blanca; tamaño y cantidad acotados | Bajo |
| R-10 | Dependencia vulnerable | Alta | Medio | Trivy bloqueante en dependencias e imagen; imagen base mínima | Bajo |
| R-11 | Ataque a la cadena de suministro | Baja | Alto | Acciones por SHA; sin scripts de instalación; versiones exactas | Bajo |
| R-12 | Abuso automatizado de la API | Media | Medio | Límite de tasa en el proxy; paginación acotada | Medio |
| R-13 | Datos personales en logs o auditoría | Media | Medio | No se registran contenidos; la auditoría guarda qué cambió, no los valores; revisión manual antes de la entrega | Bajo |
| R-14 | **Pérdida total por caída de la máquina** | Baja | Crítico | Respaldo probado de la base **y** del volumen de objetos, coordinado | **Alto** |
| R-15 | Acceso al servidor expone el archivo de entorno | Baja | Alto | Permisos del sistema de archivos; superficie mínima | Medio |

**R-14 es el riesgo más alto que queda sin resolver**, y viene del presupuesto cero: una sola máquina sin redundancia (ADR-0016). Lo honesto es declararlo, no esconderlo detrás del respaldo, que es recuperación y no disponibilidad.

**R-03 queda en medio** porque es código propio de seguridad: por bien probado que esté, una regla de autorización escrita a mano es más arriesgada que una delegada a un producto maduro. Se asumió a cambio de tener la jerarquía en el dominio, que es lo que el enunciado valora.

## 10. Mapeo con el OWASP Top 10

| Categoría | Cómo se aborda |
|---|---|
| **A01 Control de acceso roto** | Autorización en el servidor, por rol y por ámbito; nunca en el cliente; adjuntos solo con firma tras comprobar permisos; intentos rechazados auditados |
| **A02 Fallas criptográficas** | HTTPS obligatorio; SMTP sobre TLS; sin contraseñas propias que proteger; secretos fuera del repositorio |
| **A03 Inyección** | Consultas parametrizadas; escapado al mostrar; validación de entrada en el servidor |
| **A04 Diseño inseguro** | Invariantes en el dominio **y** en la base; el gateway resuelve el destinatario por identificador interno; sin webhook entrante |
| **A05 Configuración de seguridad incorrecta** | Un solo puerto expuesto; imagen sin consola, usuario sin privilegios; Trivy sobre configuración; secretos sin valor por omisión |
| **A06 Componentes vulnerables y desactualizados** | Trivy bloqueante; versiones exactas; imagen base mínima |
| **A07 Fallas de identificación y autenticación** | Delegada a un proveedor OIDC; PKCE; tokens en memoria; estado de la cuenta comprobado en cada petición |
| **A08 Fallas de integridad del software y los datos** | Acciones por SHA; sin scripts de instalación; imagen inmutable `versión-sha`; hash de los adjuntos; cadena de hashes en la auditoría |
| **A09 Fallas de registro y monitoreo** | Auditoría inmutable aparte de los logs; logs estructurados con identificador de traza; métricas de la bandeja y de los envíos fallidos |
| **A10 Falsificación de petición del lado del servidor** | La plataforma no obtiene URL que le indique el usuario. Los destinos externos son fijos y vienen de configuración, no de la petición |

## 11. Lo que queda fuera y por qué

| No se hace | Razón |
|---|---|
| WAF | La evolución si la plataforma quedara expuesta a tráfico real. Hoy el proxy con límite de tasa es proporcionado al alcance (ADR-0016) |
| Segundo factor para los roles altos | Sería lo correcto para Rector y Administrador. Queda fuera por plazo; el proveedor de identidad lo soporta, así que es configuración y no código |
| Cifrado de campos en la base | El contenido de un Kudo es institucional y visible en la plataforma, no un dato reservado. El teléfono sí sería candidato si el alcance creciera |
| Gestor de secretos dedicado | Costo. Es lo único que daría auditoría de acceso a los secretos (ADR-0018) |
| Pruebas de penetración | Fuera del alcance del curso. El pipeline cubre análisis estático y de dependencias, no dinámico |

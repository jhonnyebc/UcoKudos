# Diagramas de secuencia

Los flujos críticos de la plataforma. El primero es el que el enunciado exige implementar completo (RF-07) y el que concentra las decisiones de ADR-0005, ADR-0006 y ADR-0013.

## 1. Publicar un Kudo — la transacción

Primera mitad del flujo obligatorio: lo que ocurre mientras el usuario espera. Nótese que **no se habla con ningún proveedor externo** en esta parte.

```mermaid
sequenceDiagram
    autonumber
    actor Autor as Docente (autor)
    participant PWA as Cliente web
    participant API as Endpoint /api/kudos
    participant CU as PublicarKudoCasoUso
    participant Dom as Kudo (dominio)
    participant UT as UnidadTrabajo
    participant BD as PostgreSQL

    Autor->>PWA: Escribe el Kudo y elige categoría
    Note over PWA,API: El adjunto ya se subió aparte<br/>con URL prefirmada (ver secuencia 4)
    PWA->>API: POST /api/kudos<br/>{destinatario, categoria, contenido, adjuntos}
    API->>API: Valida el token JWT y genera<br/>el id de correlación
    API->>CU: EjecutarAsync(datos, ct)

    CU->>BD: Resuelve la persona del token<br/>y comprueba que esté activa
    BD-->>CU: Persona y rol activo
    CU->>BD: Verifica que la categoría esté activa<br/>y el destinatario exista
    BD-->>CU: Categoría y destinatario

    CU->>Dom: new Kudo(autor, destinatario, categoria, contenido, ahora)
    Note over Dom: Valida sus invariantes al construirse:<br/>autor distinto del destinatario,<br/>contenido no vacío, categoría activa
    Dom-->>CU: Kudo válido

    CU->>UT: ConfirmarAsync(kudo, eventoAuditoria, eventoPendiente)

    rect rgb(238, 246, 255)
        Note over UT,BD: UNA SOLA TRANSACCIÓN (ADR-0005)
        UT->>BD: BEGIN
        UT->>BD: INSERT kudo
        UT->>BD: INSERT adjunto (metadatos)
        UT->>BD: INSERT evento_auditoria<br/>(kudo.publicar, con hash encadenado)
        UT->>BD: INSERT evento_pendiente<br/>(KudoPublicado, con contexto de traza)
        UT->>BD: COMMIT
    end

    BD-->>UT: Confirmada
    UT-->>CU: Listo
    CU-->>API: KudoDto
    API-->>PWA: 201 Created
    PWA-->>Autor: "Kudo publicado"

    Note over Autor,BD: El usuario ya terminó. Los tres avisos<br/>salen después, en la secuencia 2
```

### Lo que hay que saber defender de esta secuencia

**Los cinco pasos del enunciado no ocurren todos aquí**, y es deliberado. En esta transacción ocurren el paso 1 (persistencia) y el paso 5 (auditoría). Los pasos 2, 3 y 4 ocurren en la secuencia siguiente.

Si los tres envíos se hicieran dentro de esta transacción, pasarían dos cosas malas: el usuario esperaría a tres servicios externos, y un 503 de Meta haría fallar la publicación de un Kudo que no tiene nada de malo. Eso contradice QA-DISP-01 y castiga la responsividad que DRV-001 exige en red móvil.

**El evento de auditoría y el evento pendiente se escriben en la misma transacción que el Kudo.** Si se confirma, se confirman los tres; si falla, no queda rastro de un cambio que no ocurrió. Esa atomicidad es lo que cierra la ventana «commit exitoso, envío perdido» que QA-CONF-01 describe.

## 2. Publicar un Kudo — los tres avisos

Segunda mitad. Ocurre en el proceso en segundo plano, segundos después y en otro hilo.

```mermaid
sequenceDiagram
    autonumber
    participant Proc as Proceso de notificaciones
    participant BD as PostgreSQL
    participant GW as GatewayNotificaciones
    participant Cat as CatalogoMensajes
    participant WA as Adaptador WhatsApp
    participant SMTP as Adaptador correo
    participant Push as Adaptador push
    participant Meta as WhatsApp Cloud API
    participant Corr as Proveedor de correo
    participant Nav as Servicio de push
    actor Dest as Estudiante (destinatario)

    loop cada 2 segundos
        Proc->>BD: BEGIN
        Proc->>BD: SELECT * FROM evento_pendiente<br/>WHERE procesado_en IS NULL<br/>AND proximo_intento <= ahora<br/>LIMIT 1 FOR UPDATE SKIP LOCKED
        BD-->>Proc: Evento KudoPublicado
        Note over Proc: SKIP LOCKED: si hubiera varias<br/>instancias, ninguna toma el mismo<br/>evento que otra (QA-ELAS-01)

        Proc->>Proc: Retoma el contexto de traza<br/>guardado en el evento (ADR-0014)
        Proc->>GW: ProcesarEvento(KudoPublicado)

        GW->>BD: Datos del destinatario y del autor
        BD-->>GW: Persona, correo, celular
        GW->>Cat: Texto de kudo.recibido con sus variables
        Cat-->>GW: Mensaje en es-CO

        Note over GW: Política de canal para este tipo de aviso:<br/>correo + WhatsApp + tiempo real

        par Correo
            GW->>SMTP: Enviar(destinatario, asunto, html)
            SMTP->>Corr: SMTP/TLS
            Corr-->>SMTP: Aceptado
            SMTP-->>GW: Enviado
            Corr-->>Dest: Correo electrónico
        and WhatsApp
            GW->>WA: EnviarPlantilla(celular, kudo_recibido, variables)
            Note over WA,Meta: Siempre por plantilla aprobada: el destinatario<br/>nunca escribió primero, así que la plataforma<br/>está siempre fuera de la ventana de 24 h
            WA->>Meta: POST /messages
            Meta-->>WA: 200 + id del mensaje
            WA-->>GW: Enviado
            Meta-->>Dest: Mensaje de WhatsApp
        and Tiempo real
            GW->>BD: INSERT aviso (queda persistido)
            alt El destinatario tiene conexión SSE abierta
                GW->>Dest: Aviso por SSE, al instante
                Note over GW: No se envía Web Push:<br/>no se notifica dos veces lo mismo
            else No está conectado y tiene suscripción
                GW->>Push: Enviar(suscripcion, aviso)
                Push->>Nav: Web Push con VAPID
                Nav-->>Dest: Notificación en el celular,<br/>aunque esté bloqueado
            else No está conectado y no tiene suscripción
                Note over GW: El aviso queda guardado<br/>y lo ve al entrar
            end
        end

        GW->>BD: INSERT notificacion por canal<br/>(único por evento, destinatario y canal)
        Note over GW,BD: Ese índice único es la idempotencia:<br/>reprocesar no envía dos veces (QA-CONF-02)
        GW->>BD: INSERT evento_auditoria por envío
        GW-->>Proc: Procesado

        Proc->>BD: UPDATE evento_pendiente SET procesado_en = ahora
        Proc->>BD: COMMIT
        Note over Proc,BD: Se marca procesado DENTRO de la misma<br/>transacción: hacerlo después reabriría<br/>la ventana de pérdida
    end
```

### Cómo responde al fallo

```mermaid
sequenceDiagram
    autonumber
    participant Proc as Proceso de notificaciones
    participant GW as GatewayNotificaciones
    participant WA as Adaptador WhatsApp
    participant Meta as WhatsApp Cloud API
    participant BD as PostgreSQL

    Proc->>GW: ProcesarEvento(KudoPublicado)
    GW->>WA: EnviarPlantilla(...)
    WA->>Meta: POST /messages
    Meta-->>WA: 503 Service Unavailable

    WA->>WA: Clasifica el error
    Note over WA: 408, 429, 5xx y fallas de red son<br/>TRANSITORIOS. El resto, PERMANENTES:<br/>reintentar un número inválido<br/>no lo vuelve válido

    WA-->>GW: Transitorio
    GW->>BD: UPDATE notificacion SET estado='Fallida'<br/>para el canal de WhatsApp
    Note over GW: El correo y el push ya salieron:<br/>un canal caído degrada el servicio,<br/>no lo interrumpe

    GW-->>Proc: Falló un canal
    Proc->>BD: UPDATE evento_pendiente<br/>SET intentos = intentos + 1,<br/>proximo_intento = ahora + espera creciente

    alt Quedan intentos
        Note over Proc,BD: El evento vuelve a la cola
    else Agotó los intentos
        Proc->>BD: UPDATE evento_pendiente SET procesado_en = ahora
        Proc->>BD: INSERT evento_auditoria (envío descartado)
        Note over Proc,BD: Queda registrado y medido:<br/>una bandeja que crece es el primer<br/>síntoma de un proveedor caído
    end
```

El Kudo quedó publicado y auditado desde la secuencia 1. **Meta caída no impide publicar** (QA-DISP-01).

## 3. Consultar el muro

El flujo más frecuente de la plataforma, y el que responde a DRV-002. Dos recorridos: con la caché caliente y sin ella.

```mermaid
sequenceDiagram
    autonumber
    actor Est as Estudiante
    participant PWA as Cliente web
    participant API as Endpoint /api/muro
    participant Lec as ConsultasMuro
    participant Cache as Caché en memoria
    participant BD as PostgreSQL

    Est->>PWA: Abre el muro
    PWA->>API: GET /api/muro?limite=20<br/>If-None-Match: "etag-anterior"
    API->>API: Valida el token y resuelve la persona
    API->>Lec: PrimeraPagina(persona, limite)

    Lec->>Cache: ¿Está la primera página?

    alt Caché caliente
        Cache-->>Lec: Página
        Note over Lec,Cache: Sin tocar la base de datos
    else Caché fría
        Cache-->>Lec: No está
        Lec->>BD: UNA consulta con JOIN a autor,<br/>destinatario y categoría,<br/>leyendo los contadores ya materializados
        Note over Lec,BD: Ni una agregación ni una consulta<br/>por fila: el total de réplicas y el<br/>promedio son columnas (ADR-0010)
        BD-->>Lec: 20 filas listas para la pantalla
        Lec->>Cache: Guarda la página
    end

    Lec-->>API: Página + cursor del último elemento
    API->>API: Calcula el validador de contenido

    alt No cambió desde la última vez
        API-->>PWA: 304 Not Modified, sin cuerpo
        Note over API,PWA: En red móvil esto ahorra<br/>transferencia real (DRV-001)
    else Cambió
        API-->>PWA: 200 OK + ETag
    end

    PWA-->>Est: Muro pintado

    Est->>PWA: Desplaza hasta el final
    PWA->>API: GET /api/muro?cursor=<ultimo>&limite=20
    Note over PWA,API: Paginación por cursor, no por número de página:<br/>la página 50 cuesta lo mismo que la 1, y no<br/>se repiten ni se saltan Kudos recién publicados
    API->>Lec: SiguientePagina(cursor)
    Lec->>BD: Consulta por el índice<br/>(publicado_en DESC, id DESC)
    BD-->>Lec: 20 filas
    Lec-->>API: Página
    API-->>PWA: 200 OK
```

Cuando alguien publica un Kudo, el evento de ADR-0005 **invalida la primera página** de la caché. Por eso la invalidación es por evento y no por expiración: no hay que elegir entre datos viejos y poca eficacia.

## 4. Cargar un adjunto

Responde a RF-08 y DRV-004, y muestra las cuatro estrategias de ADR-0012 en movimiento.

```mermaid
sequenceDiagram
    autonumber
    actor Autor as Docente
    participant PWA as Cliente web
    participant API as Endpoint /api/adjuntos
    participant CU as CargarAdjuntoCasoUso
    participant Obj as Adaptador de objetos
    participant S3 as Almacenamiento (privado)
    participant BD as PostgreSQL

    Autor->>PWA: Elige un archivo
    PWA->>PWA: Comprueba tipo y tamaño<br/>(por comodidad, la API lo repite)
    PWA->>API: POST /api/adjuntos/intencion<br/>{nombre, tipo, tamaño}

    API->>CU: EjecutarAsync(datos)
    CU->>CU: Valida contra lista blanca de tipos,<br/>tamaño máximo y cantidad por Kudo
    CU->>CU: Genera la clave del objeto
    Note over CU: Clave generada, NO el nombre del usuario:<br/>un nombre que controla quien sube el archivo<br/>es un vector de recorrido de rutas
    CU->>Obj: UrlDeSubida(clave, minutos)
    Obj-->>CU: URL prefirmada de subida
    CU->>BD: INSERT adjunto (metadatos, sin confirmar)
    CU-->>API: {urlSubida, idAdjunto}
    API-->>PWA: 200 OK

    PWA->>S3: PUT el archivo directo a la URL prefirmada
    Note over PWA,S3: Los megabytes NO atraviesan la API:<br/>menos carga y menos latencia en red móvil
    S3-->>PWA: 200 OK

    PWA->>API: POST /api/adjuntos/{id}/confirmar
    API->>Obj: Lee los primeros bytes del objeto
    Obj->>S3: GET parcial
    S3-->>Obj: Cabecera del archivo
    Obj-->>API: Tipo real detectado
    API->>API: ¿Coincide con la lista blanca?
    Note over API: El tipo se determina por el CONTENIDO,<br/>no por la extensión ni por lo que<br/>declaró el cliente: las dos las controla<br/>quien sube el archivo
    API->>Obj: Calcula el hash del contenido
    Obj-->>API: Hash
    API->>BD: UPDATE adjunto (tipo, hash, confirmado)
    API->>BD: INSERT evento_auditoria (adjunto.cargar)
    API-->>PWA: 200 OK

    Note over Autor,BD: Descargar después exige otra URL prefirmada,<br/>emitida tras comprobar permisos, y vence en<br/>minutos (QA-SEG-02). Cada descarga se audita.
```

## 5. Aprobar una solicitud de cuenta

Responde a RF-02 y RF-03, y es donde se ve el servicio de dominio de la jerarquía.

```mermaid
sequenceDiagram
    autonumber
    actor Sol as Estudiante solicitante
    actor Coord as Coordinador de Programa
    participant PWA as Cliente web
    participant API as API
    participant CU as AprobarSolicitudCasoUso
    participant Jer as JerarquiaInstitucional
    participant BD as PostgreSQL
    participant Proc as Proceso de notificaciones

    Sol->>PWA: Se registra con correo institucional y celular
    PWA->>API: POST /api/solicitudes
    API->>BD: INSERT persona (estado Pendiente)<br/>+ solicitud_cuenta
    API->>BD: INSERT evento_pendiente<br/>(VerificacionCelularSolicitada)
    Proc->>Sol: Código de un solo uso por WhatsApp
    Note over Sol,Proc: El correo lo verifica el proveedor<br/>de identidad y el celular, la plataforma

    Coord->>PWA: Abre las solicitudes de su programa
    PWA->>API: GET /api/solicitudes?pendientes
    API->>Jer: ¿Sobre quién manda este usuario?
    Jer-->>API: Estudiantes y docentes de sus programas
    API->>BD: Solicitudes de ese ámbito
    BD-->>API: Lista
    API-->>PWA: 200 OK

    Coord->>PWA: Aprueba la solicitud
    PWA->>API: POST /api/solicitudes/{id}/aprobar
    API->>CU: EjecutarAsync(solicitud, aprobador)

    CU->>Jer: ¿Puede este rol aprobar al rol solicitado?
    Note over Jer: RF-03: a un Estudiante lo aprueba el<br/>Coordinador de Programa, a este el de<br/>Facultad, y así hasta el Administrador,<br/>que se aprueba a sí mismo
    alt No le corresponde
        Jer-->>CU: No
        CU->>BD: INSERT evento_auditoria (acceso rechazado)
        CU-->>API: ExcepcionAutorizacion
        API-->>PWA: 403 Forbidden
        Note over API,BD: El intento rechazado queda auditado:<br/>a menudo informa más que uno exitoso<br/>(QA-SEG-01)
    else Le corresponde
        Jer-->>CU: Sí
        CU->>BD: ¿Hay ya un rol activo para esa persona?<br/>¿Se respeta la unicidad del rol?
        Note over CU,BD: Además del dominio, lo defienden índices<br/>únicos parciales: una validación en la<br/>aplicación no resiste dos peticiones<br/>simultáneas (QA-SEG-03)
        CU->>BD: BEGIN
        CU->>BD: UPDATE solicitud_cuenta (Aprobada)
        CU->>BD: INSERT asignacion_rol (activa)
        CU->>BD: UPDATE persona (estado según las 3 condiciones)
        CU->>BD: INSERT evento_auditoria (solicitud.aprobar)
        CU->>BD: INSERT evento_pendiente (SolicitudAprobada)
        CU->>BD: COMMIT
        CU-->>API: Listo
        API-->>PWA: 200 OK
        Proc->>Sol: Correo de cuenta aprobada
    end
```

## 6. Vetar contenido en cascada

Responde a RF-12 y muestra por qué la ruta materializada de ADR-0011 paga.

```mermaid
sequenceDiagram
    autonumber
    actor Rector as Rector
    participant API as API
    participant CU as VetarContenidoCasoUso
    participant Jer as JerarquiaInstitucional
    participant Rep as ReplicaRepositorio
    participant BD as PostgreSQL

    Rector->>API: POST /api/moderacion/vetar<br/>{tipo: replica, id, motivo}
    API->>CU: EjecutarAsync(datos, actor)

    CU->>Jer: ¿Puede vetar este rol?
    Note over Jer: Solo Rector y Coordinador Académico.<br/>Ni Administrador, ni coordinadores de<br/>facultad o programa, ni docentes,<br/>ni estudiantes (RF-12)
    Jer-->>CU: Sí

    CU->>Rep: Veta la réplica y sus descendientes
    Rep->>BD: SELECT ruta FROM replica WHERE id = @id
    BD-->>Rep: '/a1/7f/c3/'
    Rep->>BD: UPDATE replica SET vetado = true<br/>WHERE ruta LIKE '/a1/7f/c3/%'
    Note over Rep,BD: UNA operación por prefijo de índice.<br/>Con solo replica_padre_id habría que<br/>recorrer el árbol nivel por nivel

    BD-->>Rep: 14 filas afectadas
    Rep-->>CU: Vetadas la réplica y 13 descendientes

    CU->>BD: UPDATE kudo SET total_replicas = ...<br/>(contador materializado al día)
    CU->>BD: INSERT veto (motivo, en_cascada = true)
    CU->>BD: INSERT evento_auditoria (contenido.vetar)
    CU-->>API: 14 contenidos ocultados
    API-->>Rector: 200 OK

    Note over Rector,BD: No tendría sentido ocultar un comentario<br/>y dejar visibles las respuestas que lo citan
```

## 7. Resumen de trazabilidad

| Secuencia | Requisitos | Escenarios de calidad | ADR |
|---|---|---|---|
| 1. Publicar — transacción | RF-07, RF-17 | QA-CONF-01 | ADR-0005, ADR-0013 |
| 2. Publicar — avisos | RF-07, RF-14, RF-15, RF-16 | QA-CONF-02, QA-ELAS-01 | ADR-0005 a ADR-0009 |
| 2b. Fallo de un canal | RF-15 | QA-DISP-01 | ADR-0005, ADR-0007 |
| 3. Consultar el muro | RF-13 | QA-REND-01, QA-REND-02 | ADR-0010 |
| 4. Cargar un adjunto | RF-08 | QA-SEG-02 | ADR-0012 |
| 5. Aprobar una cuenta | RF-01, RF-02, RF-03, RF-04 | QA-SEG-01, QA-SEG-03 | ADR-0004 |
| 6. Vetar en cascada | RF-12 | — | ADR-0011 |

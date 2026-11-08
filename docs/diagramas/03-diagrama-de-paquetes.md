# Diagrama de paquetes

Organización del código en proyectos y espacios de nombres, y las dependencias permitidas entre ellos. Es la vista que corresponde una a una con las cinco reglas de ADR-0001, las mismas que verifican las pruebas de arquitectura.

## 1. Dependencias entre proyectos

Cada capa es un proyecto. Eso importa: la dirección de las dependencias la impone **el compilador**, no la convención. Una referencia en sentido contrario no compila.

```mermaid
flowchart BT
    dominio["<b>UcoKudos.Domain</b><br/>Entidades, objetos de valor,<br/>servicios de dominio y<br/>puertos de repositorio<br/><br/><i>Cero referencias</i>"]
    aplicacion["<b>UcoKudos.Application</b><br/>Casos de uso, puertos de entrada<br/>y de salida, gateway y catálogo"]
    infraestructura["<b>UcoKudos.Infrastructure</b><br/>Adaptadores de salida:<br/>persistencia, canales, objetos,<br/>caché e instrumentación"]
    api["<b>UcoKudos.Api</b><br/>Endpoints, manejador de errores,<br/>SSE y raíz de composición"]

    unitarias["<b>UcoKudos.UnitTests</b><br/>Base de la pirámide"]
    arquitectura["<b>UcoKudos.ArchitectureTests</b><br/>Las 5 reglas de ADR-0001"]
    integracion["<b>UcoKudos.IntegrationTests</b><br/>Contra PostgreSQL y objetos reales"]

    aplicacion -->|"usa"| dominio
    infraestructura -->|"usa"| aplicacion
    api -->|"usa"| aplicacion
    api -.->|"solo en la raíz<br/>de composición"| infraestructura

    unitarias -->|"prueba"| dominio
    unitarias -->|"prueba"| aplicacion
    arquitectura -->|"inspecciona<br/>las 4 capas"| api
    integracion -->|"levanta la<br/>aplicación completa"| api

    classDef dom fill:#6b3fa0,stroke:#45286b,color:#ffffff
    classDef app fill:#1168bd,stroke:#0b4884,color:#ffffff
    classDef infra fill:#b5651d,stroke:#7d4514,color:#ffffff
    classDef iface fill:#2d7a3e,stroke:#1d5228,color:#ffffff
    classDef test fill:#777777,stroke:#4d4d4d,color:#ffffff
    class dominio dom
    class aplicacion app
    class infraestructura infra
    class api iface
    class unitarias,arquitectura,integracion test
```

La flecha punteada de `Api` a `Infrastructure` es la única excepción y está acotada: existe para que la raíz de composición registre los adaptadores. **Es la razón por la que las pruebas de arquitectura hacen falta**: el compilador ya garantiza las tres primeras reglas, pero esta referencia es legítima, así que solo una prueba puede comprobar que los endpoints no la aprovechen.

## 2. Paquetes internos

Dentro de cada capa, la organización es **por módulo primero y por tipo de pieza después**. Así, todo lo de Kudos está junto, y no repartido entre una carpeta de casos de uso y otra de DTO.

```mermaid
flowchart TB
    subgraph dom["UcoKudos.Domain"]
        direction TB
        dTrans["Transversal<br/><i>ExcepcionDominio y sus especializadas</i>"]
        dIdent["Identidad<br/><i>Persona, Rol, AsignacionRol,<br/>JerarquiaInstitucional, puertos</i>"]
        dAcad["Academico<br/><i>Facultad, Programa,<br/>Matricula, Docencia</i>"]
        dKudos["Kudos<br/><i>Kudo, Categoria, Adjunto,<br/>reglas de publicación, puertos</i>"]
        dInter["Interaccion<br/><i>Replica, RutaReplica,<br/>Calificacion, puertos</i>"]
        dMod["Moderacion<br/><i>Reporte, Veto,<br/>ReglasModeracion</i>"]
        dAudit["Auditoria<br/><i>EventoAuditoria,<br/>CadenaAuditoria, puerto</i>"]
        dComun["Comun<br/><i>Paginacion, Cursor</i>"]
    end

    subgraph app["UcoKudos.Application"]
        direction TB
        aComun["Comun<br/><i>ICasoUso, IUnidadTrabajo,<br/>IBandejaSalida</i>"]
        aMod["Un paquete por módulo<br/><i>Identidad, Academico, Kudos,<br/>Interaccion, Moderacion</i><br/><br/>y dentro de cada uno:<br/>CasosUso/<br/>Entrada/{Puertos, Adaptadores, Dto}<br/>Salida/Puertos"]
        aNotif["Notificaciones<br/><i>GatewayNotificaciones,<br/>CatalogoMensajes,<br/>puertos de los 3 canales</i>"]
        aLect["Lectura<br/><i>IConsultasMuro,<br/>IConsultasHilo, ICache</i>"]
    end

    subgraph infra["UcoKudos.Infrastructure"]
        direction TB
        iPers["Persistencia<br/><i>DbContext, Configuraciones/,<br/>Migraciones/, Repositorios/,<br/>UnidadTrabajo, Outbox/</i>"]
        iNotif["Notificaciones<br/><i>ProcesoNotificaciones,<br/>EnviadorCorreoSmtp,<br/>EnviadorWebPush, AvisosSse</i>"]
        iWapp["Canal<br/><i>WhatsAppMetaAdapter,<br/>WhatsAppSimuladoAdapter,<br/>FormatoPlantilla</i>"]
        iObj["Objetos<br/><i>AlmacenObjetosS3</i>"]
        iLect["Lectura<br/><i>ConsultasMuroSql,<br/>CacheMemoria</i>"]
        iObs["Observabilidad<br/><i>Instrumentacion,<br/>PropagacionTraza</i>"]
    end

    subgraph iface["UcoKudos.Api"]
        direction TB
        xEnd["Endpoints<br/><i>uno por módulo</i>"]
        xPlat["Plataforma<br/><i>ManejadorExcepciones,<br/>Salud, Autorizacion</i>"]
        xSse["Avisos<br/><i>canal SSE</i>"]
    end

    app --> dom
    infra --> app
    iface --> app

    classDef cDom fill:#f5f0ff,stroke:#6b3fa0,color:#1a1a1a
    classDef cApp fill:#eef6ff,stroke:#1168bd,color:#1a1a1a
    classDef cInfra fill:#fff6ee,stroke:#b5651d,color:#1a1a1a
    classDef cIface fill:#eefaf0,stroke:#2d7a3e,color:#1a1a1a
    classDef paq fill:#ffffff,stroke:#888888,color:#1a1a1a
    class dom cDom
    class app cApp
    class infra cInfra
    class iface cIface
    class dTrans,dIdent,dAcad,dKudos,dInter,dMod,dAudit,dComun,aComun,aMod,aNotif,aLect,iPers,iNotif,iWapp,iObj,iLect,iObs,xEnd,xPlat,xSse paq
```

## 3. La estructura de un módulo de aplicación

Todos los módulos de `UcoKudos.Application` siguen la misma forma. Es el estilo de puertos y adaptadores acordado con el docente en el curso:

```text
Kudos/
├── CasosUso/
│   ├── PublicarKudoCasoUso.cs
│   ├── ConsultarMuroCasoUso.cs
│   └── CargarAdjuntoCasoUso.cs
├── Entrada/
│   ├── Puertos/
│   │   └── IKudosPuertoEntrada.cs      ← lo que la API puede llamar
│   ├── Adaptadores/
│   │   └── KudosAdaptadorEntrada.cs    ← implementa el puerto llamando a los casos de uso
│   └── Dto/
│       ├── PublicarKudoDto.cs
│       └── KudoDto.cs
└── Salida/
    └── Puertos/
        └── IAlmacenAdjuntos.cs         ← lo que el módulo necesita de afuera
```

Por qué este reparto:

- **`Entrada/Puertos` es el contrato hacia la API.** Un endpoint depende de la interfaz, nunca de un caso de uso concreto. Eso permite que la regla 4 de ADR-0001 sea verificable.
- **`Entrada/Adaptadores`** reúne varios casos de uso detrás de un puerto, así que la API tiene una sola dependencia por módulo en lugar de una por operación.
- **`Salida/Puertos`** declara lo que el módulo necesita. Está **en el módulo que lo necesita**, no en una carpeta común de interfaces: así se ve de un vistazo de qué depende cada módulo.
- **Los DTO viven aquí, no en el dominio.** Una entidad nunca cruza hacia la API.

## 4. Reglas de dependencia, y quién las hace cumplir

| Regla de ADR-0001 | Quién la impone |
|---|---|
| 1. El dominio no depende de ninguna otra capa | El compilador (el proyecto no tiene referencias) y una prueba que lo guarda |
| 2. La aplicación depende solo del dominio | El compilador y una prueba que lo guarda |
| 3. La infraestructura no conoce la capa de interfaces | El compilador y una prueba que lo guarda |
| 4. La API solo usa puertos de entrada y DTO | **Solo la prueba de arquitectura**, porque la referencia a la infraestructura es legítima en la raíz de composición |
| 5. El núcleo no referencia tecnología de persistencia | Una prueba que inspecciona los ensamblados referenciados |
| Ningún módulo usa la implementación de un caso de uso de otro módulo | La prueba de arquitectura, cuando haya más de un módulo |

La regla 4 se comprobó provocando a propósito una violación: se añadió un uso de la infraestructura fuera de la raíz de composición, falló solo esa prueba y volvió a verde al retirarlo. Una regla que nunca ha fallado no está verificada.

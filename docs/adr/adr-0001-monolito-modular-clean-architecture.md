# ADR-0001: construir UCO Kudos como monolito modular con Clean Architecture

## Metadatos

| Campo | Valor |
|---|---|
| ID | ADR-0001 |
| Fecha | 2026-11-08 |
| Estado | **Accepted** |
| Participantes | Jhonny Estiven Benítez Caro |
| Curso | Ingeniería del Software II — UCO, semestre 2-2026 |
| Profesor evaluador | Wider Farid Sánchez Garzón |
| Building blocks | ABB de estructura de la aplicación |

## Contexto

UCO Kudos debe sostener una jerarquía de siete roles, un flujo de aprobaciones, un muro de lectura intensiva (DRV-002) y una publicación que dispara cuatro efectos externos (DRV-003). El enunciado exige evidenciar Clean Architecture, SOLID y patrones de diseño (RT-06), y que el autor pueda explicar y sustentar cada decisión (RN-01).

El plazo es de cinco días (RN-02) y el presupuesto de infraestructura es cero (RT-07). No hay ningún driver que exija escalar o desplegar módulos por separado: la plataforma es de una sola universidad, con un volumen de usuarios acotado y una sola base de datos.

## Decisión

Construir la plataforma como un **monolito modular** con **Clean Architecture** en cuatro capas y dependencias dirigidas hacia el dominio:

```text
Interfaces (API HTTP)  ──┐
Infraestructura ─────────┤──→ Aplicación ──→ Dominio
                         │
   (adaptadores de salida)
```

Reglas arquitectónicas, verificadas de forma automatizada:

1. **Dominio** no referencia ninguna otra capa ni ninguna biblioteca de infraestructura. Contiene entidades con invariantes validadas en el constructor, objetos de valor, servicios de dominio y los puertos de repositorio.
2. **Aplicación** contiene los casos de uso y los puertos. Depende solo del dominio. Organización por módulo y, dentro de cada módulo, `CasosUso/`, `Entrada/{Puertos,Adaptadores,Dto}` y `Salida/Puertos`.
3. **Infraestructura** implementa los puertos de salida: persistencia, correo, WhatsApp, push, almacenamiento de objetos. Ningún otro módulo la referencia.
4. **Interfaces** (la API) solo conoce puertos de entrada y DTO. Nunca entidades de dominio ni implementaciones de infraestructura.
5. Un módulo **no** referencia la implementación de un caso de uso de otro módulo: solo su puerto de entrada.

Módulos previstos: Identidad, Estructura Académica, Categorías, Kudos, Interacción (réplicas y calificaciones), Moderación, Notificaciones, Auditoría y Adjuntos.

El estilo de puertos y adaptadores con nombres en español es el acordado con el docente en el curso y ya sustentado por el autor en un proyecto anterior.

## Alternativas consideradas

- **A. Microservicios** — descartada: ningún driver exige escalado ni despliegue diferenciado por módulo, y en cinco días la coordinación distribuida consumiría el tiempo de la funcionalidad obligatoria.
- **B. Monolito en capas sin inversión de dependencias** — descartada: el dominio terminaría dependiendo del ORM y de los SDK de los proveedores, lo que contradice RT-06 y rompe QA-MANT-01.
- **C. Serverless por función** — descartada: dispersa el dominio, complica la transacción que publica el Kudo y añade arranques en frío que castigan DRV-001.

### Comparación por atributo de calidad

| Criterio | Monolito modular con Clean Architecture (elegida) | A. Microservicios | B. Capas sin inversión | C. Serverless |
|---|---|---|---|---|
| Consistencia transaccional (QA-CONF-01) | Cumple — una transacción local cubre Kudo, auditoría y evento | No cumple — exige consistencia eventual y compensación | Cumple | No cumple |
| Testabilidad | Cumple — el dominio se prueba sin infraestructura | Parcial — exige contratos y dobles entre servicios | No cumple — hay que levantar la base para probar reglas | Parcial |
| Cambio de proveedor (QA-MANT-01) | Cumple — adaptador nuevo detrás del puerto | Cumple | No cumple — el proveedor se filtra al núcleo | Parcial |
| Rendimiento de lectura (QA-REND-01) | Cumple — sin salto de red entre módulos | Parcial — latencia entre servicios | Cumple | Parcial — arranque en frío |
| Esfuerzo en 5 días (RN-02) | Cumple | No cumple | Cumple | No cumple |
| Elasticidad (QA-ELAS-01) | Parcial — escala la instancia completa | Cumple, sin driver que lo pida | Parcial | Cumple |

**Lectura:** ninguna alternativa domina en todo, pero el monolito modular con Clean Architecture es la única que cumple a la vez la consistencia que exige la funcionalidad obligatoria, la mantenibilidad que exige QA-MANT-01 y el plazo de RN-02. Extraer un servicio queda como evolución si aparece un driver real.

## Consecuencias

**Positivas**

- Un solo artefacto que desplegar, diagnosticar y probar de punta a punta.
- El dominio se prueba sin base de datos ni red, lo que sostiene la base de la pirámide de pruebas.
- Los proveedores externos quedan detrás de puertos: cambiar de uno a otro no toca el núcleo.
- Las reglas de dependencia son verificables con pruebas, así que Clean Architecture se evidencia con evidencia ejecutable y no con una afirmación en el documento.

**Negativas**

- El ciclo de despliegue es compartido: un cambio en Moderación obliga a desplegar todo.
- Un fallo de proceso afecta a todos los módulos.
- Exige disciplina: sin las pruebas de arquitectura, el monolito modular degenera en un monolito acoplado.

## Tácticas de Bass aplicadas

- **Reducir acoplamiento** — los puertos y adaptadores separan el dominio de la infraestructura y de los proveedores.
- **Aumentar cohesión** — cada módulo concentra responsabilidades relacionadas.
- **Localizar cambios** — las dependencias externas se encapsulan detrás de límites explícitos.
- **Usar un intermediario** — los casos de uso no conocen el canal ni el proveedor.

## Escenarios y requisitos relacionados

- `QA-MANT-01` — cambiar de proveedor sin tocar el dominio.
- `QA-CONF-01` — una transacción local cubre el cambio de estado y su evento.
- `QA-REND-01` — las lecturas no atraviesan lógica que no les corresponde.
- `RF-07` y todos los demás requisitos funcionales se implementan como casos de uso de esta estructura.
- `RT-06`, `RN-01`, `RN-02`.

## ADRs relacionados

- **ADR-0002** — el lenguaje y el runtime que materializan estas capas.
- **ADR-0005** — la transacción y el Outbox que sostienen QA-CONF-01.
- **ADR-0006** — el gateway de notificaciones, ejemplo de puerto de salida con varios adaptadores.
- **ADR-0017** — las pruebas de arquitectura que hacen cumplir estas reglas.

## Notas de implementación

- Un proyecto por capa, para que la dirección de las dependencias la imponga el compilador y no la convención.
- Las reglas 1 a 5 se codifican como pruebas de arquitectura. Cada regla nueva se comprueba provocando a propósito una violación: una regla que nunca ha fallado no está verificada.
- Las entidades validan sus invariantes en el constructor, de modo que no exista una instancia inválida. La unicidad del Rector (QA-SEG-03) es una invariante del dominio más una restricción en la base, no una validación de la interfaz.
- El tiempo entra siempre como parámetro: el dominio nunca consulta el reloj del sistema, para que las reglas sean deterministas en las pruebas.

## Referencias

- Martin, R. C. *Clean Architecture: A Craftsman's Guide to Software Structure and Design.*
- Bass, L., Clements, P. y Kazman, R. *Software Architecture in Practice.*
- Cockburn, A. *Hexagonal Architecture (Ports and Adapters).*

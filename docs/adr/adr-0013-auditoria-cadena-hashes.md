# ADR-0013: auditoría persistente de solo inserción con cadena de hashes

## Metadatos

| Campo | Valor |
|---|---|
| ID | ADR-0013 |
| Fecha | 2026-11-08 |
| Estado | **Accepted** |
| Participantes | Jhonny Estiven Benítez Caro |
| Curso | Ingeniería del Software II — UCO, semestre 2-2026 |
| Profesor evaluador | Wider Farid Sánchez Garzón |
| Building blocks | ABB de Auditoría |

## Contexto

RF-17 exige que «todo el proceso deberá quedar auditado» en la publicación de un Kudo, y la auditoría es una de las evidencias obligatorias de la entrega. DRV-006 la agrupa con la observabilidad.

Conviene distinguir dos cosas que el enunciado menciona juntas y que son distintas:

- Los **logs y las trazas** (ADR-0014) sirven para diagnosticar. Son técnicos, voluminosos, se rotan y se pierden. Su público es quien opera el sistema.
- La **auditoría** sirve para rendir cuentas. Es un registro de negocio: quién hizo qué, cuándo y sobre qué. Debe conservarse, no debe poder alterarse y su público es la institución.

En una plataforma de reconocimientos institucionales la auditoría importa de verdad: un Kudo influye en la reputación de una persona, y vetar contenido (RF-12) o cambiarle el rol a alguien (RF-04) son actos con consecuencias. Si alguien con acceso a la base pudiera editar o borrar ese registro, no serviría para nada.

## Decisión

Una tabla de eventos de auditoría **de solo inserción**, encadenada con hashes, escrita **en la misma transacción** que el hecho que registra.

1. **Un esquema propio y una tabla propia**, separada del núcleo del negocio.
2. **Solo inserción, garantizado por el motor.** Un disparador rechaza toda actualización y todo borrado sobre la tabla. No es una convención del código: es una regla de la base que ni la propia aplicación puede saltarse.
3. **Cada evento registra:** quién (identificador interno del actor), qué acción, sobre qué entidad y qué identificador, cuándo, el identificador de correlación de la petición, y un cuerpo con los datos relevantes del cambio.
4. **Cadena de hashes.** Cada evento guarda el hash del evento anterior junto con el suyo. Alterar o eliminar un evento intermedio rompe la cadena y es detectable con una operación de verificación que recorre la secuencia. Esto convierte la inmutabilidad en algo **demostrable**, no solo declarado.
5. **En la misma transacción que el hecho.** Si el cambio se confirma, su auditoría también; si falla, tampoco queda auditoría de un cambio que no ocurrió (ADR-0005).
6. **Qué se audita:** publicar un Kudo y cada envío de notificación con su resultado (RF-07, RF-17), aprobar o rechazar solicitudes (RF-03), cambios de rol y de estado de cuenta (RF-04), administración de categorías (RF-06), veto y reporte de contenido (RF-11, RF-12), y carga y descarga de adjuntos (ADR-0012). También **los accesos rechazados por falta de permiso** (QA-SEG-01): un intento fallido es a menudo más informativo que uno exitoso.
7. **No se auditan datos sensibles ni valores completos cuando no hacen falta.** Al cambiar un correo se registra *qué campos cambiaron*, no el valor anterior y el nuevo. La auditoría es un registro permanente: meter en ella datos personales innecesarios es crear un problema perpetuo.
8. **Consulta de la auditoría restringida** a los roles que pueden rendir cuentas, y esa consulta también se audita.

## Alternativas consideradas

| Criterio | **Tabla de solo inserción con cadena de hashes (elegida)** | A. Solo logs de la aplicación | B. Tabla de auditoría editable | C. Historial automático del ORM | D. Servicio externo de auditoría |
|---|---|---|---|---|---|
| Inalterable de forma demostrable | Cumple — cadena verificable | **No cumple** — un archivo se edita o se rota | No cumple | No cumple | Cumple |
| Consultable como dato de negocio | Cumple | No cumple — hay que rastrear texto | Cumple | Parcial | Cumple |
| Atómica con el hecho auditado | Cumple — misma transacción | **No cumple** — se escribe aparte y puede perderse | Cumple | Cumple | No cumple — llamada de red fuera de la transacción |
| Registra la intención del negocio | Cumple — acción con nombre propio | Parcial | Cumple | **No cumple** — registra columnas cambiadas, no actos |
| Costo y esfuerzo (RT-07, RN-02) | Cumple | Cumple | Cumple | Cumple | No cumple |
| Sirve como evidencia de la entrega | Cumple | Parcial | Parcial | Parcial | Cumple |

- **A. Solo logs** — descartada: los logs se rotan, se pierden y se editan, y están escritos para diagnosticar, no para rendir cuentas. «Está en los logs» no es una auditoría.
- **B. Tabla editable** — descartada: cubre la forma pero no el fondo. Una auditoría que se puede modificar no prueba nada, y es trivial de rechazar en una sustentación.
- **C. Historial automático del ORM** — descartada: registra que una columna pasó de un valor a otro, pero no **qué acto de negocio** ocurrió. «El campo estado cambió de 2 a 3» no es «el Rector vetó esta réplica por este motivo». Además acopla la auditoría al mecanismo de persistencia, contra ADR-0001.
- **D. Servicio externo** — descartada: añade costo y, sobre todo, una llamada de red **fuera** de la transacción, lo que reintroduce la ventana de pérdida que ADR-0005 cierra.

## Consecuencias

**Positivas**

- El registro es inalterable de una forma que se puede **demostrar** ejecutando la verificación de la cadena, lo que es una evidencia sólida para la entrega y un buen momento de la sustentación.
- Nunca hay un cambio confirmado sin su auditoría, ni una auditoría de un cambio que no ocurrió.
- El identificador de correlación une la auditoría con las trazas de ADR-0014: se pasa de «qué se hizo» a «cómo se ejecutó» con un solo identificador (QA-OBS-01).
- Los intentos rechazados quedan registrados, que es lo que permite detectar un abuso.

**Negativas**

- **La tabla solo crece.** Con el volumen de una universidad no es un problema a corto plazo, pero hay que definir una política de retención y archivado, y no se puede resolver borrando.
- La cadena de hashes impone un **orden**: dos eventos simultáneos no pueden calcular su hash en paralelo sin coordinarse. Se resuelve serializando el cálculo con un candado por cadena, lo que es un punto de contención conocido y acotado.
- El hash debe calcularse sobre una representación estable de los datos. Si el formato de serialización reordena claves o la base trunca la precisión de la fecha, la verificación fallaría al releer aunque nada se haya alterado. Es un detalle que hay que acertar.
- Encadenar y verificar es más código que una tabla simple.

## Tácticas de Bass aplicadas

- **Auditar** — registro de actos de negocio con su actor.
- **Mantener la integridad de los datos** — cadena de hashes verificable.
- **Mantener la consistencia de los datos** — misma transacción que el hecho.
- **Detectar la intrusión** — los accesos rechazados quedan registrados.
- **Limitar el acceso** — la consulta de la auditoría está restringida y se audita.

## Escenarios y requisitos relacionados

- `QA-OBS-01` — reconstruir una publicación concreta con un solo identificador.
- `QA-SEG-01` — el intento rechazado queda auditado.
- `RF-17` — el requisito que este ADR responde.
- `RF-03`, `RF-04`, `RF-06`, `RF-07`, `RF-11`, `RF-12`, `DRV-006`.

## ADRs relacionados

- **ADR-0005** — la transacción que confirma cambio, auditoría y evento juntos.
- **ADR-0014** — las trazas y los logs, que son lo otro y se unen por el identificador de correlación.
- **ADR-0012** — la auditoría de cargas y descargas de adjuntos.
- **ADR-0003** — el disparador que impide actualizar y borrar.

## Notas de implementación

- La fecha se trunca a una precisión fija y el cuerpo se serializa de forma canónica, con las claves ordenadas, **antes** de calcular el hash. Sin eso, el valor que se guarda y el que se recalcula al verificar no coinciden, porque la base y el serializador no preservan ni la precisión ni el orden.
- La operación de verificación de la cadena se expone como una consulta de la plataforma, no como un proceso oculto: es lo que se ejecuta en vivo durante la sustentación para demostrar la inmutabilidad.
- Las acciones se nombran con un vocabulario cerrado y estable (`kudo.publicar`, `contenido.vetar`, `usuario.cambiar_rol`), porque un nombre de acción escrito a mano en cada sitio acaba teniendo cuatro variantes.
- El identificador de correlación se genera en el borde de entrada de la petición y viaja por todo el flujo, incluidos los eventos de la bandeja, para que la auditoría de una notificación se pueda unir con la de la publicación que la originó.

## Referencias

- OWASP. *Logging and Monitoring Cheat Sheet* y *Top 10 — A09 Security Logging and Monitoring Failures.*
- NIST. *SP 800-92, Guide to Computer Security Log Management.*
- Bass, L., Clements, P. y Kazman, R. *Software Architecture in Practice* — tácticas de seguridad.

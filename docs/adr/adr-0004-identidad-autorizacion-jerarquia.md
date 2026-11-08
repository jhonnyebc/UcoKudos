# ADR-0004: identidad delegada por OIDC y autorización por jerarquía institucional propia

## Metadatos

| Campo | Valor |
|---|---|
| ID | ADR-0004 |
| Fecha | 2026-11-08 |
| Estado | **Accepted** |
| Participantes | Jhonny Estiven Benítez Caro |
| Curso | Ingeniería del Software II — UCO, semestre 2-2026 |
| Profesor evaluador | Wider Farid Sánchez Garzón |
| Building blocks | ABB de Gestión de Identidad, Autenticación y Autorización |

## Contexto

La plataforma tiene siete roles en una jerarquía estricta (Administrador, Rector, Coordinador Académico, Coordinador de Facultad, Coordinador de Programa, Docente, Estudiante) con reglas que no son un simple mapa de permisos:

- **Exclusividad:** una persona tiene un único rol activo (RN-04).
- **Unicidad:** un Rector y un Coordinador Académico activos en la universidad; uno por facultad y uno por programa (RF-04).
- **Aprobaciones en cadena:** cada solicitud la aprueba el nivel inmediatamente superior (RF-03).
- **Activación compuesta:** la cuenta queda activa solo con solicitud aprobada, correo verificado y celular verificado (RF-02).
- **Autorización por ámbito:** un Coordinador de Programa administra a los estudiantes y docentes **de sus programas**, no a todos. Vetar contenido solo lo pueden el Rector y el Coordinador Académico (RF-12), y administrar categorías solo tres roles (RF-06).

El presupuesto es cero (RT-07) y el plazo cinco días (RN-02). No construir un sistema de contraseñas propio es además una decisión de seguridad (DRV-005): es el lugar donde más fácil se introducen vulnerabilidades.

## Decisión

Separar **autenticación** de **autorización**:

1. **Autenticación delegada a un proveedor de identidad externo por OIDC/OAuth2**, en su capa gratuita. La API actúa como *resource server*: valida el token JWT (emisor, audiencia y firma) y no gestiona contraseñas. El proveedor se encarga del hash, la política de contraseñas, el bloqueo por intentos y la verificación del correo.
2. **Autorización en la plataforma, no en el token.** El token aporta la identidad (el correo institucional); el rol, el ámbito y el estado de la cuenta se resuelven **en cada petición** contra la base de datos propia. El token nunca lleva el rol.
3. **La jerarquía es un modelo de dominio, no una lista de permisos.** Existe una entidad de rol con su nivel y su ámbito (universidad, facultad o programa), y un servicio de dominio responde las dos preguntas del enunciado: «¿quién aprueba a quién?» y «¿sobre quién manda este usuario?». Las reglas de RF-03, RF-04, RF-06 y RF-12 viven ahí, probadas con pruebas unitarias sin base de datos.
4. **La exclusividad y la unicidad son invariantes**, sostenidas en el dominio y además por índices únicos parciales (ADR-0003).
5. **Verificación del celular** con un código de un solo uso enviado por WhatsApp, reutilizando el mismo adaptador de salida de ADR-0007. El correo lo verifica el proveedor de identidad.
6. **El estado de la cuenta se consulta en cada petición, sin caché.** Si se desactiva a alguien, deja de tener acceso de inmediato y no cuando expire su token.

## Alternativas consideradas

| Criterio | **OIDC externo + autorización propia (elegida)** | A. Roles en los claims del token | B. Autenticación propia con contraseñas | C. Keycloak autoalojado |
|---|---|---|---|---|
| Riesgo de seguridad (DRV-005) | Cumple — no se almacenan contraseñas | Cumple | **No cumple** — es el componente más fácil de equivocar | Cumple |
| Revocación inmediata | Cumple — se consulta el estado en cada petición | No cumple — el rol viaja en el token hasta que expira | Cumple | Parcial |
| Ámbito por facultad y programa (RF-03) | Cumple — es una consulta relacional | No cumple — no cabe de forma mantenible en un claim | Cumple | Parcial — exige modelar la jerarquía en el proveedor |
| Costo (RT-07) | Cumple — capa gratuita | Cumple | Cumple | Parcial — consume memoria de la única máquina |
| Esfuerzo (RN-02) | Cumple | Cumple | No cumple | No cumple — configurar el proveedor lleva tiempo |
| Las reglas del enunciado se prueban sin infraestructura | Cumple — son dominio | No cumple | Parcial | No cumple — quedan en configuración del proveedor |

- **A. Roles en los claims** — descartada: es lo habitual, pero aquí falla por dos razones. La jerarquía con ámbito por facultad y programa no cabe de forma mantenible en un token, y un rol en el token no se puede revocar antes de que expire, lo que choca con RF-02 y con la desactivación de cuentas.
- **B. Autenticación propia** — descartada por seguridad y por plazo.
- **C. Keycloak autoalojado** — descartada: técnicamente es la más completa, pero consume memoria de la única máquina del despliegue y su configuración compite con el tiempo de la funcionalidad obligatoria. Además, modelar la jerarquía dentro del proveedor la sacaría del dominio, donde es el corazón del enunciado.

## Consecuencias

**Positivas**

- La plataforma no custodia contraseñas: desaparece toda una clase de vulnerabilidades.
- La jerarquía institucional, que es la parte más característica del enunciado, queda en el dominio y se prueba con pruebas unitarias rápidas.
- Desactivar una cuenta surte efecto de inmediato.
- Cambiar de proveedor de identidad es cambiar un adaptador: la autorización no depende de él.

**Negativas**

- Resolver el usuario interno en cada petición es una consulta adicional por llamada. Se mitiga con un índice por correo y con que es una lectura de una sola fila.
- Dependencia de un servicio externo para iniciar sesión: si está caído, nadie entra. Es aceptable frente a la alternativa de custodiar contraseñas, y el enunciado no fija un objetivo de disponibilidad para el inicio de sesión.
- Hay dos fuentes de verdad sobre la persona (el proveedor tiene el correo, la plataforma tiene el rol), y hay que mantenerlas de acuerdo al cambiar un correo.

## Tácticas de Bass aplicadas

- **Autenticar actores** y **autorizar actores** — separadas en dos componentes distintos.
- **Limitar el acceso** — el ámbito de cada rol restringe sobre quién puede actuar.
- **Revocar el acceso** — el estado se consulta en cada petición.
- **Auditar** — todo intento rechazado queda registrado (ADR-0013).

## Escenarios y requisitos relacionados

- `QA-SEG-01` — un Coordinador de Programa que intenta crear una categoría recibe 403 y queda auditado.
- `QA-SEG-03` — el segundo Rector activo se rechaza por invariante.
- `RF-01`, `RF-02`, `RF-03`, `RF-04`, `RF-06`, `RF-12`.
- `DRV-005`, `RT-07`, `RN-02`, `RN-04`.

## ADRs relacionados

- **ADR-0003** — los índices únicos que sostienen la unicidad.
- **ADR-0007** — el adaptador de WhatsApp que se reutiliza para verificar el celular.
- **ADR-0013** — la auditoría de los accesos rechazados.
- **ADR-0018** — dónde viven las credenciales del proveedor.

## Notas de implementación

- Exigir token en el grupo de rutas de la API, no con una política global de reserva: una política global convierte las rutas inexistentes en 401 y oculta los errores de enrutamiento detrás de un problema de autenticación.
- El servicio de dominio de la jerarquía se prueba con una tabla de casos que recorre las seis filas del flujo de aprobaciones del enunciado, incluida la del Administrador que se aprueba a sí mismo.
- La verificación del celular limita los intentos y expira el código: un código de un solo uso sin límite de intentos es un código adivinable.
- El correo institucional se normaliza antes de comparar, y el celular se normaliza a formato internacional E.164 antes de guardarlo, porque es la forma en que lo exige la API de WhatsApp.

## Referencias

- Hardt, D. *The OAuth 2.0 Authorization Framework*, RFC 6749.
- OpenID Foundation. *OpenID Connect Core 1.0.*
- OWASP. *Top 10 — A01 Broken Access Control, A07 Identification and Authentication Failures.*

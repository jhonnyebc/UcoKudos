# ADR-0018: configuración por entorno y secretos fuera del repositorio, según The Twelve-Factor App

## Metadatos

| Campo | Valor |
|---|---|
| ID | ADR-0018 |
| Fecha | 2026-11-08 |
| Estado | **Accepted** |
| Participantes | Jhonny Estiven Benítez Caro |
| Curso | Ingeniería del Software II — UCO, semestre 2-2026 |
| Profesor evaluador | Wider Farid Sánchez Garzón |
| Building blocks | ABB de Gestión de Configuración y de Seguridad |

## Contexto

La plataforma maneja credenciales de cuatro integraciones: el proveedor de identidad (ADR-0004), el token de WhatsApp (ADR-0007), las credenciales SMTP (ADR-0008) y las del almacenamiento de objetos (ADR-0012), más la contraseña de la base y la clave privada VAPID (ADR-0009).

DRV-005 incluye explícitamente la gestión de secretos, y el enunciado exige una **matriz de evaluación de cumplimiento de los doce factores**, analizados, evaluados y justificados (RT-06). El factor III, configuración, es precisamente el que gobierna este tema, así que este ADR fija el mecanismo y el documento de arquitectura desarrolla la matriz completa.

El riesgo es concreto y frecuente: un secreto en el repositorio. Una vez enviado, borrarlo no lo elimina del historial, y si el repositorio es público para que Sonar lo analice sin costo, el secreto es público.

## Decisión

**Configuración en el entorno, secretos fuera del repositorio y del artefacto**, con los doce factores como marco.

1. **El artefacto es único e independiente del entorno** (factor V, construir y ejecutar separados). La misma imagen corre en local y en producción; lo que cambia es la configuración inyectada.
2. **Toda la configuración entra por variables de entorno** (factor III). Lo no secreto puede tener un valor por omisión en el archivo de configuración versionado; lo secreto **no tiene valor por omisión**: si falta, la aplicación **no arranca**. Un valor por omisión para un secreto es una contraseña en el repositorio con otro nombre, y además oculta el error de configuración hasta que algo falla en producción.
3. **Jerarquía de precedencia declarada**: línea de comandos, variables de entorno, almacén local de secretos del desarrollador y archivo de configuración versionado, en ese orden. La sorpresa típica es no saber qué fuente gana; queda escrita.
4. **Tres lugares para los secretos, uno por entorno:**
   - **Desarrollo local:** el almacén de secretos del entorno de desarrollo del autor, fuera del árbol del proyecto, de modo que ningún secreto esté en una carpeta que git pueda ver.
   - **Pipeline:** los secretos del repositorio, inyectados como variables de entorno solo en los trabajos que los necesitan (ADR-0017).
   - **Producción:** un archivo de entorno en la máquina, con permisos restringidos al usuario del servicio, **nunca versionado**. Se documenta que el equivalente en una organización con presupuesto es un gestor de secretos dedicado, y que el cambio sería un adaptador de configuración.
5. **Un archivo de ejemplo versionado** que lista todas las variables con valores de muestra y una explicación de cada una. Es la contraparte del punto 2: si la aplicación no arranca sin un secreto, hace falta un documento que diga cuáles son.
6. **Escaneo de secretos en dos lugares** (ADR-0017): un gancho local antes del envío y una puerta en el pipeline. El gancho local es el que importa, porque evita que el secreto llegue al remoto.
7. **Rotación prevista.** Cada secreto tiene documentado cómo se rota y qué hay que reiniciar. Un secreto que nadie sabe rotar es un secreto que no se rota nunca.
8. **Nada secreto en el cliente web.** Todo lo que se compila en el cliente es público: la clave pública VAPID y el identificador del cliente de identidad pueden ir; nada más. Una regla de análisis estático lo impide activamente (ADR-0015).
9. **Los secretos nunca se registran** en logs ni en auditoría, y las excepciones de configuración mencionan el nombre de la variable que falta, nunca su valor.

## Alternativas consideradas

| Criterio | **Variables de entorno sin valor por omisión (elegida)** | A. Archivo de configuración versionado | B. Gestor de secretos gestionado | C. Secretos cifrados en el repositorio |
|---|---|---|---|---|
| Ningún secreto en el repositorio (DRV-005) | Cumple | **No cumple** | Cumple | Parcial — el cifrado protege, pero la clave hay que custodiarla igual |
| Un artefacto para todos los entornos (factor V) | Cumple | No cumple — un archivo por entorno | Cumple | Cumple |
| Rotación sin desplegar código | Cumple | No cumple | Cumple | No cumple |
| Costo (RT-07) | Cumple | Cumple | **No cumple** | Cumple |
| Esfuerzo (RN-02) | Cumple | Cumple | Parcial | Parcial |
| Auditoría de acceso a los secretos | No cumple | No cumple | Cumple | No cumple |
| Alineado con The Twelve-Factor App (RT-06) | Cumple | No cumple | Cumple | Parcial |

- **A. Archivo de configuración versionado** — descartada sin matices: es el origen del problema que este ADR evita. Además obliga a un artefacto por entorno, contra el factor V.
- **B. Gestor de secretos gestionado** — descartada **hoy** por costo, no por mérito: es la opción correcta en una organización, y la única que da auditoría de acceso a los secretos. La decisión 4 deja el camino abierto como un adaptador de configuración.
- **C. Secretos cifrados en el repositorio** — descartada: desplaza el problema a custodiar la clave de descifrado, y si esa clave se filtra, todo el historial de secretos queda expuesto de una vez.

## Consecuencias

**Positivas**

- La misma imagen se promueve entre entornos sin recompilar.
- Un secreto ausente se manifiesta al arrancar, con un mensaje que nombra la variable, en lugar de fallar más tarde con un error incomprensible.
- Rotar una credencial no exige desplegar código.
- El doble escaneo hace improbable que un secreto llegue al repositorio.
- El marco de los doce factores queda aplicado y no solo citado, lo que alimenta la matriz que exige el enunciado.

**Negativas**

- **No hay auditoría de quién accedió a cada secreto**, que es lo que un gestor dedicado aportaría. Es la limitación consciente de esta decisión.
- El archivo de entorno en la máquina es un punto sensible: quien entre al servidor lo lee. Se mitiga con permisos del sistema de archivos y con la superficie expuesta mínima de ADR-0016.
- Los secretos están en tres lugares distintos según el entorno, y mantenerlos de acuerdo es trabajo manual. El archivo de ejemplo lo reduce, no lo elimina.
- Negarse a arrancar sin un secreto es molesto en el primer arranque de un entorno nuevo. Es el comportamiento correcto.

## Tácticas de Bass aplicadas

- **Gestionar la configuración** — una sola fuente por entorno, con precedencia declarada.
- **Limitar el acceso** — permisos del sistema de archivos y secretos solo en los trabajos que los usan.
- **Resistir ataques** — escaneo antes del envío y en el pipeline.
- **Detectar fallos pronto** — fallo al arrancar en lugar de fallo en ejecución.

## Escenarios y requisitos relacionados

- `QA-SEG-02` — las credenciales del almacenamiento nunca llegan al cliente.
- `QA-MANT-01` — cambiar de proveedor es cambiar configuración.
- `DRV-005`, `RT-06`, `RT-07`.

## ADRs relacionados

- **ADR-0004**, **ADR-0007**, **ADR-0008**, **ADR-0009**, **ADR-0012** — los dueños de cada secreto.
- **ADR-0016** — la máquina donde vive el archivo de entorno.
- **ADR-0017** — el escaneo de secretos y la inyección en el pipeline.

## Notas de implementación

- La precedencia de las fuentes de configuración no es intuitiva en la mayoría de los marcos, y añadir una fuente «por debajo de las variables de entorno» exige insertarla en el lugar correcto. Conviene probarlo con una prueba automatizada que use el constructor real de la aplicación, no uno armado a mano: es la única forma de detectar que el orden quedó mal.
- Una clave de configuración con valor nulo puede **sobrescribir** el valor por omisión de una opción al enlazarla, dejándola vacía en lugar de con su valor por defecto. Es un error silencioso: no se añaden claves con valor nulo.
- Los secretos y el aprovisionamiento de la máquina los ejecuta el autor desde su propia terminal. Un proceso lanzado por una aplicación empaquetada puede escribir en una copia aislada del perfil del usuario, y el secreto acaba en un lugar que luego se borra: ya ocurrió en un proyecto anterior y costó recrear un entorno.
- Cada secreto nuevo se agrega al archivo de ejemplo en el mismo cambio que lo introduce, o nadie podrá levantar el entorno después.

## Referencias

- Wiggins, A. *The Twelve-Factor App.* 12factor.net.
- OWASP. *Secrets Management Cheat Sheet* y *Top 10 — A02 Cryptographic Failures, A05 Security Misconfiguration.*

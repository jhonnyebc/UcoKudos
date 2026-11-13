# ADR-0017: DevSecOps con quality gates que bloquean, y pirámide de pruebas automatizada

## Metadatos

| Campo | Valor |
|---|---|
| ID | ADR-0017 |
| Fecha | 2026-11-08 |
| Estado | **Accepted** |
| Participantes | Jhonny Estiven Benítez Caro |
| Curso | Ingeniería del Software II — UCO, semestre 2-2026 |
| Profesor evaluador | Wider Farid Sánchez Garzón |
| Building blocks | ABB de DevSecOps |

## Contexto

DRV-007 exige capacidades DevSecOps **desde la línea base**, y RT-03 hace obligatorios el análisis estático con Sonar y el escaneo de vulnerabilidades con Trivy. RT-04 exige un pipeline de integración continua y uno de despliegue automatizado, y el enunciado pide evidencia de ambos, de Sonar, de Trivy y de las pruebas.

La pirámide de pruebas también es obligatoria, con sus tres niveles: unitarias, de integración y de punta a punta.

La frase «desde la línea base» es la clave. Un escaneo ejecutado a mano la víspera de la entrega cumple la letra del requisito y no su propósito: encuentra cien hallazgos cuando ya no hay tiempo de arreglarlos. El valor de DevSecOps está en que cada cambio pase por las mismas puertas desde el primer commit.

El trabajo es individual (RN-01), así que no hay revisión por pares: el pipeline es el único revisor, lo que le da más peso, no menos.

## Decisión

Un **pipeline de integración continua que bloquea** en cada cambio, y un **pipeline de despliegue** que publica solo lo que pasó por él.

### Puertas del pipeline de integración

Todas **bloquean**: si una falla, el cambio no entra. Una puerta que solo informa es documentación, no una puerta.

| Puerta | Qué verifica | Por qué bloquea |
|---|---|---|
| Compilación sin advertencias | Las advertencias se tratan como errores | Una advertencia aceptada una vez se acepta siempre |
| Formato | Estilo uniforme, verificado automáticamente | Clean Code que no hay que discutir |
| **Pruebas unitarias con umbral de cobertura** | Dominio y Aplicación por encima de un umbral declarado de líneas y de ramas | La base de la pirámide; el umbral se mide donde está la lógica, no en el proyecto completo |
| **Pruebas de arquitectura** | Las cinco reglas de dependencia de ADR-0001 | Es lo que convierte Clean Architecture en una propiedad verificada |
| **Pruebas de integración** | Contra PostgreSQL y almacenamiento de objetos **reales**, en contenedores desechables | Un doble en memoria no reproduce índices parciales, bloqueos ni firmas |
| **Pruebas de punta a punta** | El flujo completo de RF-07 en un navegador | La cima de la pirámide |
| Front-end: tipos, lint, pruebas y presupuestos | Incluye accesibilidad y el peso de ADR-0015 | Mobile First medido |
| Contrato al día | Los tipos del cliente coinciden con el OpenAPI del servidor | Una ruptura de contrato falla al compilar |
| **Escaneo de secretos** | Historial y cambios, buscando credenciales | Un secreto filtrado no se arregla borrándolo: ya está en el historial |
| **Trivy: dependencias y configuración** | Vulnerabilidades altas y críticas con corrección disponible | RT-03 |
| **Trivy: imágenes de contenedor** | La imagen construida, antes de publicarla | Se escanea lo que se despliega, no el código que lo origina |
| **Sonar** | Análisis estático con la cobertura importada | RT-03 |

### Criterios de diseño del pipeline

1. **Las vulnerabilidades que bloquean son las altas y críticas con corrección disponible.** Bloquear por una vulnerabilidad sin parche disponible detiene el trabajo sin ofrecer salida; esas se reportan sin bloquear. Es la diferencia entre una puerta útil y una que se acaba desactivando.
2. **Las acciones externas se fijan por su identificador de commit**, no por una etiqueta de versión. Una etiqueta se puede mover a código distinto: es un vector de ataque de cadena de suministro real, con incidentes conocidos.
3. **Las dependencias se instalan sin ejecutar sus scripts de instalación**, por el mismo motivo.
4. **Se escanea la imagen, y se publica exactamente la imagen escaneada**, sin reconstruirla. Reconstruir después de escanear invalida el escaneo.
5. **Las imágenes se etiquetan con versión y commit**, nunca con una etiqueta móvil.
6. **El escaneo de secretos corre también antes del envío**, en un gancho local, porque detectar un secreto en el pipeline significa que ya está en el repositorio remoto.
7. **Las imágenes base son mínimas**, sin consola ni gestor de paquetes y con usuario sin privilegios: menos paquetes, menos hallazgos y menos que explotar.

### Pipeline de despliegue

Al integrar en la rama principal, y solo si todas las puertas pasaron: se publican las imágenes escaneadas en un registro, se aplican las migraciones y se reemplazan los contenedores en la máquina de ADR-0016, con una comprobación del punto de salud al final.

### Pirámide de pruebas

| Nivel | Qué prueba | Contra qué |
|---|---|---|
| **Unitarias** (base) | Invariantes del dominio, jerarquía de roles, reglas de las categorías, cálculo de la ruta de réplicas, casos de uso con puertos simulados | Nada externo: sin base de datos ni red |
| **Arquitectura** | Las reglas de dependencia de ADR-0001 | Los ensamblados compilados |
| **Integración** | Repositorios, consultas escritas a mano, índices únicos, el Outbox con su bloqueo, el guardado y la lectura de archivos en el almacenamiento real, la cadena de auditoría | PostgreSQL y almacenamiento reales en contenedores desechables |
| **Punta a punta** (cima) | Publicar un Kudo, adjuntar un archivo y comprobar que se registran los tres envíos y la auditoría, y los flujos de cuentas, interacción y moderación | La API completa **por HTTP** (no hay navegador de por medio; ver la pirámide de pruebas) |

Las tres pruebas que **no son negociables**, porque son la evidencia de los escenarios más delicados: que un reproceso no duplique notificaciones (QA-CONF-02), que un adjunto no se entregue sin sesión ni cuenta activa (QA-SEG-02) y que el segundo Rector activo sea imposible (QA-SEG-03).

## Alternativas consideradas

| Criterio | **Puertas que bloquean en cada cambio (elegida)** | A. Escaneos manuales antes de entregar | B. Puertas que solo informan | C. Pipeline solo con pruebas unitarias |
|---|---|---|---|---|
| Cumple «desde la línea base» (DRV-007) | Cumple | **No cumple** | Parcial | Parcial |
| Los hallazgos aparecen cuando son baratos | Cumple | No cumple — aparecen sin tiempo de arreglarlos | Parcial — se ignoran | Parcial |
| Evidencia de CI, CD, Sonar y Trivy | Cumple — generada en cada cambio | Parcial — una captura puntual | Cumple | No cumple |
| Verifica Clean Architecture (RT-06) | Cumple — pruebas de arquitectura | No cumple | Parcial | No cumple |
| Esfuerzo inicial | Parcial — montarlo cuesta un día | Cumple | Parcial | Cumple |
| Riesgo de llegar a la entrega con sorpresas | Bajo | **Alto** | Medio | Alto |

- **A. Escaneos manuales antes de entregar** — descartada: contradice DRV-007 de forma directa y concentra todo el riesgo en el peor momento.
- **B. Puertas que solo informan** — descartada: es el camino por el que se degradan todos los pipelines. Un hallazgo que no bloquea se posterga, y a las dos semanas hay cuarenta.
- **C. Solo pruebas unitarias** — descartada: dejaría sin verificar justo lo que más riesgo tiene aquí, que es el comportamiento contra PostgreSQL real (bloqueos, índices parciales) y contra el almacenamiento (firmas y expiración).

## Consecuencias

**Positivas**

- Cada cambio queda verificado por doce puertas, lo que compensa la ausencia de revisión por pares (RN-01).
- La evidencia que pide el enunciado se genera por sí sola en cada ejecución: no hay que fabricarla al final.
- Clean Architecture, Mobile First y la cobertura dejan de ser afirmaciones del documento y pasan a ser condiciones de la construcción.
- Los hallazgos de seguridad aparecen el día que se introducen.
- Las imágenes mínimas reducen de entrada la cantidad de hallazgos de Trivy.

**Negativas**

- **El pipeline es lento**: los contenedores desechables y el análisis estático tardan. Es el precio, y se mitiga paralelizando los trabajos independientes.
- Montarlo cuesta aproximadamente un día del plazo, aunque se parte de un pipeline ya construido (ADR-0002).
- Una dependencia con una vulnerabilidad crítica recién publicada puede bloquear el trabajo en un momento inoportuno. La regla de «con corrección disponible» lo limita, pero no lo elimina.
- Las pruebas de punta a punta son las más frágiles de la pirámide: hay que mantenerlas pocas y centradas en el flujo obligatorio.

## Tácticas de Bass aplicadas

- **Verificar y validar** — puertas automatizadas en cada cambio.
- **Detectar fallos pronto** — el defecto se encuentra donde es barato.
- **Gestionar la configuración** — imágenes inmutables con versión y commit.
- **Limitar la exposición** — imágenes mínimas con usuario sin privilegios.
- **Resistir ataques** — escaneo de secretos y acciones fijadas por commit.

## Escenarios y requisitos relacionados

- `QA-CONF-02`, `QA-SEG-02`, `QA-SEG-03` — los tres escenarios con prueba obligatoria.
- `QA-REND-01` — la prueba de carga que mide los percentiles.
- `DRV-007`, `RT-03`, `RT-04`, `RT-06`, `RN-01`.

## ADRs relacionados

- **ADR-0001** — las reglas que las pruebas de arquitectura hacen cumplir.
- **ADR-0002** — el pipeline del que se parte.
- **ADR-0016** — el destino del despliegue.
- **ADR-0018** — los secretos que el pipeline usa y que el escaneo busca.

## Notas de implementación

- Cada regla de arquitectura nueva se comprueba **provocando a propósito una violación**: una regla que nunca ha fallado no está verificada, y es fácil escribir una que no detecta nada.
- Las pruebas de integración deben estar marcadas de forma que el pipeline las seleccione: una clase sin la marca correcta no se ejecuta y nadie se da cuenta, porque el pipeline sigue en verde.
- La evidencia se recoge a medida que se genera, no al final: capturas de las ejecuciones, del tablero de Sonar y de los informes de Trivy quedan guardadas en la carpeta de entrega el mismo día.
- El proyecto en Sonar y su credencial se crean el primer día: un pipeline que falla por una credencial ausente desperdicia el tiempo de montaje.
- Hay que tener a mano el procedimiento de despliegue manual (ADR-0016) por si el pipeline falla el día de la entrega.

## Referencias

- Aqua Security. *Trivy documentation.*
- SonarSource. *SonarQube Cloud documentation.*
- Cohn, M. *Succeeding with Agile* — la pirámide de pruebas.
- OWASP. *CI/CD Security Cheat Sheet* y *Top 10 CI/CD Security Risks.*

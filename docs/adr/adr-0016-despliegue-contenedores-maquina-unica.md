# ADR-0016: despliegue en contenedores sobre una máquina de capa gratuita permanente

## Metadatos

| Campo | Valor |
|---|---|
| ID | ADR-0016 |
| Fecha | 2026-11-08 |
| Estado | **Accepted** (rev. 2026-11-11) |
| Participantes | Jhonny Estiven Benítez Caro |
| Curso | Ingeniería del Software II — UCO, semestre 2-2026 |
| Profesor evaluador | Wider Farid Sánchez Garzón |
| Building blocks | SBB de infraestructura de ejecución |

## Contexto

RT-05 exige que la solución esté publicada en una URL pública que el docente pueda acceder y usar para validar el flujo de publicación de Kudos. RT-04 exige un pipeline de despliegue automatizado. El presupuesto es cero (RT-07).

Hay un requisito implícito que condiciona todo: **la plataforma tiene que estar en pie y responder rápido en el momento exacto de la sustentación**. Un servicio que tarda un minuto en despertar o que se apagó por agotar un crédito arruina la demostración sin que nada falle en el código.

La solución necesita varias piezas corriendo a la vez: la API, PostgreSQL, el almacenamiento de objetos (ADR-0012), el recolector de observabilidad (ADR-0014) y un punto de entrada con HTTPS. Eso significa que no basta con un servicio que ejecute un único contenedor.

Las opciones gratuitas cambian con frecuencia, y varias de las que eran estándar hace unos años ya no tienen capa gratuita permanente.

## Decisión

Desplegar sobre **una máquina virtual de una capa gratuita permanente**, con todas las piezas en **contenedores orquestados por un único archivo de composición**, detrás de un **proxy inverso que gestiona HTTPS automáticamente**.

1. **Una máquina virtual, no una plataforma de aplicaciones.** La solución necesita cinco contenedores y volúmenes persistentes; una plataforma gratuita de aplicaciones no da eso, y las que lo intentan suspenden el servicio por inactividad.
2. **Capa gratuita permanente, no crédito inicial.** Un crédito de prueba se agota, y se agota sin avisar. La capa debe ser permanente por diseño.
3. **Sin suspensión por inactividad.** Es el criterio que más pesa: un servicio que se duerme y tarda de 30 a 60 segundos en responder a la primera petición es inaceptable para la sustentación y para DRV-001.
4. **Todo en contenedores**, descritos en un archivo de composición versionado. Levantar el entorno completo es un comando, y el entorno local y el de producción se describen con el mismo mecanismo: lo que cambia es la configuración, no la topología (principio de paridad de entornos, ADR-0018).
5. **HTTPS obligatorio**, con certificado emitido y renovado automáticamente por el proxy inverso. HTTPS no es opcional: el *service worker* de la PWA (ADR-0015) y la API de notificaciones solo funcionan sobre un origen seguro.
6. **Imágenes inmutables etiquetadas con versión y commit**, nunca una etiqueta móvil. Se despliega exactamente la imagen que el pipeline construyó y escaneó (ADR-0017), no una reconstruida.
7. **Superficie expuesta mínima.** Solo el proxy inverso publica puertos a internet. PostgreSQL, el almacenamiento de objetos y el recolector de observabilidad escuchan solo en la red interna de los contenedores; para verlos se usa un túnel.
8. **El cliente web se sirve como archivos estáticos** desde el mismo proxy, de modo que no hay problema de origen cruzado entre el cliente y la API.
9. **Migraciones aplicadas durante el despliegue** (ADR-0003), antes de que la nueva versión reciba tráfico.
10. **Respaldo de la base y del almacenamiento de objetos** con el mismo procedimiento y probado al menos una vez (ADR-0012).

## Alternativas consideradas

| Criterio | **Máquina virtual de capa gratuita permanente (elegida)** | A. Plataforma de aplicaciones gratuita | B. Plataforma con crédito inicial | C. Kubernetes gestionado | D. Equipo del autor con un túnel |
|---|---|---|---|---|---|
| Disponible y rápida en la sustentación (RT-05) | Cumple | **No cumple** — suspensión por inactividad, 30-60 s de despertar | Parcial — hasta agotar el crédito | Cumple | **No cumple** — depende del equipo encendido y de la red |
| Varios contenedores con volúmenes | Cumple | Parcial | Parcial | Cumple | Cumple |
| Costo permanente cero (RT-07) | Cumple | Cumple | **No cumple** — el crédito se agota | **No cumple** | Cumple |
| Esfuerzo de montaje (RN-02) | Parcial — hay que administrar la máquina | Cumple | Cumple | **No cumple** | Cumple |
| HTTPS con certificado válido | Cumple — proxy con emisión automática | Cumple | Cumple | Cumple | Parcial |
| Independiente de la red institucional | Cumple | Cumple | Cumple | Cumple | **No cumple** — la red del autor bloquea túneles |

- **A. Plataforma de aplicaciones gratuita** — descartada por el criterio 3. Es la opción más cómoda de montar, pero suspende los servicios gratuitos tras unos minutos de inactividad y tarda entre 30 y 60 segundos en volver. Durante una sustentación, eso es exactamente el peor momento posible.
- **B. Plataforma con crédito inicial** — descartada: funciona bien hasta que el crédito se agota, y se agota en semanas de uso activo. No es una base para algo que debe seguir en pie después de la entrega.
- **C. Kubernetes gestionado** — descartada: es la respuesta correcta a un problema de escala que aquí no existe, no tiene capa gratuita real y su montaje consumiría días.
- **D. El equipo del autor con un túnel** — descartada: hace que la URL pública dependa de que el portátil esté encendido, y la red institucional del autor bloquea los servicios de túnel, lo que ya se comprobó en un proyecto anterior.

**Lectura:** el factor decisivo no es técnico sino de riesgo de calendario. La máquina virtual exige administrar un servidor, que es su desventaja real frente a la alternativa A, pero es la única que garantiza que la plataforma responda de inmediato cuando el docente abra la URL.

## Consecuencias

**Positivas**

- La plataforma está siempre encendida y responde sin espera de arranque.
- Las cinco piezas conviven con volúmenes persistentes, lo que ninguna plataforma gratuita de aplicaciones permite.
- El archivo de composición documenta la topología completa: es a la vez el despliegue y parte del manual de puesta a punto.
- HTTPS válido, que es un prerrequisito técnico de la PWA y de las notificaciones.
- La superficie expuesta es un solo puerto.

**Negativas**

- **Hay un servidor que administrar**: actualizaciones del sistema operativo, espacio en disco y memoria. Es el costo de esta decisión.
- **Un único punto de fallo y ninguna redundancia.** Si la máquina cae, la plataforma cae. Para el alcance de la entrega es aceptable y hay que declararlo; la mitigación es el respaldo probado.
- Los recursos son limitados y cinco contenedores los consumen: hay que vigilar la memoria, y es la razón por la que ADR-0005 descartó añadir un broker de mensajes.
- **Riesgo de calendario propio:** la creación de la cuenta puede exigir verificación y tardar. Es la tarea que debe hacerse primero, porque bloquea la demostración.
- La región de la máquina puede tener capacidad agotada para la capa gratuita, lo que obliga a probar otra región.

## Tácticas de Bass aplicadas

- **Limitar la exposición** — solo el proxy publica puertos.
- **Mantener copias de los datos** — respaldo de base y objetos.
- **Gestionar la configuración** — topología versionada, imágenes inmutables.
- **Autenticar y cifrar el tránsito** — HTTPS obligatorio.

## Escenarios y requisitos relacionados

- `QA-REND-01` — sin arranque en frío que añada segundos a la primera petición.
- `QA-SEG-02` — el almacenamiento de objetos no está expuesto a internet.
- `RT-04`, `RT-05`, `RT-07`, `RN-02`.

## ADRs relacionados

- **ADR-0017** — el pipeline que construye, escanea y despliega las imágenes.
- **ADR-0018** — dónde viven los secretos en esta máquina.
- **ADR-0012** — el volumen del almacenamiento de objetos.
- **ADR-0014** — el recolector, accesible solo por túnel.

## Notas de implementación

- La cuenta se crea el primer día del desarrollo, antes que cualquier otra tarea de infraestructura: es la única que depende de la aprobación de un tercero.
- Los puertos de la plataforma se eligen distintos de los que usan los otros proyectos del autor en su equipo, para poder tener varios entornos locales encendidos a la vez.
- El despliegue se prueba completo al menos un día antes de la entrega, incluida la ejecución del flujo de publicación de un Kudo desde un celular con datos móviles, fuera de la red de casa. Una demostración que solo funciona en la red local no cumple RT-05.
- Se deja anotado el procedimiento manual de despliegue además del automatizado: si el pipeline falla la mañana de la entrega, hay que poder desplegar a mano.

## Revisión del 2026-11-11: el punto 2 no se pudo cumplir

El punto 2 de esta decisión dice **«capa gratuita permanente, no crédito inicial»**, y argumenta que un crédito se agota y se agota sin avisar. El despliegue real **no cumple ese punto**: corre sobre una máquina pagada con el crédito de Azure for Students, que son 100 dólares a doce meses.

**Qué se intentó antes, y por qué no salió.** El proveedor elegido ofrecía capa gratuita permanente, que era exactamente lo que el punto 2 pedía. Su registro falló: tras verificar la tarjeta, la creación de la cuenta se rechazó con un error genérico que atribuye el problema a un registro previo incompleto, y resolverlo pasa por su soporte. A dos días de la entrega, esperar una respuesta de soporte no era una opción. Se descartó también por capacidad y cupos un segundo intento en otras regiones del mismo proveedor y en formas distintas.

**Qué se eligió.** Una máquina de 2 núcleos y 8 GB en Azure, pagada con el crédito de estudiante asociado al correo institucional. El coste real hasta la sustentación es de unos 9 dólares de los 100.

**Qué riesgo queda vivo, dicho sin adornos.** El que este ADR quería evitar: el crédito se acaba. Mitigaciones aplicadas:

- La máquina se **apaga cuando no se usa**. Detenida y desasignada no consume crédito de cómputo, solo el disco, que son céntimos al mes. Así el crédito alcanza de sobra los doce meses de la oferta.
- Hay **apagado automático** programado, para que un olvido no consuma el crédito de madrugada.
- El procedimiento de despliegue **no depende del proveedor**: es una máquina con Docker, un archivo de composición y un nombre de dominio. Mudarse a otro sitio es crear una máquina, instalar Docker y ejecutar el mismo guion.

**Lo que no cambió, que es casi todo.** Los diez puntos de la decisión siguen en pie: una máquina, contenedores con un único archivo de composición, HTTPS automático, imágenes etiquetadas con versión y commit, superficie expuesta mínima, migraciones durante el despliegue y respaldo conjunto de base y adjuntos. **Ni el compose, ni el proxy, ni el guion necesitaron una sola línea distinta por el cambio de proveedor**, y eso no es suerte: esta decisión nunca nombró un proveedor, decidió una forma de desplegar.

**Sobre el nombre de dominio.** No se compró uno: se usa un servicio de DNS comodín que resuelve `kudos.<IP>.sslip.io` a la máquina, suficiente para un certificado emitido por una autoridad real. Si el proyecto siguiera, un dominio propio es un cambio de una línea en la configuración.

## Revisión del 2026-11-13 — qué es y qué no es el «despliegue automatizado»

RT-04 pedía un pipeline de despliegue automatizado, y este ADR lo daba por cubierto. **Hay que
decirlo con precisión**, porque la expresión se queda corta:

- **Automatizado por el pipeline:** construir la imagen, escanearla con Trivy y **publicarla**
  en el registro con la etiqueta de versión y commit, solo desde `main` y solo si pasaron todas las
  puertas. Eso es lo que hace el CI en cada cambio.
- **No automatizado por el pipeline:** el despliegue en la máquina. Lo hace `scripts/desplegar.sh`,
  que **se ejecuta a mano por SSH** con las dos etiquetas: trae las imágenes, aplica las migraciones,
  arranca la versión nueva, recarga el proxy y comprueba que responde. Es un solo comando y es
  repetible, pero no lo dispara ningún evento de GitHub.

La razón fue de riesgo y no de pereza: un despliegue disparado por el pipeline exige guardar en
GitHub una llave SSH de la máquina, y en la semana de la entrega pesó más no dejar un secreto con
acceso al servidor en un tercero. **El paso que falta** es un trabajo del flujo que, tras publicar,
se conecte por SSH y ejecute ese mismo guion; el guion ya está hecho para que ese cambio sea pequeño.

## Referencias

- Docker Inc. *Compose specification.*
- OWASP. *Docker Security Cheat Sheet.*
- Let's Encrypt. *ACME protocol* (RFC 8555).

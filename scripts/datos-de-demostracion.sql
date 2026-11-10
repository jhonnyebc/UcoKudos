-- Datos de demostración para el entorno local.
--
-- QUÉ ES ESTO Y QUÉ NO ES. Son las cuentas con las que se enseña la plataforma funcionando: una
-- facultad, un programa y cinco personas con roles distintos, ya aprobadas y verificadas. No es
-- una migración y no se ejecuta sola: las migraciones siembran lo que el sistema necesita para
-- existir —las categorías, los roles—, y esto es contenido de ejemplo, que es otra cosa.
--
-- POR QUÉ LAS CUENTAS SE CREAN YA ACTIVAS. El registro real (RF-01) deja la cuenta pendiente, y
-- activarla exige las tres condiciones de RF-02: solicitud aprobada, correo verificado y celular
-- verificado. Eso está bien y es lo que se demuestra con el flujo de aprobación. Pero para
-- *empezar* a demostrar hace falta al menos una cuenta que ya pueda entrar, y alguien que apruebe
-- a los demás: este archivo resuelve ese arranque en frío.
--
-- La base impone la coherencia aunque uno se equivoque aquí: hay una restricción que impide marcar
-- una cuenta como activa sin las tres verificaciones.
--
-- Es idempotente: se puede ejecutar varias veces sin duplicar nada.
--
-- Uso:  ./scripts/sembrar-demostracion.ps1

BEGIN;

-- --- Estructura académica ----------------------------------------------------------------------

INSERT INTO nucleo.facultad (id, nombre, codigo, activa)
VALUES ('0193b000-0000-7000-8000-000000000001', 'Facultad de Ingeniería', 'ING', true)
ON CONFLICT (codigo) DO NOTHING;

INSERT INTO nucleo.programa (id, facultad_id, nombre, codigo, activo)
VALUES (
    '0193b000-0000-7000-8000-000000000002',
    '0193b000-0000-7000-8000-000000000001',
    'Ingeniería de Sistemas',
    'ING-SIS',
    true)
ON CONFLICT (id) DO NOTHING;

-- --- Personas ------------------------------------------------------------------------------------
--
-- El correo es la identidad: tiene que coincidir con el del proveedor de identidad, porque es lo
-- que la API lee del token para saber quién pregunta (ADR-0004).

INSERT INTO nucleo.persona (
    id, correo_institucional, celular_e164, nombre_completo,
    correo_verificado, celular_verificado, solicitud_aprobada, estado_cuenta, creada_en)
VALUES
    ('0193b000-0000-7000-8000-000000000010', 'jhonnybenitezcaro@gmail.com', '+573225234594',
     'Jhonny Benítez Caro', true, true, true, 'Activa', now()),

    ('0193b000-0000-7000-8000-000000000011', 'rector@uco.edu.co', '+573000000002',
     'Marta Restrepo', true, true, true, 'Activa', now()),

    ('0193b000-0000-7000-8000-000000000012', 'coordinador@uco.edu.co', '+573000000003',
     'Carlos Álvarez', true, true, true, 'Activa', now()),

    ('0193b000-0000-7000-8000-000000000013', 'docente@uco.edu.co', '+573000000004',
     'Ana Gómez', true, true, true, 'Activa', now()),

    ('0193b000-0000-7000-8000-000000000014', 'estudiante@uco.edu.co', '+573000000005',
     'Luis Peña Muñoz', true, true, true, 'Activa', now()),

    -- Segunda identidad de prueba. El correo es real y el nombre no: sirve para enseñar la
    -- plataforma con dos personas distintas a la vez, que es la unica forma de ver llegar un aviso
    -- en vivo. Docente del mismo programa, para que pueda reconocer y ser reconocida sin tropezar
    -- con las reglas de jerarquia.
    ('0193b000-0000-7000-8000-000000000015', 'jhonny.benitez5293@uco.net.co', '+573000000006',
     'Sara Montoya Arango', true, true, true, 'Activa', now())
-- Al repetir se actualiza el nombre en lugar de no hacer nada, para que volver a ejecutarlo
-- **corrija** datos mal sembrados antes. Hizo falta: una primera versión del guion rompió las
-- tildes al enviar este archivo por una tubería de PowerShell, y «Peña» quedó como «PeÃ±a» en la
-- base. Con DO NOTHING no habría forma de arreglarlo sin borrar a mano.
ON CONFLICT (correo_institucional) DO UPDATE
    SET nombre_completo = EXCLUDED.nombre_completo,
        celular_e164 = EXCLUDED.celular_e164;

-- --- Roles ---------------------------------------------------------------------------------------
--
-- Un rol activo por persona: hay un índice único parcial que lo impone (RF-04), así que esto no
-- depende de que el script se porte bien.
--
-- Los ámbitos siguen las reglas del dominio: Rector y Coordinador Académico son de universidad y no
-- llevan facultad ni programa; Docente y Estudiante son de programa y los llevan.

INSERT INTO nucleo.asignacion_rol (id, persona_id, rol_id, facultad_id, programa_id, activa, desde)
VALUES
    -- Docente, con ámbito de programa: es quien publica Kudos en la demostración.
    ('0193b000-0000-7000-8000-000000000020', '0193b000-0000-7000-8000-000000000010', 6,
     '0193b000-0000-7000-8000-000000000001', '0193b000-0000-7000-8000-000000000002', true, now()),

    ('0193b000-0000-7000-8000-000000000021', '0193b000-0000-7000-8000-000000000011', 2,
     NULL, NULL, true, now()),

    ('0193b000-0000-7000-8000-000000000022', '0193b000-0000-7000-8000-000000000012', 3,
     NULL, NULL, true, now()),

    ('0193b000-0000-7000-8000-000000000023', '0193b000-0000-7000-8000-000000000013', 6,
     '0193b000-0000-7000-8000-000000000001', '0193b000-0000-7000-8000-000000000002', true, now()),

    ('0193b000-0000-7000-8000-000000000024', '0193b000-0000-7000-8000-000000000014', 7,
     '0193b000-0000-7000-8000-000000000001', '0193b000-0000-7000-8000-000000000002', true, now()),

    ('0193b000-0000-7000-8000-000000000025', '0193b000-0000-7000-8000-000000000015', 6,
     '0193b000-0000-7000-8000-000000000001', '0193b000-0000-7000-8000-000000000002', true, now())
ON CONFLICT (id) DO NOTHING;

COMMIT;

SELECT p.nombre_completo, p.correo_institucional, a.rol_id, p.estado_cuenta
FROM nucleo.persona p
JOIN nucleo.asignacion_rol a ON a.persona_id = p.id AND a.activa
ORDER BY a.rol_id;

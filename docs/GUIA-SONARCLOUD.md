# Guía: conectar SonarCloud a UcoKudos

Tarea de la Fase 1. El enunciado exige evidencia de análisis estático (RT-03), y hoy el job del CI **se salta solo** porque no existe el secreto `SONAR_TOKEN`. Pasa en verde, pero sin analizar nada.

Son unos 10 minutos. El repositorio es público, así que SonarCloud es gratis.

## 1. Crear la organización

1. Entra a `sonarcloud.io` y elige **Log in with GitHub**.
2. Autoriza SonarCloud en tu cuenta de GitHub.
3. **+** (arriba a la derecha) → **Analyze new project**.
4. Si te pide crear una organización, elige **Import an organization from GitHub** y selecciona `jhonnyebc`.
5. Plan: **Free**, que es el que aplica a repositorios públicos.

## 2. Importar el repositorio

1. En la lista de repositorios, marca **UcoKudos**.
2. **Set Up**.

Comprueba que la clave del proyecto quede exactamente así:

```text
jhonnyebc_UcoKudos
```

Y la organización:

```text
jhonnyebc
```

**Si SonarCloud te propone otra clave, cámbiala a esa.** Son los valores que el workflow ya tiene escritos en `.github/workflows/ci.yml`; si no coinciden, el análisis falla con un error de proyecto inexistente.

## 3. Desactivar el análisis automático

**Este paso es el que más se olvida y rompe el análisis.**

1. En el proyecto: **Administration** → **Analysis Method**.
2. **Desactiva** *Automatic Analysis*.

Por qué: el proyecto usa análisis **por CI**, que es el que importa la cobertura de las pruebas. Si el automático queda activo, los dos compiten y el que gana es el automático, que no sabe nada de la cobertura. Acabarías con un informe sin el dato que el enunciado pide.

## 4. Generar el token

1. Tu avatar (arriba a la derecha) → **My Account** → **Security**.
2. En **Generate Tokens**, escribe un nombre, por ejemplo `UcoKudos CI`.
3. Tipo: **Project Analysis Token**, y elige el proyecto UcoKudos. Si no te deja, un **User Token** sirve igual.
4. **Generate** y **copia el token**: solo se muestra una vez.

## 5. Guardar el token en GitHub

Dos formas. La más rápida, en tu terminal, pegando el token cuando lo pida:

```bash
gh secret set SONAR_TOKEN --repo jhonnyebc/UcoKudos
```

O por la web: `github.com/jhonnyebc/UcoKudos` → **Settings** → **Secrets and variables** → **Actions** → **New repository secret**, con el nombre exacto `SONAR_TOKEN`.

El nombre tiene que ser **exactamente** `SONAR_TOKEN`: es el que busca el workflow.

## 6. Comprobar que funciona

Con el secreto puesto, el análisis corre en el siguiente push. Para forzarlo sin esperar:

```bash
gh workflow run CI --repo jhonnyebc/UcoKudos --ref develop
```

Y para ver el resultado:

```bash
gh run list --repo jhonnyebc/UcoKudos --limit 3
```

En el job **Análisis estático (Sonar)** ya no debería aparecer el paso saltado, y en `sonarcloud.io` tendrías el tablero con la cobertura.

## Qué esperar del primer análisis

No te alarmes si el *Quality Gate* sale en rojo la primera vez. SonarCloud aplica por omisión su perfil «Sonar way», que exige un 80 % de cobertura **en código nuevo**, y la primera ejecución considera nuevo todo el proyecto.

La cobertura que se importa es la del núcleo, `Domain` y `Application`, que es donde está la lógica; la infraestructura está excluida a propósito en el workflow, porque medir cobertura de adaptadores premia escribir pruebas con dobles que no prueban nada útil.

Eso es material de sustentación, no un problema: la decisión está en el ADR-0017 y se explica.

## Para la evidencia de la entrega

Cuando el análisis pase, guarda capturas de:

- El tablero del proyecto, con la cobertura y los hallazgos.
- El job **Análisis estático (Sonar)** del CI en verde.

Son dos de las doce evidencias que pide el enunciado, y conviene recogerlas el día que se generan.

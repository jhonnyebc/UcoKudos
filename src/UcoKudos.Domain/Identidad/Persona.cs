using UcoKudos.Domain.Transversal;

namespace UcoKudos.Domain.Identidad;

/// <summary>
/// Integrante de la comunidad académica. Se registra con correo institucional y celular (RF-01), y
/// la cuenta solo queda activa con las tres condiciones de RF-02: solicitud aprobada, correo
/// verificado y celular verificado.
///
/// Las invariantes se validan al construir, de modo que no exista una instancia inválida
/// (ADR-0001). El tiempo entra como parámetro: el dominio nunca consulta el reloj del sistema, así
/// que las reglas son deterministas en las pruebas.
/// </summary>
public sealed class Persona
{
    private Persona()
    {
        // Solo para el materializador de la persistencia.
        CorreoInstitucional = null!;
        CelularE164 = null!;
        NombreCompleto = null!;
    }

    public Persona(string correoInstitucional, string celularE164, string nombreCompleto, DateTimeOffset ahora)
    {
        CorreoInstitucional = NormalizarCorreo(correoInstitucional);
        CelularE164 = NormalizarCelular(celularE164);
        NombreCompleto = ExigirNombre(nombreCompleto);
        Id = Guid.CreateVersion7(ahora);
        Estado = EstadoCuenta.Pendiente;
        CreadaEn = ahora;
    }

    public Guid Id { get; private set; }
    public string CorreoInstitucional { get; private set; }
    public string CelularE164 { get; private set; }
    public string NombreCompleto { get; private set; }
    public bool CorreoVerificado { get; private set; }
    public bool CelularVerificado { get; private set; }
    public bool SolicitudAprobada { get; private set; }
    public EstadoCuenta Estado { get; private set; }
    public DateTimeOffset CreadaEn { get; private set; }

    /// <summary>Puede usar la plataforma. Es lo que se comprueba en cada petición (ADR-0004).</summary>
    public bool PuedeAcceder => Estado == EstadoCuenta.Activa;

    public void VerificarCorreo()
    {
        CorreoVerificado = true;
        RecalcularEstado();
    }

    public void VerificarCelular()
    {
        CelularVerificado = true;
        RecalcularEstado();
    }

    public void AprobarSolicitud()
    {
        if (Estado == EstadoCuenta.Rechazada)
            throw new ExcepcionDominio("Una solicitud rechazada no se puede aprobar; hay que presentar una nueva.");

        SolicitudAprobada = true;
        RecalcularEstado();
    }

    public void RechazarSolicitud()
    {
        if (SolicitudAprobada)
            throw new ExcepcionDominio("Esta solicitud ya fue aprobada.");

        Estado = EstadoCuenta.Rechazada;
    }

    public void Desactivar()
    {
        if (Estado == EstadoCuenta.Pendiente)
            throw new ExcepcionDominio("Una cuenta que todavía no se activó no se puede desactivar.");

        Estado = EstadoCuenta.Inactiva;
    }

    public void Reactivar()
    {
        if (Estado != EstadoCuenta.Inactiva)
            throw new ExcepcionDominio("Solo se puede reactivar una cuenta inactiva.");

        RecalcularEstado();
    }

    public void CambiarCelular(string celularE164)
    {
        var nuevo = NormalizarCelular(celularE164);
        if (nuevo == CelularE164)
            return;

        CelularE164 = nuevo;
        // Un celular nuevo está sin verificar: si la cuenta estaba activa, deja de estarlo hasta
        // que se verifique. Es deliberado, porque el celular es un canal de aviso (RF-15).
        CelularVerificado = false;
        RecalcularEstado();
    }

    public void CambiarNombre(string nombreCompleto) => NombreCompleto = ExigirNombre(nombreCompleto);

    /// <summary>
    /// RF-02: la cuenta queda activa solo con las tres condiciones a la vez. Está en un único lugar
    /// para que no haya forma de activar una cuenta saltándose una.
    /// </summary>
    private void RecalcularEstado()
    {
        if (Estado == EstadoCuenta.Rechazada)
            return;

        Estado = SolicitudAprobada && CorreoVerificado && CelularVerificado
            ? EstadoCuenta.Activa
            : EstadoCuenta.Pendiente;
    }

    /// <summary>
    /// El correo se normaliza antes de guardarlo para que no entren dos cuentas que solo difieren
    /// en mayúsculas o en espacios alrededor.
    /// </summary>
    public static string NormalizarCorreo(string correo)
    {
        var limpio = (correo ?? string.Empty).Trim().ToLowerInvariant();
        if (limpio.Length == 0)
            throw new ExcepcionDominio("El correo institucional es obligatorio.");

        var arroba = limpio.IndexOf('@', StringComparison.Ordinal);
        var dominioTienePunto = arroba > 0 && arroba < limpio.Length - 1
            && limpio[(arroba + 1)..].Contains('.', StringComparison.Ordinal);
        var hayUnSoloArroba = arroba >= 0 && limpio.IndexOf('@', arroba + 1) < 0;

        return dominioTienePunto && hayUnSoloArroba
            ? limpio
            : throw new ExcepcionDominio("El correo " + correo + " no es una dirección válida.");
    }

    /// <summary>
    /// El celular se normaliza a E.164 en el borde de entrada, no en el adaptador, porque es el
    /// formato que exige la API de WhatsApp (ADR-0007) y así se guarda una sola forma.
    /// </summary>
    public static string NormalizarCelular(string celular)
    {
        var digitos = new string((celular ?? string.Empty).Where(char.IsDigit).ToArray());
        return digitos.Length is >= 8 and <= 15
            ? "+" + digitos
            : throw new ExcepcionDominio("El celular " + celular + " no es un número válido en formato internacional.");
    }

    private static string ExigirNombre(string nombre)
    {
        var limpio = (nombre ?? string.Empty).Trim();
        return limpio.Length == 0
            ? throw new ExcepcionDominio("El nombre completo es obligatorio.")
            : limpio;
    }
}

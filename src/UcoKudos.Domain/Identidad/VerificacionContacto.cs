using System.Security.Cryptography;
using System.Text;

namespace UcoKudos.Domain.Identidad;

/// <summary>Qué dato de contacto se está verificando (RF-02).</summary>
public enum TipoContacto
{
    Correo = 1,
    Celular = 2
}

/// <summary>Resultado de comprobar un código.</summary>
public enum ResultadoCodigo
{
    Correcto,
    Incorrecto,
    Vencido,

    /// <summary>Se agotaron los intentos: hay que pedir un código nuevo.</summary>
    Bloqueado,

    /// <summary>No hay un código vigente para comprobar.</summary>
    SinCodigo
}

/// <summary>
/// Un código de un solo uso para verificar el correo o el celular de una persona (RF-02).
///
/// **Se guarda su huella, no el código.** Quien lea la base no puede activar cuentas ajenas con lo
/// que encuentre. Lo que lo hace seguro no es la longitud de seis dígitos, que un atacante agota en
/// un millón de intentos, sino el **límite de intentos** y el **vencimiento**: con cinco intentos por
/// código, adivinarlo tiene una probabilidad de cinco entre un millón.
///
/// La comprobación de un código incorrecto **también cuenta y se guarda**: por eso
/// <see cref="Verificar"/> devuelve un resultado en lugar de lanzar. Lanzar revertiría la
/// transacción y el intento fallido no quedaría contado.
/// </summary>
public sealed class VerificacionContacto
{
    public const int IntentosMaximos = 5;

    public static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(10);

    private VerificacionContacto()
    {
        HuellaCodigo = null!;
    }

    public VerificacionContacto(Guid personaId, TipoContacto tipo, string codigo, DateTimeOffset ahora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codigo);

        Id = Guid.CreateVersion7(ahora);
        PersonaId = personaId;
        Tipo = tipo;
        HuellaCodigo = Huella(personaId, tipo, codigo);
        CreadaEn = ahora;
        ExpiraEn = ahora + Vigencia;
    }

    public Guid Id { get; private set; }
    public Guid PersonaId { get; private set; }
    public TipoContacto Tipo { get; private set; }
    public string HuellaCodigo { get; private set; }
    public DateTimeOffset CreadaEn { get; private set; }
    public DateTimeOffset ExpiraEn { get; private set; }
    public int Intentos { get; private set; }

    /// <summary>Ya se usó con éxito, o se reemplazó por uno más nuevo.</summary>
    public bool Consumida { get; private set; }

    public ResultadoCodigo Verificar(string codigoIngresado, DateTimeOffset ahora)
    {
        if (Consumida)
            return ResultadoCodigo.SinCodigo;

        if (ahora >= ExpiraEn)
            return ResultadoCodigo.Vencido;

        if (Intentos >= IntentosMaximos)
            return ResultadoCodigo.Bloqueado;

        Intentos++;

        var esperada = Encoding.UTF8.GetBytes(HuellaCodigo);
        var recibida = Encoding.UTF8.GetBytes(Huella(PersonaId, Tipo, (codigoIngresado ?? string.Empty).Trim()));

        // Comparación en tiempo constante: no revela cuántos caracteres coinciden.
        if (!CryptographicOperations.FixedTimeEquals(esperada, recibida))
            return ResultadoCodigo.Incorrecto;

        Consumida = true;
        return ResultadoCodigo.Correcto;
    }

    /// <summary>Un código nuevo invalida el anterior: solo el último sirve.</summary>
    public void Reemplazar() => Consumida = true;

    // Se mezcla la persona y el tipo para que la misma cifra no dé la misma huella en dos cuentas.
    private static string Huella(Guid personaId, TipoContacto tipo, string codigo) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{personaId:N}:{(int)tipo}:{codigo}")));
}

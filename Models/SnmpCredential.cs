namespace GlpiNg.Modules.Deployment.Models;

/// <summary>Version du protocole SNMP (front/snmpcredential.form.php côté GLPI core).</summary>
public enum SnmpVersion
{
    V1,
    V2c,
    V3
}

/// <summary>Protocole d'authentification SNMPv3 ("authentication" côté GLPI core).</summary>
public enum SnmpAuthProtocol
{
    None,
    Md5,
    Sha,
    Sha224,
    Sha256,
    Sha384,
    Sha512
}

/// <summary>Protocole de chiffrement SNMPv3 ("encryption" côté GLPI core).</summary>
public enum SnmpPrivProtocol
{
    None,
    Des,
    Aes128,
    TripleDes,
    CiscoAes192,
    CiscoAes256,
    Aes192Ietf,
    Aes256Ietf
}

/// <summary>
/// Identifiant SNMP utilisé par une <see cref="NetworkTask"/> pour interroger les équipements
/// réseau découverts — reprend "Identifiant SNMP" de GLPI core (front/snmpcredential.form.php).
/// <see cref="Community"/> sert pour v1/v2c ; <see cref="Username"/>/<see cref="AuthProtocol"/>/
/// <see cref="AuthPassphrase"/>/<see cref="PrivProtocol"/>/<see cref="PrivPassphrase"/> pour v3.
/// Contrairement à GLPI, pas de case "vider le mot de passe" à l'édition : les passphrases sont
/// stockées et affichées en clair, au même niveau de confiance que le reste de ce module (aucune
/// autre entité de GlpiNg ne masque de secret en édition).
/// </summary>
public class SnmpCredential
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public SnmpVersion Version { get; set; } = SnmpVersion.V2c;

    public string? Community { get; set; }

    public string? Username { get; set; }
    public SnmpAuthProtocol AuthProtocol { get; set; } = SnmpAuthProtocol.None;
    public string? AuthPassphrase { get; set; }
    public SnmpPrivProtocol PrivProtocol { get; set; } = SnmpPrivProtocol.None;
    public string? PrivPassphrase { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

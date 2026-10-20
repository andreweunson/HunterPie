using HunterPie.Core.Crypto;
using HunterPie.Core.Observability.Logging;
using HunterPie.Core.Vault;
using System.Text.Json;
using Credential = HunterPie.Core.Vault.Model.Credential;

namespace HunterPie.Platforms.Linux.Storage;

/// <summary>
/// Stores the credential in a 0600 file in the XDG config directory, with the password encrypted by the
/// same <see cref="ICryptoService"/> the Windows vault uses.
/// </summary>
internal class LinuxCredentialVault(ICryptoService cryptoService) : ICredentialVault
{
    private readonly ILogger _logger = LoggerFactory.Create();
    private readonly ICryptoService _cryptoService = cryptoService;
    private readonly string _path = Path.Combine(LinuxPaths.ConfigDirectory, "credentials.json");

    private record StoredCredential(string Username, string EncryptedPassword);

    public void Create(string username, string password)
    {
        try
        {
            var stored = new StoredCredential(username, _cryptoService.Encrypt(password));

            LinuxPaths.WritePrivateFile(_path, JsonSerializer.Serialize(stored));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error($"Failed to save credentials due to error: {ex.Message}");
        }
    }

    public void Delete()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }

    public Credential? Get()
    {
        if (!File.Exists(_path))
            return null;

        try
        {
            StoredCredential? stored = JsonSerializer.Deserialize<StoredCredential>(File.ReadAllText(_path));

            if (stored is not { Username.Length: > 0, EncryptedPassword.Length: > 0 })
                return null;

            return new Credential(
                Username: stored.Username,
                Password: _cryptoService.Decrypt(stored.EncryptedPassword)
            );
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to decrypt password: {ex}");
            return null;
        }
    }
}

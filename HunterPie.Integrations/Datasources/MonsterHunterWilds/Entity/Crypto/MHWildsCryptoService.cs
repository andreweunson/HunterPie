using HunterPie.Core.Address.Map;
using HunterPie.Core.Domain.Memory;
using HunterPie.Core.Observability.Logging;
using HunterPie.Integrations.Datasources.MonsterHunterWilds.Definitions.Crypto;
using System.Diagnostics;
using System.Runtime.Intrinsics;
using X86Aes = System.Runtime.Intrinsics.X86.Aes;
namespace HunterPie.Integrations.Datasources.MonsterHunterWilds.Entity.Crypto;

public class MHWildsCryptoService(IMemoryAsync memory) : IDisposable
{
    private delegate Vector128<byte> AesFunc(Vector128<byte> values, Vector128<byte> roundKey);

    // The game fills the key in during its own startup. HunterPie can attach before that happens (always on Linux, where
    // there is no main window to wait for), and a key cached while it was still empty makes every value decrypt to garbage
    // for the rest of the session. Re-reading 32 bytes once a second is cheap and also follows a key that changes.
    private static readonly TimeSpan KeyRefreshInterval = TimeSpan.FromSeconds(1);

    private readonly ILogger _logger = LoggerFactory.Create();
    private readonly IMemoryAsync _memory = memory;
    private CryptographyVectors? _vectors;
    private long _vectorsTimestamp;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private readonly Lazy<AesFunc> _aesDecryptLast = new(static () =>
    {
        if (X86Aes.IsSupported)
            return X86Aes.DecryptLast;

        return ManualAesCrypto.DecryptLast;
    });

    public async Task<float> DecryptFloatAsync(MHWildsEncryptedFloat encrypted)
    {
        CryptographyVectors vectors = await GetCryptographyVectors();

        Vector128<byte> values = vectors.Key ^ encrypted.ToVector128();
        Vector128<byte> decryptedValues = _aesDecryptLast.Value(values, vectors.Round);

        return decryptedValues.AsSingle()[0];
    }

    private async Task<CryptographyVectors> GetCryptographyVectors()
    {
        try
        {
            await _semaphore.WaitAsync();

            if (_vectors is not null && Stopwatch.GetElapsedTime(_vectorsTimestamp) < KeyRefreshInterval)
                return _vectors;

            ulong[] encryptionKey = await _memory.ReadAsync<ulong>(
                address: AddressMap.GetAbsolute("Encryption::Key"),
                count: 2
            );
            ulong[] roundKey = await _memory.ReadAsync<ulong>(
                address: AddressMap.GetAbsolute("Encryption::Round"),
                count: 2
            );

            var vectors = new CryptographyVectors(
                Key: Vector128.Create(encryptionKey)
                    .AsByte(),
                Round: Vector128.Create(roundKey)
                    .AsByte()
            );

            if (_vectors != vectors)
                _logger.Debug(_vectors is null
                    ? $"Read the encryption key (empty: {IsEmpty(vectors)})"
                    : $"The encryption key changed (was empty: {IsEmpty(_vectors)}, now empty: {IsEmpty(vectors)})");

            _vectors = vectors;
            _vectorsTimestamp = Stopwatch.GetTimestamp();

            return vectors;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static bool IsEmpty(CryptographyVectors vectors) =>
        vectors.Key == Vector128<byte>.Zero && vectors.Round == Vector128<byte>.Zero;

    public void Dispose() => _semaphore.Dispose();
}
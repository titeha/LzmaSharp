using System.IO;
using System.Security.Cryptography;

using BenchmarkDotNet.Attributes;

using Lzma.Core.Zip;

namespace Lzma.Core.Benchmarks;

/// <summary>
/// Замеры WinZip-AES (шифрование ZIP-членов): CTR-256 + HMAC-SHA1 одноразово (два прохода: сначала
/// CTR по всему буферу, затем HMAC) против ПОТОКОВОГО write-through (один проход: CTR+HMAC на лету,
/// путь для членов &gt; 2 ГиБ). Показывает, не проседает ли инкрементальный путь относительно
/// одноразового (AES ускорен аппаратно AES-NI; накладные — батч-генерация keystream + IncrementalHash).
/// </summary>
[ShortRunJob]
[MemoryDiagnoser]
public class WinZipAesBenchmarks
{
  [Params(4, 16)]
  public int SizeMiB;

  private byte[] _data = [];
  private byte[] _aesKey = [];
  private byte[] _macKey = [];

  [GlobalSetup]
  public void Setup()
  {
    _data = BenchData.MakeTextLike(SizeMiB * 1024 * 1024);
    byte[] salt = new byte[WinZipAes.SaltSize(WinZipAes.Strength.Aes256)];
    RandomNumberGenerator.Fill(salt);
    WinZipAes.DeriveKeys("benchmark-password"u8, salt, WinZipAes.Strength.Aes256, out _aesKey, out _macKey, out _);
  }

  [Benchmark(Baseline = true, Description = "CTR+HMAC one-shot (2 passes)")]
  public byte[] OneShot()
  {
    byte[] cipher = (byte[])_data.Clone();
    WinZipAes.CtrTransform(_aesKey, cipher);
    return WinZipAes.ComputeAuthenticationCode(_macKey, cipher);
  }

  [Benchmark(Description = "CTR+HMAC streaming (1 pass)")]
  public byte[] Streaming()
  {
    using var output = new MemoryStream(_data.Length);
    using var enc = new WinZipAesEncryptWriteStream(output, _aesKey, _macKey);
    enc.Write(_data, 0, _data.Length);
    return enc.GetAuthenticationCode();
  }
}

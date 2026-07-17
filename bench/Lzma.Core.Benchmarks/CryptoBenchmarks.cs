using BenchmarkDotNet.Attributes;

using Lzma.Core.Crypto.Gost;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Benchmarks;

/// <summary>
/// Замеры криптопримитивов: 7zAES (AES-256-CBC поверх .NET BCL — аппаратно ускорен, AES-NI/ARM) и
/// собственная managed-реализация ГОСТ (Кузнечик/Магма в режиме гаммирования CTR, хеш Стрибог).
/// ГОСТ писался под корректность (тест-векторы RFC/ГОСТ), не под скорость — цифры нужны как база.
/// </summary>
[ShortRunJob]
[MemoryDiagnoser]
public class CryptoBenchmarks
{
  // Только 1 МиБ: managed-Кузнечик медленный (табличной L-оптимизации нет), 8 МиБ мучил бы прогон.
  [Params(1)]
  public int SizeMiB;

  private byte[] _data = [];
  private byte[] _out = [];
  private readonly byte[] _key = new byte[32];        // 256-битный ключ (AES-256 и ГОСТ)
  private readonly byte[] _ivAes = new byte[16];      // блок AES
  private readonly byte[] _ivKuznyechik = new byte[GostKuznyechikCtrTransform.InitializationVectorSize]; // 8
  private readonly byte[] _ivMagma = new byte[GostMagmaCtrTransform.InitializationVectorSize];           // 4

  [GlobalSetup]
  public void Setup()
  {
    int n = SizeMiB * 1024 * 1024;
    _data = BenchData.MakeTextLike(n);
    _out = new byte[n];
    for (int i = 0; i < _key.Length; i++)
      _key[i] = (byte)(i * 7 + 1);
    // IV нулями — на пропускную способность не влияет.
  }

  [Benchmark(Description = "AES-256-CBC encrypt (BCL, HW)")]
  public byte[] AesEncrypt()
  {
    SevenZipAesPackedStreamEncryptor.TryEncryptWithKey(_key, _ivAes, _data, out byte[] ciphertext);
    return ciphertext;
  }

  [Benchmark(Description = "Kuznyechik CTR (managed)")]
  public bool KuznyechikCtr()
      => GostKuznyechikCtrTransform.TryTransform(_key, _ivKuznyechik, _data, _out);

  [Benchmark(Description = "Magma CTR (managed)")]
  public bool MagmaCtr()
      => GostMagmaCtrTransform.TryTransform(_key, _ivMagma, _data, _out);

  [Benchmark(Description = "Streebog-256 hash (managed)")]
  public byte[] Streebog256() => GostStribog.Hash256(_data);
}

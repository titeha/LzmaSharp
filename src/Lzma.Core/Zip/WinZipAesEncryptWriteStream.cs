using System.Security.Cryptography;

namespace Lzma.Core.Zip;

/// <summary>
/// <para>Потоковый write-through для WinZip-AES: принимает открытый текст (сжатые данные члена),
/// шифрует его AES-CTR на лету и пишет шифртекст в нижележащий поток, попутно накапливая HMAC-SHA1.</para>
/// <para>
/// Инкрементальный аналог <see cref="WinZipAes.CtrTransform"/> + <see cref="WinZipAes.ComputeAuthenticationCode"/>:
/// счётчик 16-байтовый little-endian со старта 1, keystream переносится между вызовами <see cref="Write"/>
/// (данные могут приходить произвольными кусками, не кратными 16). Позволяет шифровать член &gt; 2 ГиБ,
/// не держа ни открытый текст, ни шифртекст целиком в памяти. Ключи (aesKey/macKey) передаёт вызывающий
/// — вывод из пароля/соли (PBKDF2) выполняется снаружи.
/// </para>
/// </summary>
internal sealed class WinZipAesEncryptWriteStream : Stream
{
  private const int BlockSize = 16;
  private const int WorkBufferSize = 1 << 16; // кратно 16

  private readonly Stream _inner;
  private readonly Aes _aes;
  private readonly ICryptoTransform _ecb;
  private readonly IncrementalHash _hmac;

  private readonly byte[] _counter = new byte[BlockSize];
  private readonly byte[] _keystream = new byte[BlockSize];
  private int _ksPos = BlockSize; // >= BlockSize → блок keystream ещё не сгенерирован

  private readonly byte[] _work = new byte[WorkBufferSize];
  private readonly byte[] _counterBuf = new byte[WorkBufferSize];
  private readonly byte[] _ksBuf = new byte[WorkBufferSize];

  public WinZipAesEncryptWriteStream(Stream inner, ReadOnlySpan<byte> aesKey, ReadOnlySpan<byte> macKey)
  {
    _inner = inner;
    _aes = Aes.Create();
    _aes.Mode = CipherMode.ECB;
    _aes.Padding = PaddingMode.None;
    _aes.Key = aesKey.ToArray();
    _ecb = _aes.CreateEncryptor();
    _hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, macKey);

    _counter[0] = 1; // старт счётчика (little-endian)
  }

  public override void Write(byte[] buffer, int offset, int count)
  {
    int done = 0;
    while (done < count)
    {
      int chunk = Math.Min(_work.Length, count - done);
      Array.Copy(buffer, offset + done, _work, 0, chunk);
      CtrTransform(_work.AsSpan(0, chunk));
      _inner.Write(_work, 0, chunk);
      _hmac.AppendData(_work, 0, chunk);
      done += chunk;
    }
  }

  /// <summary>Код аутентификации (первые 10 байт HMAC-SHA1 над шифртекстом). Вызывать после всех Write.</summary>
  public byte[] GetAuthenticationCode()
  {
    byte[] full = _hmac.GetHashAndReset();
    return full[..WinZipAes.AuthenticationCodeSize];
  }

  // CTR НА МЕСТЕ с переносом keystream между вызовами. Полные блоки шифруются пачкой (один
  // TransformBlock на весь кусок), хвостовой неполный блок — через персистентный _keystream/_ksPos.
  private void CtrTransform(Span<byte> data)
  {
    int i = 0;

    // 1) Доиспользуем незавершённый блок keystream с прошлого вызова.
    while (_ksPos < BlockSize && i < data.Length)
      data[i++] ^= _keystream[_ksPos++];

    // 2) Полные 16-байтовые блоки — пачкой: строим счётчики, один AES-проход, XOR.
    int fullLen = (data.Length - i) / BlockSize * BlockSize;
    if (fullLen > 0)
    {
      for (int b = 0; b < fullLen; b += BlockSize)
      {
        _counter.CopyTo(_counterBuf.AsSpan(b, BlockSize));
        IncrementCounter(_counter);
      }

      _ecb.TransformBlock(_counterBuf, 0, fullLen, _ksBuf, 0);

      for (int j = 0; j < fullLen; j++)
        data[i + j] ^= _ksBuf[j];

      i += fullLen;
    }

    // 3) Хвостовой неполный блок — генерируем один блок keystream и сохраняем позицию для следующего вызова.
    if (i < data.Length)
    {
      _ecb.TransformBlock(_counter, 0, BlockSize, _keystream, 0);
      IncrementCounter(_counter);
      _ksPos = 0;
      while (i < data.Length)
        data[i++] ^= _keystream[_ksPos++];
    }
  }

  private static void IncrementCounter(Span<byte> counter)
  {
    for (int i = 0; i < counter.Length; i++)
      if (++counter[i] != 0)
        break;
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _ecb.Dispose();
      _aes.Dispose();
      _hmac.Dispose();
      CryptographicOperations.ZeroMemory(_keystream);
      CryptographicOperations.ZeroMemory(_ksBuf);
    }

    base.Dispose(disposing);
  }

  public override bool CanRead => false;
  public override bool CanSeek => false;
  public override bool CanWrite => true;
  public override long Length => throw new NotSupportedException();
  public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
  public override void Flush() => _inner.Flush();
  public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
  public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
  public override void SetLength(long value) => throw new NotSupportedException();
}

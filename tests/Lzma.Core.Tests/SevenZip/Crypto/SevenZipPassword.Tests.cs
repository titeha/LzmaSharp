using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipPasswordTests
{
  [Fact]
  public void FromString_Ascii_КодируетВUtf16LeБезBom()
  {
    using SevenZipPassword password = SevenZipPassword.FromString("abc");

    Assert.Equal(6, password.Utf16LeByteCount);
    Assert.Equal(
        new byte[] { 0x61, 0x00, 0x62, 0x00, 0x63, 0x00 },
        password.ToUtf16LeByteArray());
  }

  [Fact]
  public void FromString_Кириллица_КодируетВUtf16LeБезBom()
  {
    const string text = "пароль";

    using SevenZipPassword password = SevenZipPassword.FromString(text);

    Assert.Equal(Encoding.Unicode.GetBytes(text), password.ToUtf16LeByteArray());
  }

  [Fact]
  public void FromChars_ПустойПароль_ВозвращаетПустойМатериал()
  {
    using SevenZipPassword password = SevenZipPassword.FromChars([]);

    Assert.Equal(0, password.Utf16LeByteCount);
    Assert.Empty(password.ToUtf16LeByteArray());
  }

  [Fact]
  public void CopyUtf16LeBytesTo_КопируетМатериалВБуфер()
  {
    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] destination = new byte[4];

    password.CopyUtf16LeBytesTo(destination);

    Assert.Equal(new byte[] { 0x61, 0x00, 0x62, 0x00 }, destination);
  }

  [Fact]
  public void CopyUtf16LeBytesTo_ПриМаломБуфере_БросаетArgumentException()
  {
    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    Assert.Throws<ArgumentException>(() => password.CopyUtf16LeBytesTo(new byte[3]));
  }

  [Fact]
  public void Dispose_ПослеDisposeДоступЗапрещен()
  {
    SevenZipPassword password = SevenZipPassword.FromString("secret");

    password.Dispose();

    Assert.Throws<ObjectDisposedException>(() => password.Utf16LeByteCount);
    Assert.Throws<ObjectDisposedException>(() => password.ToUtf16LeByteArray());
    Assert.Throws<ObjectDisposedException>(() => password.CopyUtf16LeBytesTo(new byte[32]));
  }

  [Fact]
  public void Dispose_ПовторныйВызовБезОшибки()
  {
    SevenZipPassword password = SevenZipPassword.FromString("secret");

    password.Dispose();
    password.Dispose();
  }

  [Fact]
  public void FromString_Null_БросаетArgumentNullException()
  {
    Assert.Throws<ArgumentNullException>(() => SevenZipPassword.FromString(null!));
  }
}

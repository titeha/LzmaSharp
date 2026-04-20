using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipDecodeOptionsTests
{
  [Fact]
  public void Default_НеСодержитПароль()
  {
    SevenZipDecodeOptions options = SevenZipDecodeOptions.Default;

    Assert.False(options.HasPassword);
    Assert.Null(options.Password);
  }

  [Fact]
  public void WithPassword_СохраняетПереданныйПароль()
  {
    using SevenZipPassword password = SevenZipPassword.FromString("secret");

    SevenZipDecodeOptions options = SevenZipDecodeOptions.WithPassword(password);

    Assert.True(options.HasPassword);
    Assert.Same(password, options.Password);
  }

  [Fact]
  public void WithPassword_ПриNull_БросаетArgumentNullException()
  {
    Assert.Throws<ArgumentNullException>(
        () => SevenZipDecodeOptions.WithPassword(null!));
  }

  [Fact]
  public void Options_НеВладеетПаролем()
  {
    SevenZipPassword password = SevenZipPassword.FromString("secret");

    SevenZipDecodeOptions options = SevenZipDecodeOptions.WithPassword(password);

    Assert.Equal("secret".Length * 2, options.Password!.Utf16LeByteCount);

    password.Dispose();

    Assert.True(options.HasPassword);
    Assert.Throws<ObjectDisposedException>(() => options.Password!.Utf16LeByteCount);
  }

  [Fact]
  public void Init_ПозволяетСоздатьНастройкиСПаролем()
  {
    using SevenZipPassword password = SevenZipPassword.FromString("secret");

    var options = new SevenZipDecodeOptions
    {
      Password = password,
    };

    Assert.True(options.HasPassword);
    Assert.Same(password, options.Password);
  }
}

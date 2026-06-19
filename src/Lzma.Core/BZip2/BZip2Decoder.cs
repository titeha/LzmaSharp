namespace Lzma.Core.BZip2;

/// <summary>
/// Результат декодирования BZip2-потока.
/// </summary>
public enum BZip2DecodeResult
{
  /// <summary>Поток успешно декодирован.</summary>
  Ok = 0,

  /// <summary>Поток повреждён или не соответствует формату BZip2.</summary>
  InvalidData = 1,

  /// <summary>Сценарий распознан, но пока не поддержан (например, randomized-блок).</summary>
  NotSupported = 2,
}

/// <summary>
/// <para>Управляемый декодер BZip2, без unsafe.</para>
/// <para>
/// Реализует разбор стандартного bzip2-потока (заголовок <c>BZh</c>, блоки, end-of-stream)
/// по эталонной логике bzip2 (Julian Seward): таблица используемых символов, Huffman по
/// группам, обратные MTF и RLE2 (RUNA/RUNB), обратный BWT по origPtr и обратный RLE1.
/// </para>
/// <para>
/// Контрольные суммы bzip2 (block/combined CRC) сейчас читаются, но не проверяются:
/// целостность данных в 7z дополнительно покрыта folder-CRC контейнера.
/// </para>
/// </summary>
public static class BZip2Decoder
{
  private const int MaxAlphaSize = 258;
  private const int MaxCodeLen = 23;
  private const int GroupSize = 50;
  private const int MaxGroups = 6;

  private const int RunA = 0;
  private const int RunB = 1;

  private const long BlockMagic = 0x314159265359; // sqrt(pi)
  private const long EndOfStreamMagic = 0x177245385090; // sqrt(e)

  /// <summary>
  /// Декодирует bzip2-поток целиком в <paramref name="output"/>.
  /// </summary>
  public static BZip2DecodeResult Decode(ReadOnlySpan<byte> input, out byte[] output)
  {
    output = [];

    var decoder = new Worker(input);

    try
    {
      output = decoder.Decode();
      return BZip2DecodeResult.Ok;
    }
    catch (NotSupportedBZip2Exception)
    {
      output = [];
      return BZip2DecodeResult.NotSupported;
    }
    catch (InvalidBZip2Exception)
    {
      output = [];
      return BZip2DecodeResult.InvalidData;
    }
  }

  private sealed class InvalidBZip2Exception : Exception;

  private sealed class NotSupportedBZip2Exception : Exception;

  private sealed class Worker
  {
    private readonly byte[] _input;
    private int _bytePos;
    private long _bitBuffer;
    private int _bitCount;

    private int _blockSize100k;

    // Рабочие таблицы блока.
    private readonly bool[] _inUse = new bool[256];
    private readonly byte[] _seqToUnseq = new byte[256];
    private readonly byte[] _selector = new byte[2 + (900000 / GroupSize)];
    private readonly byte[] _selectorMtf = new byte[2 + (900000 / GroupSize)];

    private readonly byte[][] _len = CreateJagged(MaxGroups, MaxAlphaSize);
    private readonly int[][] _limit = CreateJaggedInt(MaxGroups, MaxCodeLen + 1);
    private readonly int[][] _base = CreateJaggedInt(MaxGroups, MaxCodeLen + 1);
    private readonly int[][] _perm = CreateJaggedInt(MaxGroups, MaxAlphaSize);
    private readonly int[] _minLens = new int[MaxGroups];

    private int[] _tt = [];
    private byte[] _ll8 = [];

    // Выход: управляемый растущий byte[] (вместо List<byte>) — без overhead List.Add и с
    // разумной стартовой ёмкостью, чтобы избежать множества удвоений.
    private byte[] _out = [];
    private int _outLen;

    public Worker(ReadOnlySpan<byte> input) => _input = input.ToArray();

    private void Emit(byte b)
    {
      if (_outLen == _out.Length)
        Array.Resize(ref _out, _out.Length * 2);

      _out[_outLen++] = b;
    }

    public byte[] Decode()
    {
      // Заголовок потока: 'B' 'Z' 'h' <level '1'..'9'>.
      if (ReadBits(8) != 'B' || ReadBits(8) != 'Z' || ReadBits(8) != 'h')
        throw new InvalidBZip2Exception();

      int level = (int)ReadBits(8);
      if (level < '1' || level > '9')
        throw new InvalidBZip2Exception();

      _blockSize100k = level - '0';

      int maxBlock = _blockSize100k * 100000;
      _tt = new int[maxBlock + 1];
      _ll8 = new byte[maxBlock];

      // Стартовая ёмкость выхода: оценка по размеру входа (bzip2-сжатие текста ~3-9×),
      // но не меньше размера блока. Удвоение скорректирует, если оценка занижена.
      _out = new byte[Math.Max(maxBlock, _input.Length * 4)];
      _outLen = 0;

      while (true)
      {
        long magic = ReadBits(48);

        if (magic == EndOfStreamMagic)
        {
          ReadBits(32); // combined CRC (не проверяем)
          break;
        }

        if (magic != BlockMagic)
          throw new InvalidBZip2Exception();

        DecodeBlock();
      }

      return _outLen == _out.Length ? _out : _out.AsSpan(0, _outLen).ToArray();
    }

    private void DecodeBlock()
    {
      ReadBits(32); // block CRC (не проверяем)

      // Флаг рандомизации (устаревший механизм bzip2). Современные кодеры (в т.ч. 7-Zip)
      // его не ставят, но старые (например SharpZipLib) — могут на сильно сжимаемых данных.
      bool randomized = ReadBits(1) != 0;

      int origPtr = (int)ReadBits(24);

      int nInUse = ReadSymbolMap();
      int alphaSize = nInUse + 2;

      int numGroups = (int)ReadBits(3);
      if (numGroups < 2 || numGroups > MaxGroups)
        throw new InvalidBZip2Exception();

      int numSelectors = (int)ReadBits(15);
      if (numSelectors < 1)
        throw new InvalidBZip2Exception();

      ReadSelectors(numGroups, numSelectors);
      ReadHuffmanTables(numGroups, alphaSize);

      int nblock = DecodeMtfValues(alphaSize, numSelectors, nInUse, origPtr);

      InverseBwtAndRle1(nblock, origPtr, randomized);
    }

    /// <summary>
    /// Читает карту используемых символов (16 групп по 16 бит) и строит seqToUnseq.
    /// </summary>
    private int ReadSymbolMap()
    {
      Array.Clear(_inUse, 0, _inUse.Length);

      int inUse16 = (int)ReadBits(16);

      for (int i = 0; i < 16; i++)
      {
        if ((inUse16 & (0x8000 >> i)) == 0)
          continue;

        int bits = (int)ReadBits(16);
        for (int j = 0; j < 16; j++)
          if ((bits & (0x8000 >> j)) != 0)
            _inUse[(i * 16) + j] = true;
      }

      int nInUse = 0;
      for (int i = 0; i < 256; i++)
        if (_inUse[i])
          _seqToUnseq[nInUse++] = (byte)i;

      if (nInUse == 0)
        throw new InvalidBZip2Exception();

      return nInUse;
    }

    /// <summary>
    /// Читает и MTF-декодирует список селекторов групп.
    /// </summary>
    private void ReadSelectors(int numGroups, int numSelectors)
    {
      for (int i = 0; i < numSelectors; i++)
      {
        int j = 0;
        while (ReadBits(1) != 0)
        {
          j++;
          if (j >= numGroups)
            throw new InvalidBZip2Exception();
        }

        _selectorMtf[i] = (byte)j;
      }

      // MTF-декодирование индексов групп.
      Span<byte> pos = stackalloc byte[MaxGroups];
      for (int i = 0; i < numGroups; i++)
        pos[i] = (byte)i;

      for (int i = 0; i < numSelectors; i++)
      {
        int v = _selectorMtf[i];
        byte tmp = pos[v];
        while (v > 0)
        {
          pos[v] = pos[v - 1];
          v--;
        }

        pos[0] = tmp;
        _selector[i] = tmp;
      }
    }

    /// <summary>
    /// Читает delta-кодированные длины кодов Хаффмана для каждой группы и строит таблицы декодирования.
    /// </summary>
    private void ReadHuffmanTables(int numGroups, int alphaSize)
    {
      for (int g = 0; g < numGroups; g++)
      {
        int current = (int)ReadBits(5);

        for (int s = 0; s < alphaSize; s++)
        {
          while (true)
          {
            if (current < 1 || current > 20)
              throw new InvalidBZip2Exception();

            if (ReadBits(1) == 0)
              break;

            if (ReadBits(1) == 0)
              current++;
            else
              current--;
          }

          _len[g][s] = (byte)current;
        }
      }

      for (int g = 0; g < numGroups; g++)
        BuildDecodeTable(g, alphaSize);
    }

    /// <summary>
    /// Строит таблицы limit/base/perm для группы (порт hbCreateDecodeTables из bzip2).
    /// </summary>
    private void BuildDecodeTable(int group, int alphaSize)
    {
      byte[] length = _len[group];
      int[] limit = _limit[group];
      int[] codeBase = _base[group];
      int[] perm = _perm[group];

      int minLen = 32;
      int maxLen = 0;
      for (int s = 0; s < alphaSize; s++)
      {
        if (length[s] > maxLen)
          maxLen = length[s];
        if (length[s] < minLen)
          minLen = length[s];
      }

      _minLens[group] = minLen;

      int pp = 0;
      for (int l = minLen; l <= maxLen; l++)
        for (int s = 0; s < alphaSize; s++)
          if (length[s] == l)
            perm[pp++] = s;

      for (int i = 0; i <= MaxCodeLen; i++)
        codeBase[i] = 0;

      for (int s = 0; s < alphaSize; s++)
        codeBase[length[s] + 1]++;

      for (int i = 1; i <= MaxCodeLen; i++)
        codeBase[i] += codeBase[i - 1];

      for (int i = 0; i <= MaxCodeLen; i++)
        limit[i] = 0;

      int vec = 0;
      for (int l = minLen; l <= maxLen; l++)
      {
        vec += codeBase[l + 1] - codeBase[l];
        limit[l] = vec - 1;
        vec <<= 1;
      }

      for (int l = minLen + 1; l <= maxLen; l++)
        codeBase[l] = ((limit[l - 1] + 1) << 1) - codeBase[l];
    }

    /// <summary>
    /// Декодирует Huffman-поток в BWT-вход (_ll8): обратные RLE2 (RUNA/RUNB) и MTF.
    /// Возвращает длину BWT-блока.
    /// </summary>
    private int DecodeMtfValues(int alphaSize, int numSelectors, int nInUse, int origPtr)
    {
      int eob = alphaSize - 1;

      // MTF-список используемых символов.
      byte[] mtf = new byte[256];
      for (int i = 0; i < nInUse; i++)
        mtf[i] = _seqToUnseq[i];

      // Счётчики байтов для последующего обратного BWT.
      int[] unzftab = new int[256];

      int nblock = 0;
      int groupNo = -1;
      int groupPos = 0;
      int currentGroup = 0;

      int runLength = 0;
      int runBit = 0;

      int maxBlock = _blockSize100k * 100000;

      int symbol = NextSymbol(ref groupNo, ref groupPos, ref currentGroup, numSelectors, alphaSize);

      while (symbol != eob)
      {
        if (symbol is RunA or RunB)
        {
          // RLE2: накопление длины повтора символа MTF[0].
          if (symbol == RunA)
            runLength += 1 << runBit;
          else
            runLength += 2 << runBit;

          runBit++;
        }
        else
        {
          FlushRun(ref runLength, ref runBit, mtf[0], unzftab, ref nblock, maxBlock);

          // symbol 2..eob-1 => MTF-индекс (symbol-1).
          int index = symbol - 1;
          byte b = mtf[index];

          // Move-to-front.
          for (int k = index; k > 0; k--)
            mtf[k] = mtf[k - 1];
          mtf[0] = b;

          if (nblock >= maxBlock)
            throw new InvalidBZip2Exception();

          unzftab[b]++;
          _ll8[nblock++] = b;
        }

        symbol = NextSymbol(ref groupNo, ref groupPos, ref currentGroup, numSelectors, alphaSize);
      }

      // Хвостовой run перед EOB.
      FlushRun(ref runLength, ref runBit, mtf[0], unzftab, ref nblock, maxBlock);

      if ((uint)origPtr >= (uint)nblock)
        throw new InvalidBZip2Exception();

      BuildInverseBwt(unzftab, nblock);

      return nblock;
    }

    private void FlushRun(ref int runLength, ref int runBit, byte value, int[] unzftab, ref int nblock, int maxBlock)
    {
      if (runLength == 0)
        return;

      if (nblock + runLength > maxBlock)
        throw new InvalidBZip2Exception();

      unzftab[value] += runLength;
      for (int i = 0; i < runLength; i++)
        _ll8[nblock++] = value;

      runLength = 0;
      runBit = 0;
    }

    /// <summary>
    /// Декодирует очередной символ через Huffman текущей группы (по 50 символов на селектор).
    /// </summary>
    private int NextSymbol(ref int groupNo, ref int groupPos, ref int currentGroup, int numSelectors, int alphaSize)
    {
      if (groupPos == 0)
      {
        groupNo++;
        if (groupNo >= numSelectors)
          throw new InvalidBZip2Exception();

        currentGroup = _selector[groupNo];
        groupPos = GroupSize;
      }

      groupPos--;

      int[] limit = _limit[currentGroup];
      int[] codeBase = _base[currentGroup];
      int[] perm = _perm[currentGroup];
      int zn = _minLens[currentGroup];

      int zvec = (int)ReadBits(zn);
      while (true)
      {
        if (zn > MaxCodeLen)
          throw new InvalidBZip2Exception();

        if (zvec <= limit[zn])
          break;

        zn++;
        zvec = (zvec << 1) | (int)ReadBits(1);
      }

      int index = zvec - codeBase[zn];
      if ((uint)index >= (uint)alphaSize)
        throw new InvalidBZip2Exception();

      return perm[index];
    }

    /// <summary>
    /// Строит next-вектор (_tt) для обратного BWT по счётчикам байтов.
    /// </summary>
    private void BuildInverseBwt(int[] unzftab, int nblock)
    {
      // cftab[c] — стартовая позиция символа c в первом столбце.
      Span<int> cftab = stackalloc int[256];
      int sum = 0;
      for (int c = 0; c < 256; c++)
      {
        cftab[c] = sum;
        sum += unzftab[c];
      }

      for (int i = 0; i < nblock; i++)
      {
        int c = _ll8[i];
        _tt[cftab[c]] = i;
        cftab[c]++;
      }
    }

    /// <summary>
    /// Выполняет обратный BWT (следуя _tt от origPtr) и обратный RLE1 первой стадии,
    /// записывая итоговые байты в выходной буфер через <see cref="Emit"/>.
    /// </summary>
    private void InverseBwtAndRle1(int nblock, int origPtr, bool randomized)
    {
      int tPos = _tt[origPtr];

      // Состояние дерандомизации (используется только при randomized == true).
      int rNToGo = 0;
      int rTPos = 0;

      // RLE1: 4 одинаковых байта подряд => следующий байт это число дополнительных повторов.
      int runByte = -1;
      int runCount = 0;

      for (int i = 0; i < nblock; i++)
      {
        int b = _ll8[tPos];
        tPos = _tt[tPos];

        if (randomized)
        {
          if (rNToGo == 0)
          {
            rNToGo = Rnums[rTPos];
            rTPos++;
            if (rTPos == 512)
              rTPos = 0;
          }

          rNToGo--;
          b ^= rNToGo == 1 ? 1 : 0;
        }

        if (runCount == 4)
        {
          // b — счётчик дополнительных повторов (0..255).
          for (int k = 0; k < b; k++)
            Emit((byte)runByte);

          runCount = 0;
          runByte = -1;
          continue;
        }

        if (b == runByte)
          runCount++;
        else
        {
          runCount = 1;
          runByte = b;
        }

        Emit((byte)b);
      }
    }

    /// <summary>
    /// Читает <paramref name="need"/> бит в порядке «старший бит — первый» (как в BZip2).
    /// </summary>
    private long ReadBits(int need)
    {
      while (_bitCount < need)
      {
        if (_bytePos >= _input.Length)
          throw new InvalidBZip2Exception();

        _bitBuffer = (_bitBuffer << 8) | _input[_bytePos++];
        _bitCount += 8;
      }

      _bitCount -= need;
      long result = (_bitBuffer >> _bitCount) & ((1L << need) - 1);

      // Сбрасываем уже потреблённые старшие биты, чтобы буфер не переполнялся.
      _bitBuffer &= (1L << _bitCount) - 1;

      return result;
    }

    // Таблица псевдослучайных интервалов для дерандомизации (bzip2 randtable.c, 512 значений).
    private static readonly short[] Rnums =
    [
        619, 720, 127, 481, 931, 816, 813, 233, 566, 247, 985, 724, 205, 454, 863, 491,
        741, 242, 949, 214, 733, 859, 335, 708, 621, 574, 73, 654, 730, 472, 419, 436,
        278, 496, 867, 210, 399, 680, 480, 51, 878, 465, 811, 169, 869, 675, 611, 697,
        867, 561, 862, 687, 507, 283, 482, 129, 807, 591, 733, 623, 150, 238, 59, 379,
        684, 877, 625, 169, 643, 105, 170, 607, 520, 932, 727, 476, 693, 425, 174, 647,
        73, 122, 335, 530, 442, 853, 695, 249, 445, 515, 909, 545, 703, 919, 874, 474,
        882, 500, 594, 612, 641, 801, 220, 162, 819, 984, 589, 513, 495, 799, 161, 604,
        958, 533, 221, 400, 386, 867, 600, 782, 382, 596, 414, 171, 516, 375, 682, 485,
        911, 276, 98, 553, 163, 354, 666, 933, 424, 341, 533, 870, 227, 730, 475, 186,
        263, 647, 537, 686, 600, 224, 469, 68, 770, 919, 190, 373, 294, 822, 808, 206,
        184, 943, 795, 384, 383, 461, 404, 758, 839, 887, 715, 67, 618, 276, 204, 918,
        873, 777, 604, 560, 951, 160, 578, 722, 79, 804, 96, 409, 713, 940, 652, 934,
        970, 447, 318, 353, 859, 672, 112, 785, 645, 863, 803, 350, 139, 93, 354, 99,
        820, 908, 609, 772, 154, 274, 580, 184, 79, 626, 630, 742, 653, 282, 762, 623,
        680, 81, 927, 626, 789, 125, 411, 521, 938, 300, 821, 78, 343, 175, 128, 250,
        170, 774, 972, 275, 999, 639, 495, 78, 352, 126, 857, 956, 358, 619, 580, 124,
        737, 594, 701, 612, 669, 112, 134, 694, 363, 992, 809, 743, 168, 974, 944, 375,
        748, 52, 600, 747, 642, 182, 862, 81, 344, 805, 988, 739, 511, 655, 814, 334,
        249, 515, 897, 955, 664, 981, 649, 113, 974, 459, 893, 228, 433, 837, 553, 268,
        926, 240, 102, 654, 459, 51, 686, 754, 806, 760, 493, 403, 415, 394, 687, 700,
        946, 670, 656, 610, 738, 392, 760, 799, 887, 653, 978, 321, 576, 617, 626, 502,
        894, 679, 243, 440, 680, 879, 194, 572, 640, 724, 926, 56, 204, 700, 707, 151,
        457, 449, 797, 195, 791, 558, 945, 679, 297, 59, 87, 824, 713, 663, 412, 693,
        342, 606, 134, 108, 571, 364, 631, 212, 174, 643, 304, 329, 343, 97, 430, 751,
        497, 314, 983, 374, 822, 928, 140, 206, 73, 263, 980, 736, 876, 478, 430, 305,
        170, 514, 364, 692, 829, 82, 855, 953, 676, 246, 369, 970, 294, 750, 807, 827,
        150, 790, 288, 923, 804, 378, 215, 828, 592, 281, 565, 555, 710, 82, 896, 831,
        547, 261, 524, 462, 293, 465, 502, 56, 661, 821, 976, 991, 658, 869, 905, 758,
        745, 193, 768, 550, 608, 933, 378, 286, 215, 979, 792, 961, 61, 688, 793, 644,
        986, 403, 106, 366, 905, 644, 372, 567, 466, 434, 645, 210, 389, 550, 919, 135,
        780, 773, 635, 389, 707, 100, 626, 958, 165, 504, 920, 176, 193, 713, 857, 265,
        203, 50, 668, 108, 645, 990, 626, 197, 510, 357, 358, 850, 858, 364, 936, 638
    ];

    private static byte[][] CreateJagged(int rows, int cols)
    {
      var result = new byte[rows][];
      for (int i = 0; i < rows; i++)
        result[i] = new byte[cols];
      return result;
    }

    private static int[][] CreateJaggedInt(int rows, int cols)
    {
      var result = new int[rows][];
      for (int i = 0; i < rows; i++)
        result[i] = new int[cols];
      return result;
    }
  }
}

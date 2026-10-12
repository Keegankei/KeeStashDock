namespace KeeStashDock.Desktop;

/// <summary>
/// QR Code encoder using UTF-8 byte mode, automatic versions 1-40,
/// error correction level M, and mask selection by the ISO penalty rules.
/// Algorithm structure follows Project Nayuki's MIT-licensed QR Code generator.
/// KeeStashDock adapts the implementation to C# and limits its API to text input.
/// See third_party/qr for the upstream license and attribution.
/// </summary>
public static class QrEncoder
{
    // ECC codewords per block / number of blocks, ECC level M, versions 1..40.
    private static readonly int[] EccPerBlockM =
    { 10,16,26,18,24,16,18,22,22,26,30,22,22,24,24,28,28,26,26,26,
      26,28,28,28,28,28,28,28,28,28,28,28,28,28,28,28,28,28,28,28 };
    private static readonly int[] NumBlocksM =
    { 1,1,1,2,2,4,4,4,5,5,5,8,9,9,10,10,11,13,14,16,
      17,17,18,20,21,23,25,26,28,29,31,33,35,37,38,40,43,45,47,49 };

    /// <summary>Encodes text as a QR module matrix ([y, x], true = dark).
    /// Throws <see cref="ArgumentException"/> when the payload exceeds
    /// version 40 capacity at ECC M.</summary>
    public static bool[,] Encode(string text)
    {
        byte[] data = System.Text.Encoding.UTF8.GetBytes(text);

        // ---- version selection --------------------------------------
        int version = 0;
        for (int v = 1; v <= 40; v++)
        {
            int capBits = DataCodewords(v) * 8;
            int needBits = 4 + (v < 10 ? 8 : 16) + data.Length * 8;
            if (needBits <= capBits) { version = v; break; }
        }
        if (version == 0)
            throw new ArgumentException("Payload exceeds QR version 40 capacity.");

        // ---- bit stream: mode + length + data + terminator + padding --
        var bits = new List<bool>();
        void Put(int val, int n)
        { for (int i = n - 1; i >= 0; i--) bits.Add(((val >> i) & 1) != 0); }

        Put(0b0100, 4);                              // byte mode
        Put(data.Length, version < 10 ? 8 : 16);
        foreach (byte b in data) Put(b, 8);

        int dataCw = DataCodewords(version);
        Put(0, Math.Min(4, dataCw * 8 - bits.Count)); // terminator
        while (bits.Count % 8 != 0) bits.Add(false);
        for (int pad = 0xEC; bits.Count < dataCw * 8; pad ^= 0xEC ^ 0x11)
            Put(pad, 8);

        var dcw = new byte[dataCw];
        for (int i = 0; i < dataCw; i++)
        {
            int b = 0;
            for (int j = 0; j < 8; j++) b = (b << 1) | (bits[i * 8 + j] ? 1 : 0);
            dcw[i] = (byte)b;
        }

        byte[] allCw = InterleaveWithEcc(version, dcw);
        return BuildMatrix(version, allCw);
    }

    private static int TotalCodewords(int v)
    {
        int result = (16 * v + 128) * v + 64;
        if (v >= 2)
        {
            int num = v / 7 + 2;
            result -= (25 * num - 10) * num - 55;
            if (v >= 7) result -= 36;
        }
        return result / 8;
    }

    private static int DataCodewords(int v)
        => TotalCodewords(v) - EccPerBlockM[v - 1] * NumBlocksM[v - 1];

    // -------------------------------------------------- GF(256), 0x11D

    private static byte GfMul(int a, int b)
    {
        int r = 0;
        for (int i = 7; i >= 0; i--)
        {
            r = (r << 1) ^ ((r >> 7) * 0x11D);
            r ^= ((b >> i) & 1) * a;
        }
        return (byte)r;
    }

    private static byte[] RsGenerator(int degree)
    {
        var g = new List<byte> { 1 };            // little-endian coefficients
        int root = 1;
        for (int d = 0; d < degree; d++)
        {
            var ng = new byte[g.Count + 1];
            for (int i = 0; i < g.Count; i++)
            {
                ng[i] ^= GfMul(g[i], root);
                ng[i + 1] ^= g[i];
            }
            g = new List<byte>(ng);
            root = GfMul(root, 2);
        }
        g.Reverse();                              // highest-first, drop lead 1
        g.RemoveAt(0);
        return g.ToArray();
    }

    private static byte[] RsRemainder(ReadOnlySpan<byte> data, byte[] gen)
    {
        var res = new byte[gen.Length];
        foreach (byte b in data)
        {
            byte factor = (byte)(b ^ res[0]);
            Array.Copy(res, 1, res, 0, res.Length - 1);
            res[^1] = 0;
            for (int i = 0; i < gen.Length; i++)
                res[i] ^= GfMul(gen[i], factor);
        }
        return res;
    }

    private static byte[] InterleaveWithEcc(int v, byte[] dcw)
    {
        int nb = NumBlocksM[v - 1], eccLen = EccPerBlockM[v - 1];
        int total = TotalCodewords(v);
        int shortLen = total / nb - eccLen;
        int numLong = total % nb;
        byte[] gen = RsGenerator(eccLen);

        var dataBlocks = new List<byte[]>();
        var eccBlocks = new List<byte[]>();
        int k = 0;
        for (int i = 0; i < nb; i++)
        {
            int dlen = shortLen + (i >= nb - numLong ? 1 : 0);
            byte[] d = dcw.AsSpan(k, dlen).ToArray();
            k += dlen;
            dataBlocks.Add(d);
            eccBlocks.Add(RsRemainder(d, gen));
        }

        var outCw = new List<byte>(total);
        int maxD = shortLen + (numLong > 0 ? 1 : 0);
        for (int i = 0; i < maxD; i++)
            foreach (var d in dataBlocks)
                if (i < d.Length) outCw.Add(d[i]);
        for (int i = 0; i < eccLen; i++)
            foreach (var e in eccBlocks) outCw.Add(e[i]);
        return outCw.ToArray();
    }

    // ----------------------------------------------------- matrix build

    private static int[] AlignPositions(int v)
    {
        if (v == 1) return Array.Empty<int>();
        int num = v / 7 + 2;
        int step = v == 32 ? 26 : (v * 4 + num * 2 + 2) / (num * 2 - 2) * 2;
        var pos = new List<int> { 6 };
        for (int i = 0, p = 17 + 4 * v - 7; i < num - 1; i++, p -= step)
            pos.Insert(1, p);
        return pos.ToArray();
    }

    private static bool[,] BuildMatrix(int v, byte[] allCw)
    {
        int size = 17 + 4 * v;
        var mod = new bool[size, size];
        var isFunc = new bool[size, size];
        void Set(int x, int y, bool val) { mod[y, x] = val; isFunc[y, x] = true; }

        // Finders + separators
        foreach (var (cx, cy) in new[] { (3, 3), (size - 4, 3), (3, size - 4) })
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || x >= size || y < 0 || y >= size) continue;
                    int d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    Set(x, y, d != 2 && d != 4);
                }

        // Alignment patterns (skip the three finder corners)
        int[] ap = AlignPositions(v);
        for (int i = 0; i < ap.Length; i++)
            for (int j = 0; j < ap.Length; j++)
            {
                if ((i == 0 && j == 0) || (i == 0 && j == ap.Length - 1) ||
                    (i == ap.Length - 1 && j == 0)) continue;
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        Set(ap[j] + dx, ap[i] + dy,
                            Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }

        // Timing
        for (int i = 0; i < size; i++)
        {
            if (!isFunc[6, i]) Set(i, 6, i % 2 == 0);
            if (!isFunc[i, 6]) Set(6, i, i % 2 == 0);
        }

        // Reserve format areas + dark module
        for (int i = 0; i < 9; i++)
        {
            if (i == 6) continue;
            if (!isFunc[i, 8]) Set(8, i, false);
            if (!isFunc[8, i]) Set(i, 8, false);
        }
        for (int i = 0; i < 8; i++)
        {
            if (!isFunc[8, size - 1 - i]) Set(size - 1 - i, 8, false);
            if (!isFunc[size - 1 - i, 8]) Set(8, size - 1 - i, false);
        }
        Set(8, size - 8, true);

        // Version info (v >= 7), BCH(18,6) generator 0x1F25
        if (v >= 7)
        {
            int rem = v;
            for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            int vi = (v << 12) | rem;
            for (int i = 0; i < 18; i++)
            {
                bool b = ((vi >> i) & 1) != 0;
                int a = size - 11 + i % 3, c = i / 3;
                Set(a, c, b);
                Set(c, a, b);
            }
        }

        // Zigzag data placement
        var bits = new bool[allCw.Length * 8];
        for (int i = 0; i < allCw.Length; i++)
            for (int j = 0; j < 8; j++)
                bits[i * 8 + j] = ((allCw[i] >> (7 - j)) & 1) != 0;
        int idx = 0;
        bool upward = true;
        for (int x = size - 1; x > 0; x -= 2)
        {
            if (x == 6) x--;
            for (int i = 0; i < size; i++)
            {
                int y = upward ? size - 1 - i : i;
                foreach (int xx in new[] { x, x - 1 })
                    if (!isFunc[y, xx])
                    {
                        mod[y, xx] = idx < bits.Length && bits[idx];
                        idx++;
                    }
            }
            upward = !upward;
        }

        // Best mask by ISO penalty
        bool[,]? best = null;
        int bestPenalty = int.MaxValue;
        for (int mask = 0; mask < 8; mask++)
        {
            var cand = ApplyMask(mod, isFunc, size, mask);
            DrawFormat(cand, size, mask);
            int p = Penalty(cand, size);
            if (p < bestPenalty) { bestPenalty = p; best = cand; }
        }
        return best!;
    }

    private static bool[,] ApplyMask(bool[,] mod, bool[,] isFunc, int size, int mask)
    {
        var f = (bool[,])mod.Clone();
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                if (isFunc[y, x]) continue;
                bool inv = mask switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0,
                    5 => x * y % 2 + x * y % 3 == 0,
                    6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
                };
                if (inv) f[y, x] = !f[y, x];
            }
        return f;
    }

    /// <summary>Format info: ECC M bits (00) + mask, BCH(15,5) generator
    /// 0x537, XOR mask 0x5412 - validated against the ISO sample strings.</summary>
    private static void DrawFormat(bool[,] m, int size, int mask)
    {
        int data = (0 << 3) | mask;   // ECC level M encodes as 00
        int rem = data;
        for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        int fb = ((data << 10) | rem) ^ 0x5412;
        bool Bit(int i) => ((fb >> i) & 1) != 0;

        for (int i = 0; i <= 5; i++) m[i, 8] = Bit(i);
        m[7, 8] = Bit(6);
        m[8, 8] = Bit(7);
        m[8, 7] = Bit(8);
        for (int i = 9; i < 15; i++) m[8, 14 - i] = Bit(i);
        for (int i = 0; i < 8; i++) m[8, size - 1 - i] = Bit(i);
        for (int i = 8; i < 15; i++) m[size - 15 + i, 8] = Bit(i);
    }

    private static int Penalty(bool[,] m, int size)
    {
        int p = 0;

        // Rule 1: runs >= 5 (rows and columns)
        for (int pass = 0; pass < 2; pass++)
            for (int a = 0; a < size; a++)
            {
                int run = 1;
                for (int b = 1; b < size; b++)
                {
                    bool cur = pass == 0 ? m[a, b] : m[b, a];
                    bool prev = pass == 0 ? m[a, b - 1] : m[b - 1, a];
                    if (cur == prev) run++;
                    else { if (run >= 5) p += 3 + run - 5; run = 1; }
                }
                if (run >= 5) p += 3 + run - 5;
            }

        // Rule 2: 2x2 blocks
        for (int y = 0; y < size - 1; y++)
            for (int x = 0; x < size - 1; x++)
                if (m[y, x] == m[y, x + 1] && m[y, x] == m[y + 1, x] &&
                    m[y, x] == m[y + 1, x + 1]) p += 3;

        // Rule 3: finder-like 1011101 with 4 light on either side
        Span<bool> pat = stackalloc bool[11]
        { true, false, true, true, true, false, true, false, false, false, false };
        for (int pass = 0; pass < 2; pass++)
            for (int a = 0; a < size; a++)
                for (int i = 0; i <= size - 11; i++)
                {
                    bool fwd = true, rev = true;
                    for (int j = 0; j < 11; j++)
                    {
                        bool cell = pass == 0 ? m[a, i + j] : m[i + j, a];
                        if (cell != pat[j]) fwd = false;
                        if (cell != pat[10 - j]) rev = false;
                    }
                    if (fwd || rev) p += 40;
                }

        // Rule 4: dark proportion
        int dark = 0;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                if (m[y, x]) dark++;
        p += Math.Abs(dark * 20 - size * size * 10) / (size * size) * 10;
        return p;
    }
}

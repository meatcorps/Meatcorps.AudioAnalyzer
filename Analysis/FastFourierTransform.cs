using System.Numerics;

namespace Meatcorps.AudioAnalyzer.Analysis;

internal static class FastFourierTransform
{
    public static void Transform(Complex[] buffer)
    {
        var n = buffer.Length;
        if (n == 0 || (n & (n - 1)) != 0)
            throw new ArgumentException("FFT size must be a power of two.", nameof(buffer));

        // Bit-reversal permutation.
        for (var i = 1; i < n; i++)
        {
            var bit = n >> 1;
            var j = 0;

            // Calculate the reversed index without allocating a lookup table.
            var value = i;
            for (var b = bit; b > 0; b >>= 1)
            {
                j = (j << 1) | (value & 1);
                value >>= 1;
            }

            if (i < j)
                (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
        }

        for (var length = 2; length <= n; length <<= 1)
        {
            var angle = -2.0 * Math.PI / length;
            var wLength = new Complex(Math.Cos(angle), Math.Sin(angle));

            for (var i = 0; i < n; i += length)
            {
                var w = Complex.One;
                var half = length >> 1;

                for (var j = 0; j < half; j++)
                {
                    var even = buffer[i + j];
                    var odd = buffer[i + j + half] * w;
                    buffer[i + j] = even + odd;
                    buffer[i + j + half] = even - odd;
                    w *= wLength;
                }
            }
        }
    }
}

/*
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge.

This source is subject to the GNU General Public License v3.0 (GPLv3).
See https://www.gnu.org/licenses/gpl-3.0.html.

THIS PROGRAM IS FREE SOFTWARE: YOU CAN REDISTRIBUTE IT AND/OR MODIFY
IT UNDER THE TERMS OF THE GNU GENERAL PUBLIC LICENSE AS PUBLISHED BY
THE FREE SOFTWARE FOUNDATION, EITHER VERSION 3 OF THE LICENSE, OR
(AT YOUR OPTION) ANY LATER VERSION.

THIS PROGRAM IS DISTRIBUTED IN THE HOPE THAT IT WILL BE USEFUL,
BUT WITHOUT ANY WARRANTY; WITHOUT EVEN THE IMPLIED WARRANTY OF
MERCHANTABILITY OR FITNESS FOR A PARTICULAR PURPOSE. SEE THE
GNU GENERAL PUBLIC LICENSE FOR MORE DETAILS.
*/

using System.Security.Cryptography;
using System.Diagnostics;
using System;

namespace WorldEdit
{
    /// <summary>
    /// RNG benchmark harness you can call from an in-game command.
    /// Example wiring:
    ///   RngBenchmark.Run(iter, len, modesCsv, s => SendFeedback(s));
    ///
    /// modesCsv: "all" or any comma-separated subset of:
    ///   "original", "getint32", "buffered", "fast"
    /// </summary>
    internal static class RngBenchmark
    {
        #region Public Entry Point

        /// <summary>
        /// Runs one or more RNG picker benchmarks and streams formatted results via <paramref name="log"/>.
        /// </summary>
        /// <param name="iterations">Number of samples to draw per picker (e.g., 2,000,000).</param>
        /// <param name="patternLen">Bucket count for the histogram (e.g., 64).</param>
        /// <param name="modesCsv">
        /// Mode filter:
        /// "all" or a comma-separated set of names:
        ///   "original", "getint32", "buffered", "fast".
        /// </param>
        /// <param name="log">Line sink (e.g., SendFeedback, Console.WriteLine, or your logger).</param>
        public static void Run(int iterations, int patternLen, string modesCsv, Action<string> log)
        {
            if (log == null) log = Console.WriteLine;
            if (iterations <= 0) iterations = 2000000;
            if (patternLen <= 0) patternLen = 64;

            // Parse mode filter
            // NOTE: local function requires C# 7.0+; this keeps callsites clean below.
            string modes = (modesCsv ?? "all").Trim().ToLowerInvariant();
            bool want(string name) =>
                modes == "all" || modes.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;

            // Build pattern 0..N-1
            // NOTE: histogram bins; values are indices, not the actual block IDs.
            int[] pattern = new int[patternLen];
            for (int i = 0; i < pattern.Length; i++) pattern[i] = i;

            // Warm-up JIT
            // IMPORTANT: ensures first timed run isn't distorted by JIT compilation.
            PickerOriginal(pattern);
            PickerGetInt32Style(pattern);
            PickerBuffered(pattern);
            PickerFast(pattern);

            // Header
            log(".NET: " + Environment.Version);
            log("Iterations: " + iterations.ToString("N0") + ", PatternLen: " + patternLen);
            log(new string('-', 50));

            // Conditional runs based on filter
            if (want("original"))
                Bench("Original (new RNGCSP per call, modulo bias)", pattern, iterations, PickerOriginal, log);

            if (want("getint32"))
                Bench("GetInt32-style (CSPRNG, zero bias)",          pattern, iterations, PickerGetInt32Style, log);

            if (want("buffered"))
                Bench("Buffered CSPRNG (zero bias)",                 pattern, iterations, PickerBuffered, log);

            if (want("fast"))
                Bench("Fast PRNG (PCG-ish, seeded from CSPRNG)",     pattern, iterations, PickerFast, log);
        }
        #endregion

        #region Benchmark Harness

        /// <summary>
        /// Measures the time to produce <paramref name="iterations"/> samples with <paramref name="picker"/>,
        /// collects a histogram for a quick uniformity sanity check, and logs results.
        /// </summary>
        /// <remarks>
        /// Uniformity metric here is Σ|count - mean| / iterations; lower ≈ more even bucket distribution.
        /// This is a lightweight heuristic, not a formal randomness test.
        /// The "checksum" prevents the JIT from optimizing away the loop body.
        /// </remarks>
        private static void Bench(string name, int[] pattern, int iterations, Func<int[], int> picker, Action<string> log)
        {
            // Reduce GC noise prior to timing
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();

            Stopwatch sw = Stopwatch.StartNew();
            long checksum = 0;
            int[] hist = new int[pattern.Length];

            for (int i = 0; i < iterations; i++)
            {
                int idx = picker(pattern);
                checksum += idx;     // prevents dead-code elimination
                hist[idx]++;         // frequency bin
            }

            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds;
            double mops = iterations / ms;

            // Quick uniformity score
            double mean = iterations / (double)pattern.Length;
            double dev = 0.0;
            for (int i = 0; i < hist.Length; i++) dev += Math.Abs(hist[i] - mean);
            double uniformity = dev / iterations;

            log(name);
            log(string.Format(
                "  Time: {0,9:N2} ms \tThroughput: {1,8:N2} ops/ms \tUniformity≈ {2:0.0000} \tChecksum: {3}",
                ms, mops, uniformity, checksum));
            log(new string('-', 50));
        }
        #endregion

        #region Picker: Original (RNGCSP per-call, modulo bias)

        /// <summary>
        /// Legacy reference: constructs a new RNGCSP per pick and maps via modulo (biased).
        /// Kept for comparison; avoid in production due to cost and bias.
        /// </summary>
        private static int PickerOriginal(int[] pattern)
        {
            if (pattern.Length == 1) return pattern[0];
            using (var rng = new RNGCryptoServiceProvider())
            {
                byte[] b = new byte[4];
                rng.GetBytes(b);
                int val = BitConverter.ToInt32(b, 0);
                int m = val % pattern.Length;
                if (m < 0) m = -m; // still biased; mirrors legacy code
                return pattern[m];
            }
        }
        #endregion

        #region Picker: GetInt32-style (CSPRNG, unbiased)

        /// <summary>
        /// Crypto-backed picker that emulates RandomNumberGenerator.GetInt32(n) via rejection sampling.
        /// Zero modulo bias; one 4B fetch per sample; more syscalls than buffered.
        /// </summary>
        private static class GIStyle
        {
            private static readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();

            /// <summary>
            /// Returns a uniform index in [0, n) using rejection sampling to avoid bias.
            /// </summary>
            public static int NextIndex(int n)
            {
                if (n <= 0) throw new ArgumentOutOfRangeException("n");
                uint un = (uint)n;
                uint limit = (uint.MaxValue / un) * un;

                while (true)
                {
                    byte[] b = new byte[4];
                    _rng.GetBytes(b);
                    uint r = ReadUInt32LE(b, 0);
                    if (r < limit) return (int)(r % un);
                }
            }

            /// <summary>Reads a little-endian UInt32 from a byte array.</summary>
            private static uint ReadUInt32LE(byte[] b, int o)
            {
                return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
            }
        }

        /// <summary>
        /// Adapter for the GetInt32-style picker to the benchmark's expected delegate signature.
        /// </summary>
        private static int PickerGetInt32Style(int[] pattern)
        {
            if (pattern.Length == 1) return pattern[0];
            return pattern[GIStyle.NextIndex(pattern.Length)];
        }
        #endregion

        #region Picker: Buffered CSPRNG (crypto, unbiased, amortized)

        /// <summary>
        /// Crypto RNG with per-thread 4KB buffer: amortizes kernel calls (fast) and remains unbiased via rejection.
        /// </summary>
        private static class CryptoBuffered
        {
            private static readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();
            [ThreadStatic] private static byte[] _buf;
            [ThreadStatic] private static int _ofs;

            /// <summary>
            /// Returns a buffered 32-bit value from the per-thread pool; refills as needed.
            /// </summary>
            private static uint NextU32()
            {
                if (_buf == null) _buf = new byte[4096]; // ~1024 draws per refill
                if (_ofs >= _buf.Length)
                {
                    _rng.GetBytes(_buf);
                    _ofs = 0;
                }
                uint v = (uint)(_buf[_ofs] | (_buf[_ofs + 1] << 8) | (_buf[_ofs + 2] << 16) | (_buf[_ofs + 3] << 24));
                _ofs += 4;
                return v;
            }

            /// <summary>
            /// Returns a uniform index in [0, n) using rejection sampling against the buffered source.
            /// </summary>
            public static int NextIndex(int n)
            {
                uint un = (uint)n;
                uint limit = (uint.MaxValue / un) * un;
                uint r;
                do { r = NextU32(); } while (r >= limit);
                return (int)(r % un);
            }
        }

        /// <summary>
        /// Adapter for buffered crypto picker to the benchmark's delegate signature.
        /// </summary>
        private static int PickerBuffered(int[] pattern)
        {
            if (pattern.Length == 1) return pattern[0];
            return pattern[CryptoBuffered.NextIndex(pattern.Length)];
        }
        #endregion

        #region Picker: Fast PRNG (PCG-ish, seeded from crypto, unbiased via rejection)

        /// <summary>
        /// Very fast non-crypto PRNG (LCG + xorshift-ish scramble), per-thread state seeded once from crypto.
        /// Uses rejection to remain unbiased when mapping to [0, n).
        /// </summary>
        private static class Fast
        {
            [ThreadStatic] private static uint s;

            /// <summary>
            /// Seeds the per-thread state from a crypto source; ensures nonzero odd state.
            /// </summary>
            private static void EnsureSeeded()
            {
                if (s != 0) return;
                byte[] b = new byte[4];
                using (var rng = RandomNumberGenerator.Create()) { rng.GetBytes(b); }
                uint seed = (uint)(b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24));
                if (seed == 0) seed = 1u;
                s = seed | 1u; // odd
            }

            /// <summary>
            /// Advances the PRNG and returns a scrambled 32-bit value.
            /// </summary>
            private static uint NextU()
            {
                EnsureSeeded();
                s = s * 747796405u + 2891336453u;  // LCG step
                uint x = s ^ (s >> 16);
                return x * 2246822519u;            // cheap scramble
            }

            /// <summary>
            /// Returns a uniform index in [0, n) using rejection sampling of the fast generator.
            /// </summary>
            public static int NextIndex(int n)
            {
                uint un = (uint)n;
                uint limit = (uint.MaxValue / un) * un;
                uint r; do { r = NextU(); } while (r >= limit);
                return (int)(r % un);
            }
        }

        /// <summary>
        /// Adapter for fast PRNG picker to the benchmark's delegate signature.
        /// </summary>
        private static int PickerFast(int[] pattern)
        {
            if (pattern.Length == 1) return pattern[0];
            return pattern[Fast.NextIndex(pattern.Length)];
        }
        #endregion
    }
}
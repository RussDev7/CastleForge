/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Security.Cryptography;
using System;

namespace CastleWallsMk2
{
    /// <summary>
    /// NOTE: Why not just use RandomNumberGenerator.GetInt32(minInclusive, maxExclusive)?
    /// ----------------------------------------------------------------------------------
    /// In modern .NET (Core 3.0+ / .NET 5+ / .NET 6+), GetInt32(...) is the ideal one-liner:
    /// it already produces a uniform, crypto-strong integer in the requested range.
    ///
    /// However, CastleMiner Z mod environments commonly target older .NET Framework builds
    /// (or older runtime profiles) where RandomNumberGenerator.GetInt32(...) may not exist.
    /// Using our own rejection-sampling implementation keeps the code portable across:
    /// - .NET Framework targets that only have RandomNumberGenerator.Create() + GetBytes(...).
    /// - Mixed mod-loader environments where the BCL surface area is limited.
    /// </summary>
    public static class CryptoRng
    {
        // Crypto RNG (CSPRNG):
        // - RandomNumberGenerator is the modern base API for cryptographic randomness.
        // - RandomNumberGenerator.Create() returns the platform-backed implementation
        //   (so we don't need to new up RNGCryptoServiceProvider directly).
        // - Kept as a single static instance to avoid per-call allocations.
        private static readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();

        /// <summary>
        /// Returns a crypto-strong random int in [min, max).
        /// Falls back to min if bounds are invalid (min >= max).
        /// </summary>
        public static int GenerateRandomNumber(int min, int max) => CryptoNext(min, max);

        /// <summary>
        /// Returns a crypto-strong random int in [minInclusive, maxInclusive].
        /// Falls back to minInclusive if bounds are invalid (minInclusive > maxInclusive).
        ///
        /// Note:
        /// - This wrapper exists because the core sampler is [minInclusive, maxExclusive).
        /// - So (69, 70) in the exclusive API can ONLY ever return 69.
        /// </summary>
        public static int GenerateRandomNumberInclusive(int minInclusive, int maxInclusive)
        {
            if (minInclusive > maxInclusive)  return minInclusive;
            if (minInclusive == maxInclusive) return minInclusive;

            // Convert inclusive max -> exclusive max using 64-bit to avoid overflow at int.MaxValue.
            long maxExclusive = (long)maxInclusive + 1L;
            return CryptoNext64(minInclusive, maxExclusive);
        }

        /// <summary>
        /// Crypto-strong uniform int in [minInclusive, maxExclusive),
        /// using rejection sampling to avoid modulo bias.
        /// </summary>
        private static int CryptoNext(int minInclusive, int maxExclusive)
        {
            // Bounds guard: "invalid or empty range" returns the lower bound.
            if (minInclusive >= maxExclusive)
                return minInclusive;

            uint range = (uint)(maxExclusive - minInclusive);

            // Rejection sampling threshold:
            // - Limit is the largest multiple of 'range' that fits in uint space.
            // - Any r >= limit would cause modulo bias, so we reroll.
            uint limit = uint.MaxValue - (uint.MaxValue % range);

            var buf = new byte[4];
            uint r;

            do
            {
                _rng.GetBytes(buf); // Fill with crypto-strong random bytes.
                r = BitConverter.ToUInt32(buf, 0);
            }
            while (r >= limit);

            return (int)(minInclusive + (r % range));
        }

        // Same algorithm as CryptoNext(...) but using 64-bit space so we can safely support:
        // - inclusive max conversions (maxInclusive + 1) even when maxInclusive == int.MaxValue
        // - very large ranges (up to 2^32 values) without uint overflow edge cases
        private static int CryptoNext64(int minInclusive, long maxExclusive)
        {
            // Bounds guard: "invalid or empty range" returns the lower bound.
            if ((long)minInclusive >= maxExclusive)
                return minInclusive;

            ulong range = (ulong)(maxExclusive - (long)minInclusive);

            // Rejection sampling threshold:
            // - Limit is the largest multiple of 'range' that fits in uint space.
            // - Any r >= limit would cause modulo bias, so we reroll.
            ulong limit = ulong.MaxValue - (ulong.MaxValue % range);

            var buf = new byte[8];
            ulong r;

            do
            {
                _rng.GetBytes(buf); // Fill with crypto-strong random bytes.
                r = BitConverter.ToUInt64(buf, 0);
            }
            while (r >= limit);

            return (int)((long)minInclusive + (long)(r % range));
        }
    }
}
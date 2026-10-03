using System;
using System.Globalization;

namespace CrawlOnline.Protocol
{
    public struct GameBuildFingerprint : IEquatable<GameBuildFingerprint>
    {
        public ulong Part0;
        public ulong Part1;
        public ulong Part2;
        public ulong Part3;

        public bool IsEmpty
        {
            get { return Part0 == 0 && Part1 == 0 && Part2 == 0 && Part3 == 0; }
        }

        public static GameBuildFingerprint Parse(string sha256)
        {
            GameBuildFingerprint fingerprint;
            if (!TryParse(sha256, out fingerprint))
                throw new FormatException("Game build fingerprint must be a non-zero SHA-256 value.");
            return fingerprint;
        }

        public static bool TryParse(string sha256, out GameBuildFingerprint fingerprint)
        {
            fingerprint = new GameBuildFingerprint();
            if (sha256 == null || sha256.Length != 64) return false;
            if (!ulong.TryParse(sha256.Substring(0, 16), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out fingerprint.Part0) ||
                !ulong.TryParse(sha256.Substring(16, 16), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out fingerprint.Part1) ||
                !ulong.TryParse(sha256.Substring(32, 16), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out fingerprint.Part2) ||
                !ulong.TryParse(sha256.Substring(48, 16), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out fingerprint.Part3) || fingerprint.IsEmpty)
            {
                fingerprint = new GameBuildFingerprint();
                return false;
            }
            return true;
        }

        public static bool MatchesMetadata(GameBuildFingerprint expected, string metadata)
        {
            GameBuildFingerprint actual;
            return !expected.IsEmpty && TryParse(metadata, out actual) && actual == expected;
        }

        public bool Equals(GameBuildFingerprint other)
        {
            return Part0 == other.Part0 && Part1 == other.Part1 &&
                   Part2 == other.Part2 && Part3 == other.Part3;
        }

        public override bool Equals(object obj)
        {
            return obj is GameBuildFingerprint && Equals((GameBuildFingerprint)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Part0.GetHashCode();
                hash = (hash * 397) ^ Part1.GetHashCode();
                hash = (hash * 397) ^ Part2.GetHashCode();
                return (hash * 397) ^ Part3.GetHashCode();
            }
        }

        public override string ToString()
        {
            return Part0.ToString("x16", CultureInfo.InvariantCulture) +
                   Part1.ToString("x16", CultureInfo.InvariantCulture) +
                   Part2.ToString("x16", CultureInfo.InvariantCulture) +
                   Part3.ToString("x16", CultureInfo.InvariantCulture);
        }

        public static bool operator ==(GameBuildFingerprint left, GameBuildFingerprint right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GameBuildFingerprint left, GameBuildFingerprint right)
        {
            return !left.Equals(right);
        }
    }
}

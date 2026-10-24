using System.Text;

namespace GfxStudio.Core.Randomness.Seeding;

internal static class SeedMixer
{
    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    public static ulong Mix(ulong value)
    {
        unchecked
        {
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }

    public static ulong HashString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = FnvOffsetBasis;
        unchecked
        {
            foreach (var item in bytes)
            {
                hash ^= item;
                hash *= FnvPrime;
            }
        }

        return hash;
    }

    public static ulong DeriveForkSeed(ulong seed, string scopeKey) =>
        Mix(seed ^ HashString(scopeKey) ^ 0x452821E638D01377UL);
}

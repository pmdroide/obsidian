using System;

namespace Engine.Voxels
{
    /// <summary>
    /// Seeded gradient (Perlin) noise for the terrain generator. The same seed always gives the same values,
    /// on every machine, so a scene's voxel world never changes between runs. Thread-safe once built.
    /// </summary>
    public sealed class VoxelNoise
    {
        private readonly int[] _perm = new int[512];
        private readonly int _seed;

        public VoxelNoise(int seed)
        {
            _seed = seed;
            var p = new int[256];
            for (int i = 0; i < 256; i++) p[i] = i;
            // Fisher-Yates with our own generator: System.Random's sequence isn't guaranteed across runtimes.
            uint state = (uint)seed * 747796405u + 2891336453u;
            for (int i = 255; i > 0; i--)
            {
                state = state * 747796405u + 2891336453u;
                int j = (int)(Mix(state) % (uint)(i + 1));
                (p[i], p[j]) = (p[j], p[i]);
            }
            for (int i = 0; i < 512; i++) _perm[i] = p[i & 255];
        }

        /// <summary>A well-mixed 32-bit hash of the seed and a cell; the basis for per-block randomness.</summary>
        public uint Hash(int x, int y, int z = 0)
        {
            uint h = (uint)_seed * 0x9E3779B1u;
            h ^= (uint)x * 0x85EBCA77u;
            h = Mix(h);
            h ^= (uint)y * 0xC2B2AE3Du;
            h = Mix(h);
            h ^= (uint)z * 0x27D4EB2Fu;
            return Mix(h);
        }

        /// <summary><see cref="Hash"/> as 0..1.</summary>
        public float Random01(int x, int y, int z = 0) => (Hash(x, y, z) & 0xFFFFFF) / 16777216f;

        private static uint Mix(uint h)
        {
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            h *= 0x846CA68Bu;
            h ^= h >> 16;
            return h;
        }

        private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float Grad(int hash, float x, float y)
        {
            switch (hash & 7)
            {
                case 0: return x + y;
                case 1: return -x + y;
                case 2: return x - y;
                case 3: return -x - y;
                case 4: return x;
                case 5: return -x;
                case 6: return y;
                default: return -y;
            }
        }

        private static float Grad(int hash, float x, float y, float z)
        {
            int h = hash & 15;
            float u = h < 8 ? x : y;
            float v = h < 4 ? y : h == 12 || h == 14 ? x : z;
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }

        /// <summary>2D gradient noise, roughly -1..1.</summary>
        public float Noise(float x, float y)
        {
            int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
            float xf = x - xi, yf = y - yi;
            xi &= 255;
            yi &= 255;
            float u = Fade(xf), v = Fade(yf);
            int aa = _perm[_perm[xi] + yi], ab = _perm[_perm[xi] + yi + 1];
            int ba = _perm[_perm[xi + 1] + yi], bb = _perm[_perm[xi + 1] + yi + 1];
            float result = Lerp(
                Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1, yf), u),
                Lerp(Grad(ab, xf, yf - 1), Grad(bb, xf - 1, yf - 1), u), v);
            return result * 0.7f;
        }

        /// <summary>3D gradient noise, roughly -1..1.</summary>
        public float Noise(float x, float y, float z)
        {
            int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y), zi = (int)MathF.Floor(z);
            float xf = x - xi, yf = y - yi, zf = z - zi;
            xi &= 255;
            yi &= 255;
            zi &= 255;
            float u = Fade(xf), v = Fade(yf), w = Fade(zf);
            int a = _perm[xi] + yi, aa = _perm[a] + zi, ab = _perm[a + 1] + zi;
            int b = _perm[xi + 1] + yi, ba = _perm[b] + zi, bb = _perm[b + 1] + zi;
            return Lerp(
                Lerp(Lerp(Grad(_perm[aa], xf, yf, zf), Grad(_perm[ba], xf - 1, yf, zf), u),
                    Lerp(Grad(_perm[ab], xf, yf - 1, zf), Grad(_perm[bb], xf - 1, yf - 1, zf), u), v),
                Lerp(Lerp(Grad(_perm[aa + 1], xf, yf, zf - 1), Grad(_perm[ba + 1], xf - 1, yf, zf - 1), u),
                    Lerp(Grad(_perm[ab + 1], xf, yf - 1, zf - 1), Grad(_perm[bb + 1], xf - 1, yf - 1, zf - 1), u), v),
                w);
        }

        /// <summary>Fractal sum of <paramref name="octaves"/> layers, each twice as fine and half as strong; roughly -1..1.</summary>
        public float Fbm(float x, float y, int octaves)
        {
            float sum = 0, amplitude = 1, norm = 0;
            for (int i = 0; i < octaves; i++)
            {
                // Offset each octave so their lattices don't line up at the origin.
                sum += Noise(x + i * 17.31f, y - i * 9.17f) * amplitude;
                norm += amplitude;
                amplitude *= 0.5f;
                x *= 2;
                y *= 2;
            }
            return sum / norm;
        }

        /// <summary>Ridged fractal: sharp crests where the noise crosses zero; 0..1.</summary>
        public float Ridged(float x, float y, int octaves)
        {
            float sum = 0, amplitude = 1, norm = 0, weight = 1;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1 - MathF.Abs(Noise(x + i * 31.7f, y + i * 12.9f));
                n *= n * weight;
                weight = Math.Clamp(n * 1.5f, 0, 1);
                sum += n * amplitude;
                norm += amplitude;
                amplitude *= 0.5f;
                x *= 2;
                y *= 2;
            }
            return sum / norm;
        }
    }
}

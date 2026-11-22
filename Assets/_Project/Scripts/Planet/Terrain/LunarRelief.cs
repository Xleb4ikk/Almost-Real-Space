using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace Galilego.Universe
{
    /// <summary>
    /// Blittable parameters of the lunar-type relief generator (copied into
    /// TerrainNoiseParams, therefore usable inside Burst jobs). Built from
    /// LunarReliefSettings.ToParams(seed); all values are in metres / radians-free units.
    /// </summary>
    public struct LunarParams
    {
        public int Enabled;
        public int Seed;
        /// <summary>Hash of all content, so the renderer can detect live edits cheaply.</summary>
        public long ContentHash;

        public double Radius;
        public double MeanOffset;

        // ---- global figure ----
        public double GlobalRelief;
        public double GlobalFrequency;
        public double NearFarOffset;
        public double NearX, NearY, NearZ;
        public double RoughAmp;
        public double RoughBaseFreq;
        public double RoughGain;
        public int RoughOctaves;

        // ---- crater field ----
        public double CraterDensity;
        public double ProductionSlope;
        public double BasinDensityScale;
        public double ComplexTransition;
        public double FreshnessExp;
        public double MareAge;
        public int FirstCells;
        public int LevelCount;
        public double RayDensity;

        // ---- giant basin (South-Pole-Aitken analogue) ----
        public double GiantD;
        public double GiantX, GiantY, GiantZ;
        public double GiantDepthScale;

        // ---- maria ----
        public double MareThreshold;
        public double MareSharp;
        public double MareFreq;
        public double MareNearBias;
        public double MareLevel;
        public double MareLevelVar;
        public double MareLevelFreq;
        public double MareSmooth;
        public double MareUndul;
        public double MareDrop;
        public double WrinkleH;
        public double WrinkleFreq;
        public double RilleDepth;
        public double RilleFreq;
        public double RilleAmount;
        public double DomeH;

        // ---- extras ----
        public double ScarpH;
        public double ScarpFreq;
        public double ScarpAmount;
        public double RegolithAmp;
        public double RegolithBaseFreq;
        public int RegolithOctaves;

        // ---- albedo (written to the colour-detail channel) ----
        public double MareDark;
        public double HighlandVar;
        public double FreshBright;
    }

    /// <summary>
    /// Procedural relief of an airless, heavily cratered body (Moon, Mercury, ancient icy moons).
    /// Pure statics on double3/math: no allocations, no managed references, Burst-safe.
    ///
    /// Model (all heights in metres above the mean radius):
    ///  1. global figure: low-frequency swell, near/far-side offset, fractal highland roughness;
    ///  2. crater population: log-spaced size levels on a cube-sphere hash grid, power-law size
    ///     distribution, simple -> complex -> peak-ring -> multi-ring morphology by diameter,
    ///     per-crater age (degradation), non-circular rims, terraces, central peaks, ejecta;
    ///  3. giant basin + lava flooding: craters older than MareAge are "pre-mare"; maria are the
    ///     pre-mare topography flooded up to a level surface, so coastlines follow real contours
    ///     and flooded craters keep their rims;
    ///  4. post-mare detail: young craters, wrinkle ridges, sinuous rilles, domes, scarps, regolith.
    /// The same craters yield an albedo field (dark maria, bright fresh ejecta and rays).
    /// </summary>
    public static class LunarRelief
    {
        private const double HalfPi = 1.5707963267948966d;
        private const double PiOver4 = 0.7853981633974483d;
        private const double FourOverPi = 1.2732395447351628d;
        private const double TwoPi = 6.283185307179586d;

        // ------------------------------------------------------------------ hashing / noise

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Mix(uint h)
        {
            unchecked
            {
                h ^= h >> 16;
                h *= 0x7feb352du;
                h ^= h >> 15;
                h *= 0x846ca68bu;
                h ^= h >> 16;
                return h;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Hash3(int x, int y, int z, uint salt)
        {
            unchecked
            {
                uint h = (salt * 0x9E3779B1u) + ((uint)x * 0x85EBCA6Bu);
                h = Mix(h ^ ((uint)y * 0xC2B2AE35u));
                h = Mix(h ^ ((uint)z * 0x27D4EB2Fu));
                return h;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double U01(uint h)
        {
            return (h >> 8) * (1d / 16777216d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Next(uint h)
        {
            unchecked
            {
                return Mix(h + 0x9E3779B9u);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Sm(double t)
        {
            if (t <= 0d) return 0d;
            if (t >= 1d) return 1d;
            return t * t * (3d - (2d * t));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double SMin(double a, double b, double k)
        {
            if (!(k > 0d)) return math.min(a, b);
            double h = math.max(k - math.abs(a - b), 0d) / k;
            return math.min(a, b) - (h * h * k * 0.25d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double SMax(double a, double b, double k)
        {
            if (!(k > 0d)) return math.max(a, b);
            double h = math.max(k - math.abs(a - b), 0d) / k;
            return math.max(a, b) + (h * h * k * 0.25d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Grad(uint h, double x, double y, double z)
        {
            uint k = h & 15u;
            double u = k < 8u ? x : y;
            double v = k < 4u ? y : ((k == 12u || k == 14u) ? x : z);
            return ((k & 1u) == 0u ? u : -u) + ((k & 2u) == 0u ? v : -v);
        }

        /// <summary>Gradient noise, roughly in [-1, 1] (RMS ~0.3).</summary>
        private static double Noise3(double3 p, uint salt)
        {
            double fx = math.floor(p.x);
            double fy = math.floor(p.y);
            double fz = math.floor(p.z);
            int ix = unchecked((int)(long)fx);
            int iy = unchecked((int)(long)fy);
            int iz = unchecked((int)(long)fz);
            double x = p.x - fx;
            double y = p.y - fy;
            double z = p.z - fz;
            double u = x * x * x * ((x * ((x * 6d) - 15d)) + 10d);
            double v = y * y * y * ((y * ((y * 6d) - 15d)) + 10d);
            double w = z * z * z * ((z * ((z * 6d) - 15d)) + 10d);

            double n000 = Grad(Hash3(ix, iy, iz, salt), x, y, z);
            double n100 = Grad(Hash3(ix + 1, iy, iz, salt), x - 1d, y, z);
            double n010 = Grad(Hash3(ix, iy + 1, iz, salt), x, y - 1d, z);
            double n110 = Grad(Hash3(ix + 1, iy + 1, iz, salt), x - 1d, y - 1d, z);
            double n001 = Grad(Hash3(ix, iy, iz + 1, salt), x, y, z - 1d);
            double n101 = Grad(Hash3(ix + 1, iy, iz + 1, salt), x - 1d, y, z - 1d);
            double n011 = Grad(Hash3(ix, iy + 1, iz + 1, salt), x, y - 1d, z - 1d);
            double n111 = Grad(Hash3(ix + 1, iy + 1, iz + 1, salt), x - 1d, y - 1d, z - 1d);

            double x00 = n000 + (u * (n100 - n000));
            double x10 = n010 + (u * (n110 - n010));
            double x01 = n001 + (u * (n101 - n001));
            double x11 = n011 + (u * (n111 - n011));
            double y0 = x00 + (v * (x10 - x00));
            double y1 = x01 + (v * (x11 - x01));
            return (y0 + (w * (y1 - y0))) * 0.8d;
        }

        /// <summary>fBm of Noise3; octaves are axis-swapped so the lattice does not align. Normalised by the weight sum.</summary>
        private static double Fbm(double3 p, int octaves, double gain, uint salt)
        {
            double sum = 0d;
            double amp = 1d;
            double norm = 0d;
            int n = octaves < 1 ? 1 : octaves;
            for (int o = 0; o < n; o++)
            {
                sum += amp * Noise3(p, salt + (uint)(o * 7919));
                norm += amp;
                amp *= gain;
                p = new double3((p.y * 2d) + 19.19d, (p.z * 2d) + 7.47d, (p.x * 2d) + 3.13d);
            }

            return sum / norm;
        }

        // ------------------------------------------------------------------ cube-sphere grid

        private struct FaceCoord
        {
            public int Ok;
            public int Axis;
            public int Id;
            public double Sign;
            public double Sa;
            public double Sb;
        }

        private static FaceCoord MakeFace(double3 d, int axis)
        {
            FaceCoord f = default(FaceCoord);
            double c;
            double u;
            double v;
            double sg;
            if (axis == 0)
            {
                sg = d.x >= 0d ? 1d : -1d; c = math.abs(d.x); u = d.y; v = d.z;
            }
            else if (axis == 1)
            {
                sg = d.y >= 0d ? 1d : -1d; c = math.abs(d.y); u = d.z; v = d.x;
            }
            else
            {
                sg = d.z >= 0d ? 1d : -1d; c = math.abs(d.z); u = d.x; v = d.y;
            }

            f.Axis = axis;
            f.Sign = sg;
            f.Id = (axis * 2) + (sg > 0d ? 0 : 1);
            if (c < 1e-9d)
            {
                return f;
            }

            double a = u / c;
            double b = v / c;
            if (math.abs(a) > 2.2d || math.abs(b) > 2.2d)
            {
                return f;
            }

            f.Sa = math.atan(a) * FourOverPi;
            f.Sb = math.atan(b) * FourOverPi;
            f.Ok = 1;
            return f;
        }

        private static double3 FaceDir(int axis, double sign, double a, double b)
        {
            if (axis == 0) return math.normalize(new double3(sign, a, b));
            if (axis == 1) return math.normalize(new double3(b, sign, a));
            return math.normalize(new double3(a, b, sign));
        }

        // ------------------------------------------------------------------ public entry

        /// <summary>Height in metres above the mean radius. albedo: ~[-1,1], +dark / -bright (only if wantAlbedo).</summary>
        public static double Sample(in LunarParams p, double3 dir, bool wantAlbedo, out double albedo)
        {
            albedo = 0d;
            uint seedSalt = unchecked((uint)p.Seed * 2654435761u) + 0x51ED27u;
            double3 near = new double3(p.NearX, p.NearY, p.NearZ);
            double R = p.Radius;

            // ---- 1. global figure
            double hBase = (-p.NearFarOffset * math.dot(dir, near))
                + (Fbm((dir * p.GlobalFrequency) + SaltVec(seedSalt, 1u), 3, 0.5d, seedSalt + 11u) * 2d * p.GlobalRelief)
                + (Fbm((dir * p.RoughBaseFreq) + SaltVec(seedSalt, 2u), p.RoughOctaves, p.RoughGain, seedSalt + 23u) * 2d * p.RoughAmp);

            // ---- 2. crater population
            double old = 0d;
            double young = 0d;
            double bright = 0d;
            int wa = wantAlbedo ? 1 : 0;

            if (p.GiantD > 0d)
            {
                double3 g = math.normalize(new double3(p.GiantX, p.GiantY, p.GiantZ));
                double rc = 0.5d * p.GiantD;
                double ang = 2d * math.asin(math.min(1d, 0.5d * math.length(dir - g)));
                double r = R * ang;
                if (r < 3d * rc)
                {
                    AddCrater(p, dir, g, p.GiantD, r, 0d, 0x2545F491u ^ seedSalt, 3d, 0, 0, p.GiantDepthScale,
                        ref old, ref young, ref bright);
                }
            }

            FaceCoord f0 = MakeFace(dir, 0);
            FaceCoord f1 = MakeFace(dir, 1);
            FaceCoord f2 = MakeFace(dir, 2);

            double faceWidth = HalfPi * R;
            double rho = p.CraterDensity * math.pow((faceWidth / p.FirstCells) / 20000d, 2d - p.ProductionSlope);
            double rhoStep = math.pow(2d, p.ProductionSlope - 2d);
            for (int k = 0; k < p.LevelCount; k++)
            {
                int N = p.FirstCells << k;
                double S = faceWidth / N;
                double rk = rho;
                if (S >= 300000d) rk *= p.BasinDensityScale;
                if (rk > 1d) rk = 1d;
                ProcessLevel(p, dir, f0, f1, f2, seedSalt, k, N, rk, 0.5d, 1d, -1d, 0, wa, ref old, ref young, ref bright);
                rho *= rhoStep;
            }

            if (p.RayDensity > 0d)
            {
                // rare, fresh, large craters with long ray systems (Tycho / Copernicus analogues)
                ProcessLevel(p, dir, f0, f1, f2, seedSalt, 40, 4, p.RayDensity, 0.04d, 0.12d, 0.97d, 1, wa, ref old, ref young, ref bright);
            }

            double hPre = hBase + old;

            // ---- 3. maria: flood the pre-mare topography up to a lava level
            double hFlood = hPre;
            double lava = 0d;
            double M = 0d;
            double floodDepth = -1e9d;
            if (p.MareThreshold < 4d)
            {
                double mn = Fbm((dir * p.MareFreq) + SaltVec(seedSalt, 3u), 3, 0.5d, seedSalt + 31u)
                    + (p.MareNearBias * math.dot(dir, near));
                M = Sm(((mn - p.MareThreshold) / (2d * p.MareSharp)) + 0.5d);
                double lv = Fbm((dir * p.MareLevelFreq) + SaltVec(seedSalt, 4u), 2, 0.5d, seedSalt + 37u);
                double und = Fbm((dir * (R / 45000d)) + SaltVec(seedSalt, 5u), 2, 0.5d, seedSalt + 41u);
                double Leff = p.MareLevel + (p.MareLevelVar * 2d * lv) + (p.MareUndul * 2d * und) - ((1d - M) * p.MareDrop);
                hFlood = SMax(hPre, Leff, p.MareSmooth);
                floodDepth = Leff - hPre;
                lava = M * Sm(floodDepth / 120d);
            }

            // ---- 4. post-mare detail
            double h = hFlood + young;

            if (lava > 0.0001d)
            {
                double deep = Sm((floodDepth - 150d) / 1100d);
                if (p.WrinkleH > 0d && deep > 0d)
                {
                    double nw = Noise3((dir * p.WrinkleFreq) + SaltVec(seedSalt, 6u), seedSalt + 53u);
                    double mod = 0.6d + (0.4d * (Noise3((dir * p.WrinkleFreq * 0.2d) + SaltVec(seedSalt, 7u), seedSalt + 59u) + 0.5d));
                    double crest = math.exp(-(nw * nw) / 0.0035d);
                    double arch = math.exp(-(nw * nw) / 0.04d);
                    h += p.WrinkleH * deep * M * mod * ((0.75d * crest) + (0.35d * arch));
                }

                if (p.RilleDepth > 0d && p.RilleAmount > 0d)
                {
                    double rsel = Sm((Noise3((dir * p.RilleFreq * 0.12d) + SaltVec(seedSalt, 8u), seedSalt + 61u) - (0.55d - p.RilleAmount)) / 0.12d);
                    if (rsel > 0d)
                    {
                        double nr = Noise3((dir * p.RilleFreq) + SaltVec(seedSalt, 9u), seedSalt + 67u);
                        double edge = M * (1d - Sm((floodDepth - 300d) / 1200d));
                        h -= p.RilleDepth * rsel * edge * math.exp(-(nr * nr) / 0.0009d);
                    }
                }

                if (p.DomeH > 0d)
                {
                    double nd = Noise3((dir * (R / 9000d)) + SaltVec(seedSalt, 10u), seedSalt + 71u);
                    h += p.DomeH * lava * Sm((nd - 0.28d) / 0.12d);
                }
            }

            if (p.ScarpH > 0d)
            {
                double ns = Noise3((dir * p.ScarpFreq) + SaltVec(seedSalt, 11u), seedSalt + 73u);
                double sel = Sm((Noise3((dir * p.ScarpFreq * 0.18d) + SaltVec(seedSalt, 12u), seedSalt + 79u) - (0.4d - p.ScarpAmount)) / 0.15d);
                h += p.ScarpH * sel * Sm((ns / 0.05d * 0.5d) + 0.5d);
            }

            if (p.RegolithAmp > 0d)
            {
                h += p.RegolithAmp * 2d * Fbm((dir * p.RegolithBaseFreq) + SaltVec(seedSalt, 13u), p.RegolithOctaves, 0.55d, seedSalt + 83u);
            }

            h += p.MeanOffset;

            if (wantAlbedo)
            {
                double var = p.HighlandVar * 2d * Fbm((dir * (R / 220000d)) + SaltVec(seedSalt, 14u), 3, 0.5d, seedSalt + 89u);
                double a = (p.MareDark * lava) + var - (p.FreshBright * math.min(1d, bright));
                albedo = math.clamp(a, -1d, 1d);
            }

            return h;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double3 SaltVec(uint seedSalt, uint k)
        {
            uint a = Mix(seedSalt + (k * 0x9E3779B1u));
            uint b = Next(a);
            uint c = Next(b);
            return new double3(U01(a) * 500d, U01(b) * 500d, U01(c) * 500d);
        }

        // ------------------------------------------------------------------ crater levels

        private static void ProcessLevel(
            in LunarParams p, double3 dir, FaceCoord f0, FaceCoord f1, FaceCoord f2, uint seedSalt,
            int level, int N, double rho, double dLo, double dHi, double fixedAge, int rayMode, int wantAlbedo,
            ref double old, ref double young, ref double bright)
        {
            if (!(rho > 0d)) return;

            double R = p.Radius;
            double S = HalfPi * R / N;
            double slope = math.max(p.ProductionSlope, 0.5d);
            double ratio = dHi / dLo;
            double q = math.pow(ratio, -slope);
            double invSlope = -1d / slope;
            double dMean = 0.5d * (dLo + dHi);

            for (int fi = 0; fi < 3; fi++)
            {
                FaceCoord f = fi == 0 ? f0 : (fi == 1 ? f1 : f2);
                if (f.Ok == 0) continue;
                double xa = (f.Sa + 1d) * 0.5d * N;
                double xb = (f.Sb + 1d) * 0.5d * N;
                if (xa < -1.5d || xa > N + 1.5d || xb < -1.5d || xb > N + 1.5d) continue;
                int ia = (int)math.floor(xa);
                int ib = (int)math.floor(xb);

                for (int da = -1; da <= 1; da++)
                {
                    int ci = ia + da;
                    if (ci < 0 || ci >= N) continue;
                    for (int db = -1; db <= 1; db++)
                    {
                        int cj = ib + db;
                        if (cj < 0 || cj >= N) continue;

                        uint h = Hash3(ci, cj, (f.Id * 97) + (level * 7) + 13, seedSalt);
                        double sa = -1d + ((2d * (ci + 0.5d)) / N);
                        double sb = -1d + ((2d * (cj + 0.5d)) / N);
                        double w = 1d - (0.293d * ((sa * sa) + (sb * sb))) + (0.356d * sa * sa * sb * sb);
                        if (U01(h) >= rho * w) continue;

                        h = Next(h);
                        double xcA = ci + U01(h);
                        h = Next(h);
                        double xcB = cj + U01(h);
                        h = Next(h);
                        double D = S * dLo * math.pow(1d - (U01(h) * (1d - q)), invSlope);
                        double Rc = 0.5d * D;
                        double cap = 1.6d + (1.4d * Sm((D - 50000d) / 250000d));
                        double rhoMax = math.clamp(0.8d * S / Rc, 1.15d, cap);
                        double reach = rhoMax * Rc;
                        if (rayMode == 1 && wantAlbedo == 1) reach = 0.85d * S;

                        double dxa = xcA - xa;
                        double dxb = xcB - xb;
                        double rc2 = (reach / S) * 1.6d;
                        if ((dxa * dxa) + (dxb * dxb) > rc2 * rc2) continue;

                        double3 cdir = FaceDir(
                            f.Axis, f.Sign,
                            math.tan(PiOver4 * (((2d * xcA) / N) - 1d)),
                            math.tan(PiOver4 * (((2d * xcB) / N) - 1d)));
                        double ang = 2d * math.asin(math.min(1d, 0.5d * math.length(dir - cdir)));
                        double r = R * ang;
                        if (r >= reach) continue;

                        h = Next(h);
                        double ageU = fixedAge >= 0d ? fixedAge : U01(h);
                        h = Next(h);
                        AddCrater(p, dir, cdir, D, r, ageU, h, rhoMax, rayMode, wantAlbedo, 1d,
                            ref old, ref young, ref bright);
                    }
                }
            }

            // (dMean kept for readability of the size model; not otherwise used)
            if (dMean < 0d) old += 0d;
        }

        // ------------------------------------------------------------------ one crater

        private static void AddCrater(
            in LunarParams p, double3 dir, double3 cdir, double D, double r, double ageU, uint rnd,
            double rhoMax, int rayMode, int wantAlbedo, double depthScale,
            ref double old, ref double young, ref double bright)
        {
            double R = p.Radius;
            double Rc = 0.5d * D;
            double rho = r / Rc;

            double Dt = p.ComplexTransition;
            double sc = Dt / 18000d;
            double Dkm = (D / sc) * 0.001d;
            double cw = Sm(((D / Dt) - 0.65d) / 0.7d);
            double ringW = Sm(((D / (8d * Dt)) - 0.8d) / 0.45d);
            double multiW = Sm(((D / (17d * Dt)) - 0.8d) / 0.4d);

            double dKm = math.exp(math.lerp(
                math.log(0.196d * math.pow(Dkm, 1.01d)),
                math.log(1.044d * math.pow(Dkm, 0.301d)), cw));
            double d = math.min(dKm * 1000d * sc, 0.22d * D) * depthScale;
            double hr = math.lerp(0.036d * math.pow(Dkm, 1.014d), 0.236d * math.pow(Dkm, 0.399d), cw) * 1000d * sc;

            double m = 1d - math.pow(ageU, p.FreshnessExp);
            m = math.clamp(m, 0d, 1d);
            d *= 1d - (0.65d * m);
            hr *= 1d - (0.8d * m);

            uint h = rnd;
            double u1 = U01(h); h = Next(h);
            double u2 = U01(h); h = Next(h);
            double u3 = U01(h); h = Next(h);
            double u4 = U01(h);

            // non-circular rim and unique hummocks: noise anchored to the crater, not to the sample
            double invRc = R / Rc;
            double3 off = new double3(u1 * 300d, u2 * 300d, u3 * 300d);
            double nRim = Noise3((dir * (invRc * 0.8d)) + off, 101u);
            double eps = 0.04d + (0.08d * cw) + (0.07d * m);
            double re = rho * (1d + (eps * nRim));
            if (re < 0d) re = 0d;

            // local frame (crater radii units) for peaks, azimuth sculpture and rays
            double xt = 0d;
            double yt = 0d;
            double phi = 0d;
            bool needFrame = cw > 0.3d || (rayMode == 1 && wantAlbedo == 1);
            if (needFrame)
            {
                double3 helper = math.abs(cdir.z) < 0.9d ? new double3(0d, 0d, 1d) : new double3(1d, 0d, 0d);
                double3 e1 = math.normalize(math.cross(cdir, helper));
                double3 e2 = math.cross(cdir, e1);
                xt = math.dot(dir, e1) * invRc;
                yt = math.dot(dir, e2) * invRc;
                phi = math.atan2(yt, xt);
            }

            // ---- cavity
            double bowl = -d + ((d + hr) * re * re);

            double rf = 0.30d + (0.20d * cw) + (0.10d * ringW);
            // slump terraces: few, wobbling, and present only in some sectors (not vinyl-record grooves)
            double nTer = Noise3((dir * (invRc * 2.2d)) + off, 211u);
            double nBlk = Noise3((dir * (invRc * 1.1d)) + off, 227u);
            double t = math.clamp(((re + (0.07d * nTer)) - rf) / (1d - rf), 0d, 1d);
            double w0 = math.pow(t, 1.35d);
            double nT = 2d + math.floor(u4 * 2.5d);
            double st = t * nT;
            double fl = math.floor(st);
            double stair = (fl + Sm(((st - fl) - 0.25d) / 0.5d)) / nT;
            double terr = math.clamp(0.45d + (1.6d * nBlk), 0d, 1d) * 0.6d * (1d - m);
            double W = math.lerp(w0, stair, terr);
            double complexC = -d + ((d + hr) * W);

            double c = math.lerp(bowl, complexC, cw);

            if (multiW > 0.001d)
            {
                double T = (0.28d * (1d - Sm(((re - 0.88d) / 0.08d))))
                    + (0.26d * (1d - Sm(((re - 0.63d) / 0.08d))))
                    + (0.20d * (1d - Sm(((re - 0.46d) / 0.08d))))
                    + (0.26d * (1d - Sm(((re - 0.29d) / 0.08d))));
                double crests = hr * ((0.6d * G(re, 0.91d, 0.03d)) + (0.5d * G(re, 0.65d, 0.03d))
                    + (0.4d * G(re, 0.48d, 0.03d)) + (0.3d * G(re, 0.31d, 0.03d)));
                double cm = (hr * Sm((re - 0.8d) / 0.2d)) - (d * T) + crests;
                c = math.lerp(c, cm, multiW);
            }

            double a = math.min(c, hr);

            // ---- ejecta blanket
            double s = 0.02d + (0.35d * m);
            double e = hr * math.pow((1d + (s * s)) / ((re * re) + (s * s)), 1.5d);
            if (e > hr) e = hr;
            if (D > 1000d)
            {
                double nH = Noise3((dir * (invRc * 2.5d)) + off, 131u);
                e *= 1d + (0.30d * nH);
            }

            if (multiW > 0.02d)
            {
                double nH2 = Noise3((dir * (invRc * 1.5d)) + off, 151u);
                e *= 1d + (multiW * 0.45d * math.cos((14d * phi) + (3d * nH2)));
            }

            double win = 1d - Sm((rho - (0.55d * rhoMax)) / (0.45d * rhoMax));
            double b = math.min(hr, e) * win;

            double hc = SMin(a, b, hr * (0.08d + (0.9d * m)) + 1e-6d);

            // ---- central peak / peak ring
            if (cw > 0.3d)
            {
                double wp = cw * (1d - ringW) * (1d - (0.85d * m));
                if (wp > 0.001d)
                {
                    double hp = 0.30d * d * (0.7d + (0.6d * u2));
                    double rp = 0.14d + (0.12d * u3);
                    double o1x = 0.30d * (u1 - 0.5d);
                    double o1y = 0.30d * (u2 - 0.5d);
                    double o2x = 0.30d * (u3 - 0.5d);
                    double o2y = 0.30d * (u4 - 0.5d);
                    hc += wp * hp * (Pk(xt, yt, rp)
                        + (0.6d * Pk(xt - o1x, yt - o1y, rp * 0.7d))
                        + (0.45d * Pk(xt - o2x, yt - o2y, rp * 0.6d)));
                }
            }

            if (ringW > 0.01d)
            {
                double wr = ringW * (1d - (0.6d * multiW)) * (1d - (0.6d * m));
                double n2 = Noise3((dir * (invRc * 5d)) + off, 171u) + 0.5d;
                hc += wr * 0.42d * d * G(re, 0.5d, 0.075d) * math.clamp(0.6d + (0.5d * n2), 0.2d, 1.1d);
            }

            if (ageU < p.MareAge) old += hc;
            else young += hc;

            if (wantAlbedo == 1 && ageU > 0.78d)
            {
                double fr = Sm((ageU - 0.78d) / 0.22d);
                double rhoAlb = rayMode == 1 ? (rhoMax + 1e-9d) : rhoMax;
                double bb = 0.6d * (1d - Sm((rho - 0.9d) / math.max(0.2d, math.min(rhoAlb, 3d) - 0.9d)));
                if (rayMode == 1 && rho > 1d)
                {
                    double reachRho = (0.85d * (HalfPi * R / 4d)) / Rc;
                    double nr = Noise3(new double3(math.cos(phi) * 5d, math.sin(phi) * 5d, u1 * 50d), 191u);
                    double sr = math.clamp((nr * 1.8d) + 0.35d, 0d, 1d);
                    double rr = math.min(1d, math.pow(1.6d / rho, 1.1d));
                    double fade = 1d - Sm((rho - (0.55d * reachRho)) / (0.45d * reachRho));
                    bb = math.max(bb, (0.2d + (0.8d * sr * sr)) * rr * fade);
                }

                // huge basins do not keep a bright halo for long
                double dk = D / 80000d;
                bright += fr * bb / (1d + (dk * dk));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double G(double x, double c, double width)
        {
            double z = (x - c) / width;
            return math.exp(-z * z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Pk(double x, double y, double rp)
        {
            double q = ((x * x) + (y * y)) / (rp * rp);
            if (q >= 1d) return 0d;
            double v = 1d - q;
            return v * math.sqrt(v);
        }
    }
}

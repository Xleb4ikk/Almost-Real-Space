using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace GalilegoPhysicsTests.Support.Artifacts
{
    internal static class ArtifactWriter
    {
        private static string repositoryRoot;

        public static string RepositoryRoot
        {
            get
            {
                if (repositoryRoot != null)
                {
                    return repositoryRoot;
                }

                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                while (directory != null)
                {
                    if (Directory.Exists(Path.Combine(directory.FullName, "Assets")) &&
                        Directory.Exists(Path.Combine(directory.FullName, "tests")))
                    {
                        repositoryRoot = directory.FullName;
                        return repositoryRoot;
                    }

                    directory = directory.Parent;
                }

                repositoryRoot = Directory.GetCurrentDirectory();
                return repositoryRoot;
            }
        }

        public static string ArtifactsDirectory
        {
            get
            {
                string path = Path.Combine(RepositoryRoot, "TestResults", "artifacts");
                Directory.CreateDirectory(path);
                return path;
            }
        }

        public static string PathFor(string name)
        {
            return Path.Combine(ArtifactsDirectory, Sanitize(name) + ".csv");
        }

        public static string Write(string name, IReadOnlyList<RunSample> samples)
        {
            string path = PathFor(name);
            var builder = new StringBuilder();
            builder.AppendLine("time_s,px,py,pz,vx,vy,vz,position_error_m,energy_drift_rel");
            foreach (RunSample sample in samples)
            {
                builder.Append(Format(sample.Time)).Append(',');
                builder.Append(Format(sample.State.Position.X)).Append(',');
                builder.Append(Format(sample.State.Position.Y)).Append(',');
                builder.Append(Format(sample.State.Position.Z)).Append(',');
                builder.Append(Format(sample.State.Velocity.X)).Append(',');
                builder.Append(Format(sample.State.Velocity.Y)).Append(',');
                builder.Append(Format(sample.State.Velocity.Z)).Append(',');
                builder.Append(Format(sample.PositionError)).Append(',');
                builder.AppendLine(Format(sample.EnergyDrift));
            }

            File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
            return path;
        }

        private static string Format(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return string.Empty;
            }

            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Sanitize(string name)
        {
            var builder = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                builder.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '_');
            }

            return builder.ToString();
        }
    }
}

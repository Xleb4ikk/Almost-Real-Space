// Contact zone (vertical slice).
// Height grid over the site: samples the ITerrainModel terrain function on a local
// square area ±HalfSize around the anchor. Normal and height come from the same
// function (ITerrainModel contract). Triangle winding gives upward normals.
using Galilego.Core;
using Galilego.Universe;
using UnityEngine;

namespace Galilego.Simulation.ContactZone
{
    [RequireComponent(typeof(MeshCollider))]
    public sealed class TerrainPatch : MonoBehaviour
    {
        [Tooltip("Половина стороны области, м.")]
        public float HalfSize = 150f;
        [Tooltip("Число вершин по стороне (шаг = 2·HalfSize / (N−1)).")]
        public int Resolution = 129;

        private MeshCollider col;
        private Mesh mesh;

        public void Build(OrbitingBody body, ITerrainModel terrain, SiteFrame frame, double t)
        {
            col = GetComponent<MeshCollider>();
            mesh = new Mesh();
            int n = Resolution;
            var verts = new Vector3[n * n];
            var idx = new int[(n - 1) * (n - 1) * 6];
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    double east = -HalfSize + (2d * HalfSize * i / (n - 1));
                    double north = -HalfSize + (2d * HalfSize * j / (n - 1));
                    double up = HeightAt(body, terrain, frame, east, north, t);
                    // Local (east, north, up) → Unity (north, up, east).
                    verts[(j * n) + i] = new Vector3((float)north, (float)up, (float)east);
                }
            }

            // Winding gives the normal UP (Unity up axis +Y): the reverse order would give
            // downward normals and the contact would push bodies into the terrain instead of supporting them.
            int k = 0;
            for (int j = 0; j < n - 1; j++)
            {
                for (int i = 0; i < n - 1; i++)
                {
                    int a = (j * n) + i;
                    int b = a + 1;
                    int c = a + n;
                    int d = c + 1;
                    idx[k++] = a; idx[k++] = b; idx[k++] = c;
                    idx[k++] = b; idx[k++] = d; idx[k++] = c;
                }
            }

            mesh.vertices = verts;
            mesh.triangles = idx;
            mesh.RecalculateNormals();
            col.sharedMesh = mesh;
        }

        public void Clear()
        {
            if (col != null) col.sharedMesh = null;
            if (mesh != null) Destroy(mesh);
            mesh = null;
        }

        // Surface height above the anchor tangent plane at point (east, north). The direction
        // is radial from the body center; the terrain height comes from ITerrainModel.
        private static double HeightAt(OrbitingBody body, ITerrainModel terrain, SiteFrame frame, double east, double north, double t)
        {
            Vector3d lateral = frame.Origin + (frame.East * east) + (frame.North * north);
            body.EvaluateWorldState(t, out Vector3d center, out _);
            Vector3d radial = (lateral - center).Normalized;
            Vector3d sample = center + (radial * body.Radius);
            body.SurfaceLatLonAt(sample, t, out double latDeg, out double lonDeg);
            double h = terrain.GetHeightMeters(body, KeplerMath.DegreesToRadians(latDeg), KeplerMath.DegreesToRadians(lonDeg));
            Vector3d surface = center + (radial * (body.Radius + h));
            return Vector3d.Dot(surface - frame.Origin, frame.Up);
        }
    }
}

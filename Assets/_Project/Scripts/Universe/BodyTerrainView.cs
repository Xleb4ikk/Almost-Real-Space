using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Рендер процедурного рельефа (визуальная сторона ITerrainModel).
    /// Сетка тайлов TileGrid×TileGrid вокруг подспутниковой точки корабля;
    /// тайл — quad в lat/lon с высотами из ТОЙ ЖЕ HeightfieldTerrain, что
    /// читает физика (паритет по построению — иначе корабль сядет «в воздухе»).
    ///
    /// Вершины строятся в тел-fixed кадре: BodyView каждый кадр ставит
    /// localRotation = ориентация тела (tilt+spin), поэтому локальные координаты
    /// тел-fixed точки НЕЗАВИСИМЫ от времени — меш перестраивается только при
    /// пересечении границы центрального тайла или смене доминантного тела,
    /// не каждый кадр. Море — вершины клампятся самой HeightfieldTerrain
    /// (плоская вода) + цвет воды в вершинной палитре.
    /// Вызов — LateUpdate после BodyView (Script Execution Order).
    /// </summary>
    public sealed class BodyTerrainView : MonoBehaviour
    {
        [Tooltip("SimulationRunner сцены.")]
        public SimulationRunner Runner;

        [Tooltip("Вершин на сторону тайла (33 ≈ 32 квада).")]
        public int TileResolution = 33;

        [Tooltip("Угловой размер тайла (градусы по дуге).")]
        public double TileSizeDegrees = 2d;

        [Tooltip("Сетка тайлов по стороне (нечётное; 5 = ±2 от центрального).")]
        public int TileGrid = 5;

        [Tooltip("Высота над рельефом, ниже которой рендерится сетка тайлов (м).")]
        public double MaxAltitudeMeters = 200000d;

        private OrbitingBody body;
        private HeightfieldTerrain terrain;
        private GameObject[,] tiles;
        private Mesh[,] meshes;
        private int centerLatIndex = int.MinValue;
        private int centerLonIndex = int.MinValue;
        private Material materialCache;

        private void Start()
        {
            if (Runner == null || Runner.SystemState == null)
            {
                enabled = false;
                return;
            }

            foreach (OrbitingBody candidate in Runner.SystemState.AllBodies)
            {
                if (candidate.Name == gameObject.name)
                {
                    body = candidate;
                    break;
                }
            }

            terrain = body?.Terrain as HeightfieldTerrain;
            if (terrain == null)
            {
                // Гладкая сфера или рельеф не включён — компонент не нужен.
                enabled = false;
                return;
            }

            if (Runner.SystemView == null)
            {
                Runner.SystemView = new BodyTransformRegistry();
            }

            CreateTileObjects();
        }

        private void LateUpdate()
        {
            if (body == null || Runner.Ship == null)
            {
                return;
            }

            // Тайлы нужны только рядом с поверхностью ЭТОГО тела.
            if (Runner.DominantBody != body)
            {
                HideAll();
                centerLatIndex = int.MinValue;
                return;
            }

            body.EvaluateWorldState(Runner.TimeSeconds, out Vector3d bodyPosition, out _);
            double altitude = (Runner.Ship.Position - bodyPosition).Magnitude - body.Radius;
            if (altitude > MaxAltitudeMeters)
            {
                HideAll();
                centerLatIndex = int.MinValue;
                return;
            }

            body.SurfaceLatLonAt(Runner.Ship.Position, Runner.TimeSeconds, out double shipLatDeg, out double shipLonDeg);
            double step = TileSizeDegrees;
            int latIndex = (int)System.Math.Floor(shipLatDeg / step);
            int lonIndex = (int)System.Math.Floor(shipLonDeg / step);

            if (latIndex == centerLatIndex && lonIndex == centerLonIndex)
            {
                return;
            }

            centerLatIndex = latIndex;
            centerLonIndex = lonIndex;
            RebuildTiles(latIndex, lonIndex);
        }

        private void CreateTileObjects()
        {
            int n = System.Math.Max(1, TileGrid);
            tiles = new GameObject[n, n];
            meshes = new Mesh[n, n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    GameObject tile = new GameObject("TerrainTile_" + i + "_" + j);
                    tile.transform.SetParent(transform, false);
                    MeshFilter filter = tile.AddComponent<MeshFilter>();
                    MeshRenderer renderer = tile.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = GetMaterial();
                    Mesh mesh = new Mesh();
                    mesh.MarkDynamic();
                    filter.sharedMesh = mesh;
                    tiles[i, j] = tile;
                    meshes[i, j] = mesh;
                    tile.SetActive(false);
                }
            }
        }

        private void RebuildTiles(int latIndex, int lonIndex)
        {
            // Ориентация на текущий момент: перевод мировых точек поверхности
            // (GetSurfaceState) в тел-fixed локальные координаты трансформа.
            // Поворот делается в Unity-кватернионах (AstroFrame-мост, T76):
            // local = q⁻¹ · world-дельта, шейп тела от времени не зависит.
            Runner.SystemState.GetVisualFrame(body, Runner.TimeSeconds, out Vector3d bodyPos, out _, out QuaternionD orientation);
            Quaternion inverse = Quaternion.Inverse(AstroFrame.ToSimulation(orientation));

            int half = TileGrid / 2;
            for (int i = 0; i < TileGrid; i++)
            {
                for (int j = 0; j < TileGrid; j++)
                {
                    double latCenter = ((latIndex + i - half) + 0.5d) * TileSizeDegrees;
                    double lonCenter = ((lonIndex + j - half) + 0.5d) * TileSizeDegrees;
                    FillTile(meshes[i, j], latCenter, lonCenter, inverse, bodyPos);
                    tiles[i, j].SetActive(true);
                }
            }
        }

        private void FillTile(Mesh mesh, double latCenterDeg, double lonCenterDeg, Quaternion inverse, Vector3d bodyPos)
        {
            int n = System.Math.Max(2, TileResolution);
            double step = TileSizeDegrees / (n - 1);
            var vertices = new Vector3[n * n];
            var colors = new Color[n * n];
            var triangles = new int[(n - 1) * (n - 1) * 6];

            double seaLevel = terrain.SeaLevelMeters;
            double amplitude = System.Math.Max(1d, terrain.AmplitudeMeters);

            for (int row = 0; row < n; row++)
            {
                double lat = ClampLatitude(latCenterDeg + ((row - ((n - 1) * 0.5d)) * step));
                for (int col = 0; col < n; col++)
                {
                    double lon = lonCenterDeg + ((col - ((n - 1) * 0.5d)) * step);
                    double latRad = lat * (System.Math.PI / 180d);
                    double lonRad = lon * (System.Math.PI / 180d);
                    double height = terrain.GetHeightMeters(body, latRad, lonRad);
                    body.GetSurfaceState(lat, lon, height, Runner.TimeSeconds, out Vector3d world, out _);
                    int index = (row * n) + col;
                    vertices[index] = inverse * AstroFrame.ToSimulation(world - bodyPos);
                    colors[index] = HeightColor(height, seaLevel, amplitude);
                }
            }

            int t = 0;
            for (int row = 0; row < n - 1; row++)
            {
                for (int col = 0; col < n - 1; col++)
                {
                    int a = (row * n) + col;
                    int b = a + 1;
                    int c = a + n;
                    int d = c + 1;
                    triangles[t++] = a;
                    triangles[t++] = c;
                    triangles[t++] = b;
                    triangles[t++] = b;
                    triangles[t++] = c;
                    triangles[t++] = d;
                }
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private static double ClampLatitude(double latDeg)
        {
            const double limit = 88d;
            if (latDeg > limit)
            {
                return limit;
            }

            if (latDeg < -limit)
            {
                return -limit;
            }

            return latDeg;
        }

        /// <summary>Палитра по высоте: море, низины, горы, снежники.</summary>
        private static Color HeightColor(double height, double seaLevel, double amplitude)
        {
            if (height <= seaLevel + (amplitude * 0.001d))
            {
                return new Color(0.15f, 0.35f, 0.65f);
            }

            double t = (height - seaLevel) / (2d * amplitude);
            if (t < 0.25d)
            {
                return Color.Lerp(new Color(0.85f, 0.8f, 0.55f), new Color(0.25f, 0.55f, 0.25f), (float)(t * 4d));
            }

            if (t < 0.7d)
            {
                return Color.Lerp(new Color(0.25f, 0.55f, 0.25f), new Color(0.45f, 0.4f, 0.35f), (float)((t - 0.25d) * (1d / 0.45d)));
            }

            return Color.Lerp(new Color(0.45f, 0.4f, 0.35f), new Color(0.95f, 0.95f, 0.97f), (float)((t - 0.7d) * (1d / 0.3d)));
        }

        private Material GetMaterial()
        {
            if (materialCache == null)
            {
                Shader shader = Shader.Find("Galilego/TerrainPatch");
                materialCache = new Material(shader);
            }

            return materialCache;
        }

        private void HideAll()
        {
            if (tiles == null)
            {
                return;
            }

            for (int i = 0; i < TileGrid; i++)
            {
                for (int j = 0; j < TileGrid; j++)
                {
                    if (tiles[i, j].activeSelf)
                    {
                        tiles[i, j].SetActive(false);
                    }
                }
            }
        }
    }
}

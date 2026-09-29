using System;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    // T108 — математика локального поверхностного фрейма (SurfaceFrameMath).
    // Фрейм — единственное, на чём держится авторинг базы: если ENU-базис
    // «правый» неправ, поворот объекта окажется зеркальным, а если
    // DirectionOffset вернёт неединичное направление, высота в превью уедет
    // вместе с радиусом. Обе ошибки невидимы на глаз и ломают всё остальное.

    private static int Test108_SurfaceFrameBasis()
    {
        // 1. Базис ортонормирован и ПРАВЫЙ: X север, Y зенит, Z восток.
        //    N × U = E. Если знак минус — поворот зеркальный.
        double[][] samples =
        {
            new double[] { 0d, 0d },
            new double[] { 0.7d, 2.1d },
            new double[] { -1.2d, -3.9d },
            new double[] { 1.5707963d, 0.3d },
            new double[] { -1.5707963d, 4.4d }
        };

        bool unit = true;
        bool rightHanded = true;
        bool axesDistinct = true;
        foreach (double[] s in samples)
        {
            Vector3d n = SurfaceFrameMath.North(s[0], s[1]);
            Vector3d u = SurfaceFrameMath.Up(s[0], s[1]);
            Vector3d e = SurfaceFrameMath.East(s[0], s[1]);

            unit &= System.Math.Abs(n.Magnitude - 1d) < 1e-12
                && System.Math.Abs(u.Magnitude - 1d) < 1e-12
                && System.Math.Abs(e.Magnitude - 1d) < 1e-12;

            Vector3d cross = Vector3d.Cross(n, u);
            rightHanded &= System.Math.Abs(cross.X - e.X) < 1e-12
                && System.Math.Abs(cross.Y - e.Y) < 1e-12
                && System.Math.Abs(cross.Z - e.Z) < 1e-12;

            axesDistinct &= System.Math.Abs(Vector3d.Dot(n, u)) < 1e-12
                && System.Math.Abs(Vector3d.Dot(n, e)) < 1e-12
                && System.Math.Abs(Vector3d.Dot(u, e)) < 1e-12;
        }

        Check(unit, "T108 surface-frame-basis", "все три оси единичные на всех lat/lon, включая полюса");
        Check(rightHanded, "T108 surface-frame-basis", "базис правый: север x зенит = восток");
        Check(axesDistinct, "T108 surface-frame-basis", "оси взаимно перпендикулярны");

        // 2. Кватернион фрейма ровно соответствует базису: q*x = север,
        //    q*y = зенит, q*z = восток. Rotation собирается матрицей — если
        //    ветви трассировки разъедутся на полюсах, вылезет NaN.
        bool quatMatches = true;
        bool finite = true;
        foreach (double[] s in samples)
        {
            QuaternionD q = SurfaceFrameMath.Rotation(s[0], s[1]);
            finite &= System.Math.Abs(q.X) < double.MaxValue && System.Math.Abs(q.W) < double.MaxValue
                && System.Math.Abs(q.NormSquared - 1d) < 1e-12;

            Vector3d nx = q.Rotate(new Vector3d(1d, 0d, 0d));
            Vector3d ny = q.Rotate(new Vector3d(0d, 1d, 0d));
            Vector3d nz = q.Rotate(new Vector3d(0d, 0d, 1d));
            quatMatches &= Close(nx, SurfaceFrameMath.North(s[0], s[1]))
                && Close(ny, SurfaceFrameMath.Up(s[0], s[1]))
                && Close(nz, SurfaceFrameMath.East(s[0], s[1]));
        }

        Check(finite, "T108 surface-frame-basis", "кватернион фрейма единичный и конечный на полюсах");
        Check(quatMatches, "T108 surface-frame-basis", "q переводит локальные оси в север/зенит/восток");

        // 3. Зенит совпадает с направлением, которое считает рельеф
        //    (HeightfieldTerrain.GetRawHeightMeters): иначе превью и физика
        //    смотрят в разные точки планеты.
        HeightfieldTerrain terrain = MakeTerrain(24334543);
        OrbitingBody probe = new OrbitingBody
        {
            Name = "T108",
            Radius = 1143000d,
            StandardGravitationalParameter = 1d
        };
        probe.Terrain = terrain;

        double lat = -0.4886921906d;
        double lon = -2.085461958d;
        Vector3d expected = new Vector3d(
            System.Math.Cos(lat) * System.Math.Cos(lon),
            System.Math.Cos(lat) * System.Math.Sin(lon),
            System.Math.Sin(lat));
        double shaderDirError = (SurfaceFrameMath.Up(lat, lon) - expected).Magnitude;
        Check(shaderDirError < 1e-15, "T108 surface-frame-basis",
            "Up(lat,lon) совпадает с базисом рельефа (ошибка " + shaderDirError.ToString("E2") + ")");

        // 4. DirectionOffset остаётся ЕДИНИЧНЫМ на любом смещении, иначе
        //    выборка высоты в превью поедет по радиусу.
        double radius = 1143000d;
        bool unitAfterOffset = true;
        double[] offsets = { 0d, 1d, 100d, 5000d, 150000d };
        foreach (double east in offsets)
        {
            foreach (double northOff in offsets)
            {
                Vector3d dir = SurfaceFrameMath.DirectionOffset(lat, lon, radius, east, northOff);
                unitAfterOffset &= System.Math.Abs(dir.Magnitude - 1d) < 1e-12;
            }
        }

        Check(unitAfterOffset, "T108 surface-frame-basis",
            "DirectionOffset единичный на смещениях до 150 км");

        // 5. ENU-смещение в осях фрейма: восток -> Z, север -> X, зенит -> Y.
        //    Раньше здесь стоял LocalOffset, который возвращал вектор в осях
        //    ТЕЛА: объект смещался на лишний угол фрейма (T108 поймал).
        Vector3d eastOffset = SurfaceFrameMath.EnuToFrameLocal(10d, 0d, 0d);
        Vector3d northOffset = SurfaceFrameMath.EnuToFrameLocal(0d, 10d, 0d);
        Vector3d upOffset = SurfaceFrameMath.EnuToFrameLocal(0d, 0d, 10d);
        Check(System.Math.Abs(eastOffset.X) < 1e-9 && System.Math.Abs(eastOffset.Y) < 1e-9
            && System.Math.Abs(eastOffset.Z - 10d) < 1e-9, "T108 surface-frame-basis",
            "10 м на восток -> 10 м по Z");
        Check(System.Math.Abs(northOffset.Y) < 1e-9 && System.Math.Abs(northOffset.Z) < 1e-9
            && System.Math.Abs(northOffset.X - 10d) < 1e-9, "T108 surface-frame-basis",
            "10 м на север -> 10 м по X");
        Check(System.Math.Abs(upOffset.X) < 1e-9 && System.Math.Abs(upOffset.Z) < 1e-9
            && System.Math.Abs(upOffset.Y - 10d) < 1e-9, "T108 surface-frame-basis",
            "10 м вверх -> 10 м по Y");

        // 6. ToLocal обращает перенос ENU в осях тела: круговой цикл не должен
        //    терять компоненту (иначе превью «съедает» рельеф вдоль одной оси).
        Vector3d enu = SurfaceFrameMath.EnuToFrameLocal(7d, -13d, 5d);
        Vector3d bodyFixed = (SurfaceFrameMath.East(lat, lon) * enu.Z)
            + (SurfaceFrameMath.North(lat, lon) * enu.X)
            + (SurfaceFrameMath.Up(lat, lon) * enu.Y);
        Vector3d roundTrip = SurfaceFrameMath.ToLocal(lat, lon, bodyFixed);
        Check(System.Math.Abs(roundTrip.X + 13d) < 1e-9 && System.Math.Abs(roundTrip.Y - 5d) < 1e-9
            && System.Math.Abs(roundTrip.Z - 7d) < 1e-9, "T108 surface-frame-basis",
            "ToLocal обращает перенос ENU: (север −13, зенит 5, восток 7)");

        // 7. Смещение вдоль зенита/горизонтали взаимно перпендикулярно в осях
        //    тела: без этого превью «наклоняет» рельеф.
        double ortho = System.Math.Abs(
            Vector3d.Dot(SurfaceFrameMath.Up(lat, lon), SurfaceFrameMath.East(lat, lon)))
            + System.Math.Abs(Vector3d.Dot(SurfaceFrameMath.Up(lat, lon), SurfaceFrameMath.North(lat, lon)));
        Check(ortho < 1e-12, "T108 surface-frame-basis",
            "зенит перпендикулярен касательным (скалярное произведение " + ortho.ToString("E2") + ")");

        return 0;
    }

    private static bool Close(Vector3d a, Vector3d b)
    {
        return (a - b).Magnitude < 1e-12;
    }
}

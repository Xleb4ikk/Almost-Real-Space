using System;
using System.Collections.Generic;
using Galilego.Core;

namespace Galilego.Spacecraft.Solver
{
    /// <summary>
    /// Твёрдое тело детали в решателе фазы 2. Масса и главные моменты (в осях тела),
    /// состояние: центр масс в кадре решателя, скорость, ориентация тела (тело → кадр),
    /// угловая скорость в осях тела. Внешние сила и момент задаются перед шагом и
    /// постоянны на шаге. Контакты с землёй — фаза 3, здесь их нет.
    /// </summary>
    public sealed class RigidBody
    {
        public readonly double Mass;
        public readonly Vector3d PrincipalInertia;

        public Vector3d Position;
        public Vector3d Velocity;
        public QuaternionD Orientation;
        public Vector3d AngularVelocityBody;

        public Vector3d ForceWorld;
        public Vector3d TorqueBody;
        /// <summary>Точки контакта с поверхностью в осях тела, от центра масс (м). Пусто — тело без контакта.</summary>
        public Vector3d[] ContactPointsBody = new Vector3d[0];

        public RigidBody(double mass, Vector3d principalInertia, Vector3d position, QuaternionD orientation)
        {
            if (!(mass > 0d))
            {
                throw new ArgumentOutOfRangeException(nameof(mass), "Масса тела обязана быть положительной.");
            }

            if (!(principalInertia.X > 0d && principalInertia.Y > 0d && principalInertia.Z > 0d))
            {
                throw new ArgumentOutOfRangeException(nameof(principalInertia), "Главные моменты обязаны быть положительными.");
            }

            Mass = mass;
            PrincipalInertia = principalInertia;
            Position = position;
            Velocity = Vector3d.Zero;
            Orientation = orientation.Normalized;
            AngularVelocityBody = Vector3d.Zero;
            ForceWorld = Vector3d.Zero;
            TorqueBody = Vector3d.Zero;
        }

        /// <summary>Обратный тензор инерции, применённый к мировому вектору: I⁻¹·v (мир).</summary>
        public Vector3d ApplyInverseInertiaWorld(QuaternionD orientation, Vector3d worldVector)
        {
            Vector3d body = orientation.Conjugated.Rotate(worldVector);
            Vector3d scaled = new Vector3d(
                body.X / PrincipalInertia.X,
                body.Y / PrincipalInertia.Y,
                body.Z / PrincipalInertia.Z);
            return orientation.Rotate(scaled);
        }
    }

    /// <summary>
    /// Жёсткий стык: три неколлинеарные точки совпадают на двух телах. Девять
    /// скалярных ограничений ранга 6 фиксируют относительное положение и ориентацию.
    /// Точки задаются в осях тел относительно их центров масс.
    /// </summary>
    public sealed class RigidJoint
    {
        public readonly int BodyA;
        public readonly int BodyB;
        public readonly Vector3d[] AnchorA;
        public readonly Vector3d[] AnchorB;

        public RigidJoint(int bodyA, int bodyB, Vector3d[] anchorA, Vector3d[] anchorB)
        {
            if (anchorA == null || anchorB == null || anchorA.Length != anchorB.Length || anchorA.Length < 1)
            {
                throw new ArgumentException("Стык требует одинакового числа якорных точек на обоих телах.");
            }

            BodyA = bodyA;
            BodyB = bodyB;
            AnchorA = anchorA;
            AnchorB = anchorB;
        }
    }

    /// <summary>
    /// Решатель твёрдых тел фазы 2: фиксированный шаг dt, явное интегрирование
    /// скоростей, RK4 для вращения тела (уравнения Эйлера в осях тела), XPBD-проекция
    /// жёстких стыков (compliance α = 0) по скалярным ограничениям вдоль мировых осей.
    /// Внутренние силы стыков сохраняют линейный импульс системы точно.
    /// </summary>
    public sealed class RigidAssemblySolver
    {
        private static readonly Vector3d[] WorldAxes =
        {
            new Vector3d(1d, 0d, 0d),
            new Vector3d(0d, 1d, 0d),
            new Vector3d(0d, 0d, 1d),
        };

        private readonly List<RigidBody> bodies = new List<RigidBody>();
        private readonly List<RigidJoint> joints = new List<RigidJoint>();
        private Vector3d[] contactDv = new Vector3d[0];
        private Vector3d[] contactDw = new Vector3d[0];
        private readonly double timeStep;
        private readonly int iterations;

        public RigidAssemblySolver(double timeStep, int iterations)
        {
            if (!(timeStep > 0d))
            {
                throw new ArgumentOutOfRangeException(nameof(timeStep), "Шаг решателя обязан быть положительным.");
            }

            if (iterations < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(iterations), "Нужна хотя бы одна итерация.");
            }

            this.timeStep = timeStep;
            this.iterations = iterations;
        }

        public double TimeStep => timeStep;
        public double Time { get; private set; }
        public IReadOnlyList<RigidBody> Bodies => bodies;
        public IReadOnlyList<RigidJoint> Joints => joints;

        public int AddBody(RigidBody body)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            bodies.Add(body);
            return bodies.Count - 1;
        }

        /// <summary>
        /// Жёсткий стык в мировой точке pivot: три точки pivot, pivot + ex·L, pivot + ey·L
        /// переводятся в оси обоих тел при текущем состоянии.
        /// </summary>
        public int AddRigidJoint(int bodyA, int bodyB, Vector3d pivotWorld, double spacingMeters = 1d)
        {
            if (bodyA == bodyB)
            {
                throw new ArgumentException("Стык должен соединять два разных тела.");
            }

            RigidBody a = bodies[bodyA];
            RigidBody b = bodies[bodyB];
            var points = new Vector3d[]
            {
                pivotWorld,
                pivotWorld + (WorldAxes[0] * spacingMeters),
                pivotWorld + (WorldAxes[1] * spacingMeters),
            };

            var anchorA = new Vector3d[points.Length];
            var anchorB = new Vector3d[points.Length];
            for (int k = 0; k < points.Length; k++)
            {
                anchorA[k] = a.Orientation.Conjugated.Rotate(points[k] - a.Position);
                anchorB[k] = b.Orientation.Conjugated.Rotate(points[k] - b.Position);
            }

            joints.Add(new RigidJoint(bodyA, bodyB, anchorA, anchorB));
            return joints.Count - 1;
        }

        /// <summary>Наибольшее расхождение якорных точек стыка, м (мировой кадр, текущее состояние).</summary>
        public double JointSeparation(int jointIndex)
        {
            RigidJoint j = joints[jointIndex];
            RigidBody a = bodies[j.BodyA];
            RigidBody b = bodies[j.BodyB];
            double worst = 0d;
            for (int k = 0; k < j.AnchorA.Length; k++)
            {
                Vector3d pa = a.Position + a.Orientation.Rotate(j.AnchorA[k]);
                Vector3d pb = b.Position + b.Orientation.Rotate(j.AnchorB[k]);
                worst = Math.Max(worst, (pa - pb).Magnitude);
            }

            return worst;
        }

        public Vector3d LinearMomentum()
        {
            Vector3d p = Vector3d.Zero;
            for (int i = 0; i < bodies.Count; i++)
            {
                p += bodies[i].Velocity * bodies[i].Mass;
            }

            return p;
        }

        public double KineticEnergy()
        {
            double e = 0d;
            for (int i = 0; i < bodies.Count; i++)
            {
                RigidBody b = bodies[i];
                double translational = 0.5d * b.Mass * b.Velocity.SqrMagnitude;
                Vector3d w = b.AngularVelocityBody;
                double rotational = 0.5d * ((b.PrincipalInertia.X * w.X * w.X)
                    + (b.PrincipalInertia.Y * w.Y * w.Y)
                    + (b.PrincipalInertia.Z * w.Z * w.Z));
                e += translational + rotational;
            }

            return e;
        }

        /// <summary>Один шаг фиксированной длительности dt (порядок sequential impulses).</summary>
        public void Step()
        {
            int n = bodies.Count;

            for (int i = 0; i < n; i++)
            {
                RigidBody b = bodies[i];
                b.Velocity = b.Velocity + ((b.ForceWorld / b.Mass) * timeStep);
                b.AngularVelocityBody = IntegrateEulerRk4(
                    b.AngularVelocityBody, b.TorqueBody, b.PrincipalInertia, timeStep);
            }

            // Стыки: импульсы на скорости, Baumgarte-смещение по ошибке якорей на начале шага.
            // Внутренние импульсы парные в точках якорей, поэтому P и L сохраняются.
            for (int it = 0; it < iterations; it++)
            {
                for (int j = 0; j < joints.Count; j++)
                {
                    RigidJoint joint = joints[j];
                    for (int k = 0; k < joint.AnchorA.Length; k++)
                    {
                        for (int axis = 0; axis < 3; axis++)
                        {
                            ApplyJointAxisImpulse(joint, k, WorldAxes[axis]);
                        }
                    }
                }
            }

            var predictedPosition = new Vector3d[n];
            var predictedOrientation = new QuaternionD[n];
            for (int i = 0; i < n; i++)
            {
                RigidBody b = bodies[i];
                predictedPosition[i] = b.Position + (b.Velocity * timeStep);
                predictedOrientation[i] = (b.Orientation
                    * RotationExponential(b.AngularVelocityBody * timeStep)).Normalized;
            }

            if (Ground != null)
            {
                DetectAndSolveContacts(predictedPosition, predictedOrientation);
            }

            for (int i = 0; i < n; i++)
            {
                RigidBody b = bodies[i];
                b.Position = predictedPosition[i];
                b.Orientation = predictedOrientation[i];
            }

            Time += timeStep;
        }

        /// <summary>
        /// Импульс вдоль мировой оси e в точке якоря k стыка: обнуляет относительную скорость
        /// точек вдоль e с Baumgarte-смещением −(β/dt)·C, C = (pA − pB)·e на начале шага.
        /// </summary>
        private void ApplyJointAxisImpulse(RigidJoint joint, int anchorIndex, Vector3d axis)
        {
            int ia = joint.BodyA;
            int ib = joint.BodyB;
            RigidBody a = bodies[ia];
            RigidBody b = bodies[ib];

            Vector3d ra = a.Orientation.Rotate(joint.AnchorA[anchorIndex]);
            Vector3d rb = b.Orientation.Rotate(joint.AnchorB[anchorIndex]);
            Vector3d pa = a.Position + ra;
            Vector3d pb = b.Position + rb;

            double c = Vector3d.Dot(pa - pb, axis);
            double vn = Vector3d.Dot(PointVelocity(ia, a.Orientation, ra) - PointVelocity(ib, b.Orientation, rb), axis);

            Vector3d rxa = Vector3d.Cross(ra, axis);
            Vector3d rxb = Vector3d.Cross(rb, axis);
            double w = (1d / a.Mass) + (1d / b.Mass)
                + Vector3d.Dot(rxa, a.ApplyInverseInertiaWorld(a.Orientation, rxa))
                + Vector3d.Dot(rxb, b.ApplyInverseInertiaWorld(b.Orientation, rxb));
            if (!(w > 0d))
            {
                return;
            }

            double lambda = -(vn + (JointBaumgarte / timeStep) * c) / w;
            Vector3d impulse = axis * lambda;
            ApplyVelocityImpulse(ia, a.Orientation, ra, impulse);
            ApplyVelocityImpulse(ib, b.Orientation, rb, -impulse);
        }

        /// <summary>Коэффициент Baumgarte для дрейфа якорей стыка (безразмерный, 0..1).</summary>
        public double JointBaumgarte { get; set; } = 0.2d;

        /// <summary>Геометрия контакта; null — контактов нет (фаза 2 без изменений).</summary>
        public IContactGround Ground { get; set; }
        public double StaticFriction { get; set; } = 0.7d;
        public double KineticFriction { get; set; } = 0.5d;
        public double Restitution { get; set; } = 0d;
        public double ImpactThreshold { get; set; } = 0.01d;

        private sealed class ContactRecord
        {
            public int Body;
            public int Point;
            public Vector3d Normal;
            public Vector3d R;
            public double VnPre;
            public double LambdaN;
            public Vector3d FrictionAcc;
        }

        /// <summary>
        /// Сцепление без состояния: тангенциальный импульс, гасящий скольжение, допустим, пока
        /// его величина не превышает μs·λn; иначе — кинетическое трение μk·λn против скольжения.
        /// </summary>
        private void DetectAndSolveContacts(Vector3d[] position, QuaternionD[] orientation)
        {
            var contacts = new List<ContactRecord>();
            contactDv = new Vector3d[bodies.Count];
            contactDw = new Vector3d[bodies.Count];
            for (int i = 0; i < bodies.Count; i++)
            {
                RigidBody b = bodies[i];
                for (int k = 0; k < b.ContactPointsBody.Length; k++)
                {
                    Vector3d r = orientation[i].Rotate(b.ContactPointsBody[k]);
                    Ground.Query(position[i] + r, out double depth, out Vector3d n);
                    if (depth <= 0d)
                    {
                        continue;
                    }

                    contacts.Add(new ContactRecord
                    {
                        Body = i,
                        Point = k,
                        Normal = n,
                        R = r,
                        VnPre = Vector3d.Dot(PointVelocity(i, orientation[i], r), n),
                        LambdaN = 0d,
                    });
                }
            }

            if (contacts.Count == 0)
            {
                return;
            }

            for (int it = 0; it < iterations; it++)
            {
                for (int c = 0; c < contacts.Count; c++)
                {
                    SolveNormalAndFriction(contacts[c], orientation);
                }
            }

            // Положения — из скоростей, уже очищенных от проникновения (Box2D-порядок):
            // без этого предсказанное на шаге проникновение от гравитации уводило бы тело
            // по нормали, а трение (покой) это не компенсирует.
            for (int i = 0; i < bodies.Count; i++)
            {
                position[i] = position[i] + (contactDv[i] * timeStep);
                orientation[i] = (RotationExponential(contactDw[i] * timeStep) * orientation[i]).Normalized;
            }

            for (int it = 0; it < iterations; it++)
            {
                for (int c = 0; c < contacts.Count; c++)
                {
                    ProjectPenetration(contacts[c], position, orientation);
                }
            }
        }

        private void SolveNormalAndFriction(ContactRecord c, QuaternionD[] orientation)
        {
            RigidBody b = bodies[c.Body];
            QuaternionD q = orientation[c.Body];

            Vector3d vp = PointVelocity(c.Body, q, c.R);
            double vn = Vector3d.Dot(vp, c.Normal);
            Vector3d rxn = Vector3d.Cross(c.R, c.Normal);
            double wn = (1d / b.Mass) + Vector3d.Dot(rxn, b.ApplyInverseInertiaWorld(q, rxn));
            if (!(wn > 0d))
            {
                return;
            }

            double target = c.VnPre < -ImpactThreshold ? -Restitution * c.VnPre : 0d;
            double dl = (target - vn) / wn;
            double newLambda = Math.Max(0d, c.LambdaN + dl);
            dl = newLambda - c.LambdaN;
            c.LambdaN = newLambda;
            ApplyImpulse(c.Body, q, c.R, c.Normal * dl);

            vp = PointVelocity(c.Body, q, c.R);
            Vector3d vt = vp - (c.Normal * Vector3d.Dot(vp, c.Normal));
            double vtMag = vt.Magnitude;
            if (vtMag < 1e-12d)
            {
                return;
            }

            Vector3d t = vt / vtMag;
            Vector3d rxt = Vector3d.Cross(c.R, t);
            double wt = (1d / b.Mass) + Vector3d.Dot(rxt, b.ApplyInverseInertiaWorld(q, rxt));

            // Накопленный за подшаг тангенциальный импульс конуса Кулона: сумма по итерациям,
            // а не добавка каждую итерацию. Залипание — пока суммарный импульс внутри μs·λn;
            // иначе скольжение — импульс на границе конуса μk·λn против движения.
            Vector3d need = t * (-vtMag / wt);
            Vector3d total = c.FrictionAcc + need;
            double magnitude = total.Magnitude;
            if (magnitude > StaticFriction * c.LambdaN)
            {
                total = magnitude > 0d ? total * (KineticFriction * c.LambdaN / magnitude) : Vector3d.Zero;
            }

            Vector3d delta = total - c.FrictionAcc;
            c.FrictionAcc = total;
            ApplyImpulse(c.Body, q, c.R, delta);
        }

        private void ProjectPenetration(ContactRecord c, Vector3d[] position, QuaternionD[] orientation)
        {
            int i = c.Body;
            RigidBody b = bodies[i];
            QuaternionD q = orientation[i];
            Vector3d r = q.Rotate(b.ContactPointsBody[c.Point]);
            Ground.Query(position[i] + r, out double depth, out Vector3d n);
            if (depth <= 0d)
            {
                return;
            }

            Vector3d rxn = Vector3d.Cross(r, n);
            double w = (1d / b.Mass) + Vector3d.Dot(rxn, b.ApplyInverseInertiaWorld(q, rxn));
            if (!(w > 0d))
            {
                return;
            }

            double lambda = depth / w;
            position[i] = position[i] + (n * (lambda / b.Mass));
            Vector3d dTheta = b.ApplyInverseInertiaWorld(q, rxn) * lambda;
            orientation[i] = (RotationExponential(dTheta) * q).Normalized;
        }

        private Vector3d PointVelocity(int bodyIndex, QuaternionD q, Vector3d r)
        {
            RigidBody b = bodies[bodyIndex];
            return b.Velocity + Vector3d.Cross(q.Rotate(b.AngularVelocityBody), r);
        }

        private void ApplyVelocityImpulse(int bodyIndex, QuaternionD q, Vector3d r, Vector3d impulse)
        {
            RigidBody b = bodies[bodyIndex];
            Vector3d dOmegaWorld = b.ApplyInverseInertiaWorld(q, Vector3d.Cross(r, impulse));
            b.Velocity = b.Velocity + (impulse / b.Mass);
            b.AngularVelocityBody = b.AngularVelocityBody + q.Conjugated.Rotate(dOmegaWorld);
        }

        /// <summary>Контактный импульс: скорость сразу и накопление для сдвига положения на шаге.</summary>
        private void ApplyImpulse(int bodyIndex, QuaternionD q, Vector3d r, Vector3d impulse)
        {
            ApplyVelocityImpulse(bodyIndex, q, r, impulse);
            RigidBody b = bodies[bodyIndex];
            contactDv[bodyIndex] = contactDv[bodyIndex] + (impulse / b.Mass);
            contactDw[bodyIndex] = contactDw[bodyIndex] + b.ApplyInverseInertiaWorld(q, Vector3d.Cross(r, impulse));
        }

        /// <summary>Суммарный момент импульса относительно начала мира: Σ(x×mv + R·I·ω_тела).</summary>
        public Vector3d AngularMomentum()
        {
            Vector3d l = Vector3d.Zero;
            for (int i = 0; i < bodies.Count; i++)
            {
                RigidBody b = bodies[i];
                Vector3d spin = new Vector3d(
                    b.PrincipalInertia.X * b.AngularVelocityBody.X,
                    b.PrincipalInertia.Y * b.AngularVelocityBody.Y,
                    b.PrincipalInertia.Z * b.AngularVelocityBody.Z);
                l = l + Vector3d.Cross(b.Position, b.Velocity * b.Mass) + b.Orientation.Rotate(spin);
            }

            return l;
        }

        /// <summary>
        /// Поворот на вектор rotationVector (рад, мировой или телесный — безразлично для малых
        /// приращений здесь: вызывающий использует ту же ось): exp(θ/2) как кватернион.
        /// </summary>
        public static QuaternionD RotationExponential(Vector3d rotationVector)
        {
            double angle = rotationVector.Magnitude;
            if (angle < 1e-12)
            {
                return new QuaternionD(0.5d * rotationVector.X, 0.5d * rotationVector.Y,
                    0.5d * rotationVector.Z, 1d).Normalized;
            }

            double half = 0.5d * angle;
            double s = Math.Sin(half) / angle;
            return new QuaternionD(rotationVector.X * s, rotationVector.Y * s, rotationVector.Z * s, Math.Cos(half));
        }

        /// <summary>Один шаг RK4 для уравнений Эйлера (τ постоянен на шаге): ω̇ = (τ − ω×Iω)/I.</summary>
        public static Vector3d IntegrateEulerRk4(Vector3d omega, Vector3d torqueBody, Vector3d inertia, double dt)
        {
            Vector3d k1 = EulerRate(omega, torqueBody, inertia);
            Vector3d k2 = EulerRate(omega + (k1 * (0.5d * dt)), torqueBody, inertia);
            Vector3d k3 = EulerRate(omega + (k2 * (0.5d * dt)), torqueBody, inertia);
            Vector3d k4 = EulerRate(omega + (k3 * dt), torqueBody, inertia);
            return omega + ((k1 + (k2 * 2d) + (k3 * 2d) + k4) * (dt / 6d));
        }

        private static Vector3d EulerRate(Vector3d w, Vector3d torque, Vector3d inertia)
        {
            Vector3d iw = new Vector3d(inertia.X * w.X, inertia.Y * w.Y, inertia.Z * w.Z);
            Vector3d wxiw = Vector3d.Cross(w, iw);
            return new Vector3d(
                (torque.X - wxiw.X) / inertia.X,
                (torque.Y - wxiw.Y) / inertia.Y,
                (torque.Z - wxiw.Z) / inertia.Z);
        }
    }
}

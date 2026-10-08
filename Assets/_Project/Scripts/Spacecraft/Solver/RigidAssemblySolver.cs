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

        /// <summary>Один шаг фиксированной длительности dt.</summary>
        public void Step()
        {
            int n = bodies.Count;
            var predictedPosition = new Vector3d[n];
            var predictedOrientation = new QuaternionD[n];

            for (int i = 0; i < n; i++)
            {
                RigidBody b = bodies[i];

                b.Velocity += (b.ForceWorld / b.Mass) * timeStep;
                b.AngularVelocityBody = IntegrateEulerRk4(
                    b.AngularVelocityBody, b.TorqueBody, b.PrincipalInertia, timeStep);

                predictedPosition[i] = b.Position + (b.Velocity * timeStep);
                predictedOrientation[i] = (b.Orientation
                    * RotationExponential(b.AngularVelocityBody * timeStep)).Normalized;
            }

            for (int it = 0; it < iterations; it++)
            {
                for (int j = 0; j < joints.Count; j++)
                {
                    RigidJoint joint = joints[j];
                    for (int k = 0; k < joint.AnchorA.Length; k++)
                    {
                        for (int axis = 0; axis < 3; axis++)
                        {
                            ApplyPointAxisConstraint(joint, k, WorldAxes[axis],
                                predictedPosition, predictedOrientation);
                        }
                    }
                }
            }

            for (int i = 0; i < n; i++)
            {
                RigidBody b = bodies[i];
                b.Position = predictedPosition[i];
                b.Orientation = predictedOrientation[i];
            }

            Time += timeStep;
        }

        private void ApplyPointAxisConstraint(
            RigidJoint joint,
            int anchorIndex,
            Vector3d axis,
            Vector3d[] position,
            QuaternionD[] orientation)
        {
            int ia = joint.BodyA;
            int ib = joint.BodyB;
            RigidBody a = bodies[ia];
            RigidBody b = bodies[ib];

            Vector3d ra = orientation[ia].Rotate(joint.AnchorA[anchorIndex]);
            Vector3d rb = orientation[ib].Rotate(joint.AnchorB[anchorIndex]);
            Vector3d pa = position[ia] + ra;
            Vector3d pb = position[ib] + rb;

            double c = Vector3d.Dot(pa - pb, axis);

            Vector3d raCross = Vector3d.Cross(ra, axis);
            Vector3d rbCross = Vector3d.Cross(rb, axis);
            Vector3d ia_w = a.ApplyInverseInertiaWorld(orientation[ia], raCross);
            Vector3d ib_w = b.ApplyInverseInertiaWorld(orientation[ib], rbCross);

            double w = (1d / a.Mass) + (1d / b.Mass)
                + Vector3d.Dot(raCross, ia_w) + Vector3d.Dot(rbCross, ib_w);
            if (!(w > 0d))
            {
                return;
            }

            double lambda = -c / w;

            Vector3d dxA = axis * (lambda / a.Mass);
            Vector3d dThetaA = ia_w * lambda;
            Vector3d dxB = axis * (-lambda / b.Mass);
            Vector3d dThetaB = ib_w * (-lambda);

            position[ia] = position[ia] + dxA;
            position[ib] = position[ib] + dxB;

            orientation[ia] = (RotationExponential(dThetaA) * orientation[ia]).Normalized;
            orientation[ib] = (RotationExponential(dThetaB) * orientation[ib]).Normalized;

            a.Velocity += dxA / timeStep;
            b.Velocity += dxB / timeStep;
            a.AngularVelocityBody += orientation[ia].Conjugated.Rotate(dThetaA) / timeStep;
            b.AngularVelocityBody += orientation[ib].Conjugated.Rotate(dThetaB) / timeStep;
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

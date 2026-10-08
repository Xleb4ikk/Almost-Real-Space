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
    /// Пружинный ползун (нога): тело B скользит вдоль оси, закреплённой на теле A.
    /// Связи: две поперечные (точка B на оси A), три на поворот (относительная ориентация
    /// фиксирована), упругость и демпфер вдоль оси (явно, по конфигурации начала шага),
    /// односторонние ограничения хода [min, max] относительно длины покоя.
    /// s — координата вдоль оси: (pB − pA)·e; сжатие — s уменьшается.
    /// </summary>
    public sealed class SliderJoint
    {
        public readonly int BodyA;
        public readonly int BodyB;
        public readonly Vector3d AnchorA;
        public readonly Vector3d AnchorB;
        public readonly Vector3d AxisA;
        public readonly QuaternionD RelativeRest;
        public readonly double RestCoordinate;
        public readonly double Stiffness;
        public readonly double Damping;
        public readonly double MinTravel;
        public readonly double MaxTravel;
        internal double LambdaLow;
        internal double LambdaHigh;

        public SliderJoint(int bodyA, int bodyB, Vector3d anchorA, Vector3d anchorB, Vector3d axisA,
            QuaternionD relativeRest, double restCoordinate, double stiffness, double damping,
            double minTravel, double maxTravel)
        {
            BodyA = bodyA;
            BodyB = bodyB;
            AnchorA = anchorA;
            AnchorB = anchorB;
            AxisA = axisA;
            RelativeRest = relativeRest;
            RestCoordinate = restCoordinate;
            Stiffness = stiffness;
            Damping = damping;
            MinTravel = minTravel;
            MaxTravel = maxTravel;
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
        private readonly List<SliderJoint> sliders = new List<SliderJoint>();

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

        /// <summary>
        /// Ползун: ось axisWorld закреплена на теле A через точку anchorWorld; тело B
        /// скользит вдоль неё. Длина покоя — текущая координата s. Ход — относительно неё.
        /// dampingRatio — ζ, c = 2ζ·√(k·m_эфф).
        /// </summary>
        public int AddSliderJoint(int bodyA, int bodyB, Vector3d anchorWorld, Vector3d axisWorld,
            double minTravel, double maxTravel, double stiffness, double dampingRatio,
            double damperReferenceMass = 0d)
        {
            if (bodyA == bodyB)
            {
                throw new ArgumentException("Ползун должен соединять два разных тела.");
            }

            if (!(stiffness > 0d) || !(dampingRatio >= 0d) || !(minTravel <= 0d) || !(maxTravel >= 0d))
            {
                throw new ArgumentOutOfRangeException(nameof(stiffness), "k > 0, ζ ≥ 0, min ≤ 0 ≤ max.");
            }

            RigidBody a = bodies[bodyA];
            RigidBody b = bodies[bodyB];
            Vector3d e = axisWorld.Normalized;
            Vector3d anchorA = a.Orientation.Conjugated.Rotate(anchorWorld - a.Position);
            Vector3d anchorB = b.Orientation.Conjugated.Rotate(anchorWorld - b.Position);
            Vector3d axisLocal = a.Orientation.Conjugated.Rotate(e);
            // Длина покоя — по точкам крепления (не по центрам тел): s = (pB − pA)·e.
            double s0 = Vector3d.Dot(
                (b.Position + b.Orientation.Rotate(anchorB)) - (a.Position + a.Orientation.Rotate(anchorA)), e);
            double mEff = (a.Mass * b.Mass) / (a.Mass + b.Mass);
            // ζ задаётся по ОПОРНОМУ режиму (как садятся ноги): колеблется масса
            // аппарата на ногу, а не приведённая масса воздушного режима (ТЗ 6.7).
            double referenceMass = damperReferenceMass > 0d ? damperReferenceMass : mEff;
            double damping = 2d * dampingRatio * Math.Sqrt(stiffness * referenceMass);
            QuaternionD relative = a.Orientation.Conjugated * b.Orientation;

            sliders.Add(new SliderJoint(bodyA, bodyB, anchorA, anchorB, axisLocal, relative,
                s0, stiffness, damping, minTravel, maxTravel));
            return sliders.Count - 1;
        }

        public IReadOnlyList<SliderJoint> Sliders => sliders;

        /// <summary>Координата ползуна s = (pB − pA)·e в текущем состоянии.</summary>
        public double SliderCoordinate(int sliderIndex)
        {
            SliderJoint j = sliders[sliderIndex];
            RigidBody a = bodies[j.BodyA];
            RigidBody b = bodies[j.BodyB];
            Vector3d e = a.Orientation.Rotate(j.AxisA);
            Vector3d pa = a.Position + a.Orientation.Rotate(j.AnchorA);
            Vector3d pb = b.Position + b.Orientation.Rotate(j.AnchorB);
            return Vector3d.Dot(pb - pa, e);
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

        /// <summary>Один шаг фиксированной длительности dt (sequential impulses, вариант A).</summary>
        public void Step()
        {
            int n = bodies.Count;
            var xStart = new Vector3d[n];
            var qStart = new QuaternionD[n];

            var wStart = new Vector3d[n];
            for (int i = 0; i < n; i++)
            {
                RigidBody b = bodies[i];
                xStart[i] = b.Position;
                qStart[i] = b.Orientation;
                wStart[i] = b.AngularVelocityBody;
                b.Velocity = b.Velocity + ((b.ForceWorld / b.Mass) * timeStep);
                b.AngularVelocityBody = IntegrateEulerRk4(
                    b.AngularVelocityBody, b.TorqueBody, b.PrincipalInertia, timeStep);
            }

            for (int s = 0; s < sliders.Count; s++)
            {
                ApplySliderSpring(sliders[s], xStart, qStart);
                sliders[s].LambdaLow = 0d;
                sliders[s].LambdaHigh = 0d;
            }

            // Контакты генерируются по предсказанной конфигурации (x* от текущих скоростей),
            // чтобы поймать касание внутри шага.
            var predictedPosition = new Vector3d[n];
            var predictedOrientation = new QuaternionD[n];
            for (int i = 0; i < n; i++)
            {
                RigidBody b = bodies[i];
                predictedPosition[i] = xStart[i] + (b.Velocity * timeStep);
                predictedOrientation[i] = (qStart[i]
                    * RotationExponential(b.AngularVelocityBody * timeStep)).Normalized;
            }

            List<ContactRecord> contacts = null;
            if (Ground != null)
            {
                contacts = DetectContacts(predictedPosition, predictedOrientation);
            }
            else
            {
                LastContactCount = 0;
                LastNormalImpulseSum = 0d;
                LastFrictionImpulseSum = 0d;
            }

            // Единый Gauss–Seidel: стыки, ползуны и контакты в ОДНОЙ петле, чтобы импульс
            // ходил по цепочке контакт→нога→ползун→бак ВНУТРИ шага. Раздельные петли
            // (сначала связи, потом контакты) давали систематическое сползание: трение на
            // лёгкой ноге насыщалось по её массе, а бак получал реакцию лишь следующим шагом.
            for (int it = 0; it < iterations; it++)
            {
                for (int j = 0; j < joints.Count; j++)
                {
                    RigidJoint joint = joints[j];
                    for (int k = 0; k < joint.AnchorA.Length; k++)
                    {
                        for (int axis = 0; axis < 3; axis++)
                        {
                            ApplyJointAxisImpulse(joint, k, WorldAxes[axis], xStart, qStart);
                        }
                    }
                }

                for (int s = 0; s < sliders.Count; s++)
                {
                    SolveSlider(sliders[s], xStart, qStart);
                }

                if (contacts != null)
                {
                    for (int c = 0; c < contacts.Count; c++)
                    {
                        SolveNormalAndFriction(contacts[c], predictedOrientation);
                    }
                }
            }

            if (contacts != null)
            {
                double normalSum = 0d;
                double frictionSum = 0d;
                for (int c = 0; c < contacts.Count; c++)
                {
                    normalSum += contacts[c].LambdaN;
                    frictionSum += contacts[c].FrictionAcc.Magnitude;
                }

                LastNormalImpulseSum = normalSum;
                LastFrictionImpulseSum = frictionSum;
            }

            // Позиции — по ИТОГОВЫМ скоростям шага; ориентация — по средней ω (O(dt²)),
            // чтобы импульсы связей и контактов были согласованы с перемещением.
            for (int i = 0; i < n; i++)
            {
                RigidBody b = bodies[i];
                predictedPosition[i] = xStart[i] + (b.Velocity * timeStep);
                predictedOrientation[i] = (qStart[i]
                    * RotationExponential(((wStart[i] + b.AngularVelocityBody) * 0.5d) * timeStep)).Normalized;
            }

            // Проекции по положению: проникновение контактов и жёсткие упоры хода.
            if (contacts != null)
            {
                for (int it = 0; it < iterations; it++)
                {
                    for (int c = 0; c < contacts.Count; c++)
                    {
                        ProjectPenetration(contacts[c], predictedPosition, predictedOrientation);
                    }
                }
            }

            for (int it = 0; it < iterations; it++)
            {
                for (int s = 0; s < sliders.Count; s++)
                {
                    ProjectSliderLimits(sliders[s], predictedPosition, predictedOrientation);
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

        /// <summary>
        /// Проекция нарушения хода ползуна по положению (односторонняя). C_lo = s − (s0+min) ≥ 0,
        /// C_hi = (s0+max) − s ≥ 0. Наружу упора — вдоль оси e со знаком, увеличивающим s.
        /// </summary>
        private void ProjectSliderLimits(SliderJoint j, Vector3d[] position, QuaternionD[] orientation)
        {
            int ia = j.BodyA;
            int ib = j.BodyB;
            RigidBody a = bodies[ia];
            RigidBody b = bodies[ib];
            QuaternionD qa = orientation[ia];
            QuaternionD qb = orientation[ib];
            Vector3d e = qa.Rotate(j.AxisA).Normalized;
            Vector3d ra = qa.Rotate(j.AnchorA);
            Vector3d rb = qb.Rotate(j.AnchorB);
            Vector3d pa = position[ia] + ra;
            Vector3d pb = position[ib] + rb;
            double s = Vector3d.Dot(pb - pa, e);

            double cLo = (j.RestCoordinate + j.MinTravel) - s;
            double cHi = s - (j.RestCoordinate + j.MaxTravel);
            double violation;
            double sign;
            if (cLo > 0d)
            {
                violation = cLo;
                sign = 1d;
            }
            else if (cHi > 0d)
            {
                violation = cHi;
                sign = -1d;
            }
            else
            {
                return;
            }

            Vector3d axis = e * sign;
            Vector3d rxa = Vector3d.Cross(ra, axis);
            Vector3d rxb = Vector3d.Cross(rb, axis);
            double w = (1d / a.Mass) + (1d / b.Mass)
                + Vector3d.Dot(rxa, a.ApplyInverseInertiaWorld(qa, rxa))
                + Vector3d.Dot(rxb, b.ApplyInverseInertiaWorld(qb, rxb));
            if (!(w > 0d))
            {
                return;
            }

            double lambda = violation / w;
            position[ib] = position[ib] + (axis * (lambda / b.Mass));
            position[ia] = position[ia] - (axis * (lambda / a.Mass));
            orientation[ib] = (RotationExponential(b.ApplyInverseInertiaWorld(qb, rxb) * lambda) * qb).Normalized;
            orientation[ia] = (RotationExponential(a.ApplyInverseInertiaWorld(qa, rxa) * (-lambda)) * qa).Normalized;
        }

        /// <summary>
        /// Импульс вдоль мировой оси e в точке якоря k стыка: обнуляет относительную скорость
        /// точек вдоль e с Baumgarte-смещением −(β/dt)·C, C = (pA − pB)·e в средней конфигурации.
        /// </summary>
        private void ApplyJointAxisImpulse(RigidJoint joint, int anchorIndex, Vector3d axis,
            Vector3d[] xMid, QuaternionD[] qMid)
        {
            int ia = joint.BodyA;
            int ib = joint.BodyB;
            RigidBody a = bodies[ia];
            RigidBody b = bodies[ib];
            QuaternionD qa = qMid[ia];
            QuaternionD qb = qMid[ib];

            Vector3d ra = qa.Rotate(joint.AnchorA[anchorIndex]);
            Vector3d rb = qb.Rotate(joint.AnchorB[anchorIndex]);
            Vector3d pa = xMid[ia] + ra;
            Vector3d pb = xMid[ib] + rb;

            double c = Vector3d.Dot(pa - pb, axis);
            double vn = Vector3d.Dot(PointVelocity(ia, qa, ra) - PointVelocity(ib, qb, rb), axis);
            double w = PairEffectiveMass(a, b, qa, qb, ra, rb, axis);
            if (!(w > 0d))
            {
                return;
            }

            // Связь на средней скорости шага: (vn_pre + vn_end)/2 = −(β/dt)·C.
            double lambda = -(vn + (JointBaumgarte / timeStep) * c) / w;
            Vector3d impulse = axis * lambda;
            ApplyVelocityImpulse(ia, qa, ra, impulse);
            ApplyVelocityImpulse(ib, qb, rb, -impulse);
        }

        private double PairEffectiveMass(RigidBody a, RigidBody b, QuaternionD qa, QuaternionD qb,
            Vector3d ra, Vector3d rb, Vector3d axis)
        {
            Vector3d rxa = Vector3d.Cross(ra, axis);
            Vector3d rxb = Vector3d.Cross(rb, axis);
            return (1d / a.Mass) + (1d / b.Mass)
                + Vector3d.Dot(rxa, a.ApplyInverseInertiaWorld(qa, rxa))
                + Vector3d.Dot(rxb, b.ApplyInverseInertiaWorld(qb, rxb));
        }

        /// <summary>Упругость и демпфер ползуна вдоль оси: явно, по скоростям и конфигурации начала шага.</summary>
        private void ApplySliderSpring(SliderJoint j, Vector3d[] x, QuaternionD[] q)
        {
            Vector3d e = q[j.BodyA].Rotate(j.AxisA);
            Vector3d ra = q[j.BodyA].Rotate(j.AnchorA);
            Vector3d rb = q[j.BodyB].Rotate(j.AnchorB);
            double s = Vector3d.Dot((x[j.BodyB] + rb) - (x[j.BodyA] + ra), e);
            double vs = Vector3d.Dot(PointVelocity(j.BodyB, q[j.BodyB], rb) - PointVelocity(j.BodyA, q[j.BodyA], ra), e);
            double force = -j.Stiffness * (s - j.RestCoordinate) - j.Damping * vs;
            Vector3d impulse = e * (force * timeStep);
            ApplyVelocityImpulse(j.BodyB, q[j.BodyB], rb, impulse);
            ApplyVelocityImpulse(j.BodyA, q[j.BodyA], ra, -impulse);
        }

        /// <summary>
        /// Связи ползуна в средней конфигурации: две поперечные, три поворотные (Baumgarte по
        /// ошибке относительной ориентации), два односторонних ограничения хода.
        /// </summary>
        private void SolveSlider(SliderJoint j, Vector3d[] xMid, QuaternionD[] qMid)
        {
            int ia = j.BodyA;
            int ib = j.BodyB;
            RigidBody a = bodies[ia];
            RigidBody b = bodies[ib];
            QuaternionD qa = qMid[ia];
            QuaternionD qb = qMid[ib];

            Vector3d ra = qa.Rotate(j.AnchorA);
            Vector3d rb = qb.Rotate(j.AnchorB);
            Vector3d pa = xMid[ia] + ra;
            Vector3d pb = xMid[ib] + rb;
            Vector3d e = qa.Rotate(j.AxisA).Normalized;
            double beta = JointBaumgarte / timeStep;

            Vector3d u1 = Vector3d.Cross(e, Math.Abs(e.X) < 0.9d ? new Vector3d(1d, 0d, 0d) : new Vector3d(0d, 1d, 0d)).Normalized;
            Vector3d u2 = Vector3d.Cross(e, u1);
            foreach (Vector3d u in new[] { u1, u2 })
            {
                double c = Vector3d.Dot(pa - pb, u);
                double vn = Vector3d.Dot(PointVelocity(ia, qa, ra) - PointVelocity(ib, qb, rb), u);
                double w = PairEffectiveMass(a, b, qa, qb, ra, rb, u);
                if (!(w > 0d))
                {
                    continue;
                }

                double lambda = -(vn + (beta * c)) / w;
                ApplyVelocityImpulse(ia, qa, ra, u * lambda);
                ApplyVelocityImpulse(ib, qb, rb, u * (-lambda));
            }

            // Поворот: целевая ориентация B = A ⊗ rel0; ошибка — векторная часть E = qB ⊗ conj(target).
            QuaternionD target = qa * j.RelativeRest;
            QuaternionD err = qb * target.Conjugated;
            double sign = err.W >= 0d ? 1d : -1d;
            Vector3d theta = new Vector3d(2d * sign * err.X, 2d * sign * err.Y, 2d * sign * err.Z);
            Vector3d[] worldAxes = WorldAxes;
            for (int k = 0; k < 3; k++)
            {
                Vector3d w3 = worldAxes[k];
                Vector3d wB = qb.Rotate(b.AngularVelocityBody);
                Vector3d wA = qa.Rotate(a.AngularVelocityBody);
                double dw = Vector3d.Dot(wB - wA, w3);
                double eff = Vector3d.Dot(w3, a.ApplyInverseInertiaWorld(qa, w3))
                    + Vector3d.Dot(w3, b.ApplyInverseInertiaWorld(qb, w3));
                if (!(eff > 0d))
                {
                    continue;
                }

                double lambda = -(dw + (beta * Vector3d.Dot(theta, w3))) / eff;
                ApplyAngularImpulse(ib, qb, w3 * lambda);
                ApplyAngularImpulse(ia, qa, w3 * (-lambda));
            }

            // Ограничения хода. s = (pB − pA)·e; C_lo = s − (s0 + min), C_hi = (s0 + max) − s.
            double s = Vector3d.Dot(pb - pa, e);
            double vs = Vector3d.Dot(PointVelocity(ib, qb, rb) - PointVelocity(ia, qa, ra), e);
            double wAx = PairEffectiveMass(a, b, qa, qb, ra, rb, e);
            if (wAx > 0d)
            {
                // Ход — жёсткий упор: полная коррекция (β = 1) по положению, без смягчения.
                double clo = s - (j.RestCoordinate + j.MinTravel);
                double delta = -(vs + ((1d / timeStep) * clo)) / wAx;
                double newAcc = Math.Max(0d, j.LambdaLow + delta);
                double d = newAcc - j.LambdaLow;
                j.LambdaLow = newAcc;
                ApplyVelocityImpulse(ib, qb, rb, e * d);
                ApplyVelocityImpulse(ia, qa, ra, e * (-d));

                double chi = (j.RestCoordinate + j.MaxTravel) - s;
                vs = Vector3d.Dot(PointVelocity(ib, qb, rb) - PointVelocity(ia, qa, ra), e);
                double deltaHi = -(-vs + ((1d / timeStep) * chi)) / wAx;
                double newAccHi = Math.Max(0d, j.LambdaHigh + deltaHi);
                double dHi = newAccHi - j.LambdaHigh;
                j.LambdaHigh = newAccHi;
                ApplyVelocityImpulse(ib, qb, rb, e * (-dHi));
                ApplyVelocityImpulse(ia, qa, ra, e * dHi);
            }
        }

        private void ApplyAngularImpulse(int bodyIndex, QuaternionD q, Vector3d angularImpulseWorld)
        {
            RigidBody b = bodies[bodyIndex];
            b.AngularVelocityBody = b.AngularVelocityBody + q.Conjugated.Rotate(
                b.ApplyInverseInertiaWorld(q, angularImpulseWorld));
        }

        /// <summary>Коэффициент Baumgarte для дрейфа якорей стыка (безразмерный, 0..1).</summary>
        public double JointBaumgarte { get; set; } = 0.2d;

        /// <summary>Геометрия контакта; null — контактов нет (фаза 2 без изменений).</summary>
        public IContactGround Ground { get; set; }
        public double StaticFriction { get; set; } = 0.7d;
        public double KineticFriction { get; set; } = 0.5d;
        public double Restitution { get; set; } = 0d;
        public double ImpactThreshold { get; set; } = 0.01d;

        /// <summary>Телеметрия последнего шага: число контактов и суммы импульсов (Н·с) — для отладки.</summary>
        public int LastContactCount { get; private set; }
        public double LastNormalImpulseSum { get; private set; }
        public double LastFrictionImpulseSum { get; private set; }

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
        /// Генерация контактов по конфигурации: точки тела ниже поверхности.
        /// Решение импульсов — в общей петле Step (стыки, ползуны, контакты вместе).
        /// </summary>
        private List<ContactRecord> DetectContacts(Vector3d[] position, QuaternionD[] orientation)
        {
            var contacts = new List<ContactRecord>();
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

            LastContactCount = contacts.Count;
            if (contacts.Count == 0)
            {
                LastNormalImpulseSum = 0d;
                LastFrictionImpulseSum = 0d;
            }

            return contacts;
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
            ApplyVelocityImpulse(c.Body, q, c.R, c.Normal * dl);

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
            ApplyVelocityImpulse(c.Body, q, c.R, delta);
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

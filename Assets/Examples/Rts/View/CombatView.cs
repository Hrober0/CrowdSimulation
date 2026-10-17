using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Examples.Rts
{
    /// <summary>What a soldier is drawn holding. Decided by the view from the weapon's reach, not by `Rts`.</summary>
    public enum CombatWeaponKind : byte
    {
        Sword,
        Gun,
    }

    /// <summary>The three prefabs the combat view is built from. See <c>RtsSceneBuilder</c> for their layout.</summary>
    [Serializable]
    public struct CombatViewPrefabs
    {
        [Tooltip("A body with a 'Hand' child holding the blade. The hand is what swings.")]
        public GameObject Sword;

        [Tooltip("A body with a 'Gun' child and a 'Gun/Flash' child shown for a moment on every shot.")]
        public GameObject Gun;

        [Tooltip("One round in flight. Drawn along +Y, so it is turned to face where it is going.")]
        public GameObject Bullet;
    }

    /// <summary>
    /// What one armed agent looks like this frame. The system reads the simulation; this is all it hands over.
    /// </summary>
    public struct SoldierFrame
    {
        public float2 Position;
        public float2 Velocity;
        public float MaxSpeed;
        public byte Faction;
        public CombatWeaponKind Kind;

        /// <summary>Where it is aiming, if it is fighting something. Faces this rather than where it walks.</summary>
        public bool HasAim;
        public float2 Aim;
    }

    /// <summary>
    /// Soldiers, their weapons and their shots, drawn on top of the agent view (design §10).
    ///
    /// The simulation has no projectiles: a <c>Weapon</c> hurts its target the tick it fires, through the damage
    /// queue. So everything here is presentation and nothing feeds back - a bullet is a picture of a shot that
    /// has already landed, and a sword swing is a picture of a hit that has already been counted. That is the
    /// one-way contract of §10, and it is why a missed bullet cannot exist.
    ///
    /// Shots are detected rather than reported. A weapon that fires moves its <c>NextShotTime</c> forward, so a
    /// change in it since the last frame *is* a shot, and the view needs no event or extra component from the
    /// simulation to see one.
    ///
    /// Main-thread transforms, unlike the agent pool: soldiers are a fraction of the crowd, each view is several
    /// transforms with an animation on them, and parenting the instances here keeps the hierarchy readable.
    /// </summary>
    public sealed class CombatView : IDisposable
    {
        private const float SWING_SECONDS = 0.18f;
        private const float RECOVER_SECONDS = 0.25f;
        private const float REST_ANGLE = -25f;
        private const float WINDUP_ANGLE = -95f;
        private const float STRIKE_ANGLE = 70f;
        private const float FLASH_SECONDS = 0.06f;
        private const float RECOIL_SECONDS = 0.12f;
        private const float RECOIL_DISTANCE = 0.12f;
        private const float FACING_SPEED_FRACTION = 0.15f;

        private readonly CombatViewPrefabs _prefabs;
        private readonly Transform _root;

        private readonly Dictionary<Entity, SoldierView> _soldiers = new();
        private readonly Stack<SoldierView> _idleSwords = new();
        private readonly Stack<SoldierView> _idleGuns = new();
        private readonly List<Entity> _unseen = new();

        /// <summary>Every weapon's reload time as last seen, and the frame it was seen on.</summary>
        private readonly Dictionary<Entity, (double NextShot, int Seen)> _lastShot = new();

        private readonly List<Bullet> _bullets = new();
        private readonly Stack<Transform> _idleBullets = new();

        private readonly List<GameObject> _created = new();

        private int _tick;

        public CombatView(CombatViewPrefabs prefabs, Transform root)
        {
            _prefabs = prefabs;
            _root = root;
        }

        /// <summary>Sorting offset of the soldiers towards the camera, in world units.</summary>
        public float Depth { get; set; } = 0.1f;

        /// <summary>Sorting offset of bullets in flight. Above the soldiers, so a shot is never hidden by a body.</summary>
        public float BulletDepth { get; set; } = 0.3f;

        /// <summary>Cells a second. Fast enough to read as a shot, slow enough to be seen at all.</summary>
        public float BulletSpeed { get; set; } = 24f;

        public float TurnDegreesPerSecond { get; set; } = 540f;

        public int SoldierCount => _soldiers.Count;

        public int BulletCount => _bullets.Count;

        public void BeginFrame() => _tick++;

        /// <summary>
        /// Whether that weapon has fired since it was last looked at. The first sighting is never a shot: a
        /// weapon seen for the first time has a reload time from before the view knew about it.
        /// </summary>
        public bool NoteShot(Entity shooter, double nextShotTime)
        {
            bool known = _lastShot.TryGetValue(shooter, out (double NextShot, int Seen) previous);
            _lastShot[shooter] = (nextShotTime, _tick);

            return known && nextShotTime > previous.NextShot;
        }

        public void Show(Entity entity, in SoldierFrame frame)
        {
            if (_soldiers.TryGetValue(entity, out SoldierView view) && view.Kind != frame.Kind)
            {
                Release(entity, view);
                view = null;
            }

            if (view == null)
            {
                view = Acquire(frame.Kind);
                _soldiers[entity] = view;

                view.Facing = Heading(frame) ?? view.Facing;
                view.Root.rotation = SimToWorld.Rotation(view.Facing);
                view.SwingStart = double.NegativeInfinity;
                view.FlashUntil = double.NegativeInfinity;
            }

            if (view.Faction != frame.Faction || !view.Tinted)
            {
                Tint(view.Body, RtsFactions.Colour(frame.Faction));
                view.Faction = frame.Faction;
                view.Tinted = true;
            }

            view.Frame = frame;
            view.SeenTick = _tick;
        }

        /// <summary>
        /// Plays the attack on the soldier that made it, and sends a round from a gun. Called after
        /// <see cref="Show"/> for the shooter, so a soldier that fires on the frame it appears still animates.
        /// </summary>
        public void Fire(Entity shooter, float2 from, float2 target, double now)
        {
            if (!_soldiers.TryGetValue(shooter, out SoldierView view))
            {
                // A turret, or something out of view: the round still flies, from wherever it was fired.
                Launch(from, target, now);
                return;
            }

            if (view.Kind == CombatWeaponKind.Sword)
            {
                view.SwingStart = now;
                return;
            }

            view.FlashUntil = now + FLASH_SECONDS;
            view.RecoilStart = now;

            Vector3 muzzle = view.Flash != null ? view.Flash.position : view.Root.position;
            Launch(SimToWorld.ToSim(muzzle), target, now);
        }

        /// <summary>Hands back what nothing showed, then animates everything that is left.</summary>
        public void EndFrame(double now, float deltaTime)
        {
            _unseen.Clear();
            foreach ((Entity entity, SoldierView view) in _soldiers)
            {
                if (view.SeenTick != _tick)
                {
                    _unseen.Add(entity);
                }
            }

            foreach (Entity entity in _unseen)
            {
                Release(entity, _soldiers[entity]);
            }

            // Shots are tracked for turrets too, which never have a soldier view to be released with.
            _unseen.Clear();
            foreach ((Entity shooter, (double NextShot, int Seen) shot) in _lastShot)
            {
                if (shot.Seen != _tick)
                {
                    _unseen.Add(shooter);
                }
            }

            foreach (Entity shooter in _unseen)
            {
                _lastShot.Remove(shooter);
            }

            float maxTurn = math.radians(TurnDegreesPerSecond) * deltaTime;
            foreach (SoldierView view in _soldiers.Values)
            {
                Animate(view, now, maxTurn);
            }

            MoveBullets(now);
        }

        public void Dispose()
        {
            foreach (GameObject instance in _created)
            {
                if (instance == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(instance);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }

            _created.Clear();
            _soldiers.Clear();
            _idleSwords.Clear();
            _idleGuns.Clear();
            _bullets.Clear();
            _idleBullets.Clear();
            _lastShot.Clear();
        }

        private void Animate(SoldierView view, double now, float maxTurn)
        {
            SoldierFrame frame = view.Frame;

            if (Heading(frame) is { } heading)
            {
                float turn = math.clamp(SignedAngle(view.Facing, heading), -maxTurn, maxTurn);
                view.Facing = Rotate(view.Facing, turn);
            }

            view.Root.SetPositionAndRotation(
                SimToWorld.Position(frame.Position, Depth),
                SimToWorld.Rotation(view.Facing));

            if (view.Kind == CombatWeaponKind.Sword)
            {
                if (view.Hand != null)
                {
                    view.Hand.localRotation = Quaternion.Euler(0f, 0f, SwingAngle(now - view.SwingStart));
                }

                return;
            }

            if (view.Flash != null)
            {
                bool flashing = now < view.FlashUntil;
                if (view.Flash.gameObject.activeSelf != flashing)
                {
                    view.Flash.gameObject.SetActive(flashing);
                }
            }

            if (view.Gun != null)
            {
                float recoil = 1f - math.saturate((float)(now - view.RecoilStart) / RECOIL_SECONDS);
                view.Gun.localPosition = view.GunRest - new Vector3(0f, RECOIL_DISTANCE * recoil, 0f);
            }
        }

        /// <summary>
        /// Wind-up, a fast cut across the front, and a slower return to guard. Degrees about the hand, with
        /// zero pointing forward and positive swinging across to the left.
        /// </summary>
        private static float SwingAngle(double sinceSwing)
        {
            float t = (float)sinceSwing;
            if (t < 0f || t >= SWING_SECONDS + RECOVER_SECONDS)
            {
                return REST_ANGLE;
            }

            if (t < SWING_SECONDS * 0.3f)
            {
                return math.lerp(REST_ANGLE, WINDUP_ANGLE, t / (SWING_SECONDS * 0.3f));
            }

            if (t < SWING_SECONDS)
            {
                float strike = (t - SWING_SECONDS * 0.3f) / (SWING_SECONDS * 0.7f);
                return math.lerp(WINDUP_ANGLE, STRIKE_ANGLE, 1f - (1f - strike) * (1f - strike));
            }

            return math.lerp(STRIKE_ANGLE, REST_ANGLE, (t - SWING_SECONDS) / RECOVER_SECONDS);
        }

        /// <summary>The target if it has one, otherwise where it is walking, otherwise nothing new.</summary>
        private static float2? Heading(in SoldierFrame frame)
        {
            if (frame.HasAim && math.lengthsq(frame.Aim - frame.Position) > math.EPSILON)
            {
                return math.normalize(frame.Aim - frame.Position);
            }

            float speed = math.length(frame.Velocity);
            if (speed > math.EPSILON && speed >= frame.MaxSpeed * FACING_SPEED_FRACTION)
            {
                return frame.Velocity / speed;
            }

            return null;
        }

        private void Launch(float2 from, float2 to, double now)
        {
            float distance = math.distance(from, to);
            if (distance < 0.3f || _prefabs.Bullet == null)
            {
                return;
            }

            Transform bullet = _idleBullets.Count > 0 ? _idleBullets.Pop() : Create(_prefabs.Bullet).transform;
            bullet.gameObject.SetActive(true);
            bullet.SetPositionAndRotation(SimToWorld.Position(from, BulletDepth), SimToWorld.Rotation(to - from));

            _bullets.Add(new Bullet
            {
                Transform = bullet,
                From = from,
                To = to,
                Start = now,
                Duration = distance / math.max(BulletSpeed, 1f),
            });
        }

        private void MoveBullets(double now)
        {
            for (int i = _bullets.Count - 1; i >= 0; i--)
            {
                Bullet bullet = _bullets[i];
                float t = (float)((now - bullet.Start) / bullet.Duration);

                if (t >= 1f)
                {
                    bullet.Transform.gameObject.SetActive(false);
                    _idleBullets.Push(bullet.Transform);
                    _bullets.RemoveAt(i);
                    continue;
                }

                bullet.Transform.position = SimToWorld.Position(math.lerp(bullet.From, bullet.To, t), BulletDepth);
            }
        }

        private SoldierView Acquire(CombatWeaponKind kind)
        {
            Stack<SoldierView> idle = kind == CombatWeaponKind.Sword ? _idleSwords : _idleGuns;
            if (idle.Count > 0)
            {
                SoldierView reused = idle.Pop();
                reused.Root.gameObject.SetActive(true);
                return reused;
            }

            GameObject prefab = kind == CombatWeaponKind.Sword ? _prefabs.Sword : _prefabs.Gun;
            GameObject instance = Create(prefab);
            instance.SetActive(true);

            Transform root = instance.transform;
            Transform gun = root.Find("Gun");

            return new SoldierView
            {
                Kind = kind,
                Root = root,
                Body = root.Find("Body") is { } body ? body.GetComponent<Renderer>() : null,
                Hand = root.Find("Hand"),
                Gun = gun,
                GunRest = gun != null ? gun.localPosition : Vector3.zero,
                Flash = gun != null ? gun.Find("Flash") : null,
                Facing = new float2(0f, 1f),
            };
        }

        private void Release(Entity entity, SoldierView view)
        {
            _soldiers.Remove(entity);
            view.Root.gameObject.SetActive(false);
            (view.Kind == CombatWeaponKind.Sword ? _idleSwords : _idleGuns).Push(view);
        }

        private GameObject Create(GameObject prefab)
        {
            GameObject instance = UnityEngine.Object.Instantiate(prefab, _root);
            instance.SetActive(false);
            _created.Add(instance);
            return instance;
        }

        private static void Tint(Renderer renderer, Color colour)
        {
            if (renderer == null)
            {
                return;
            }

            // A property block rather than renderer.material, which would leak a material per soldier.
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", colour);
            block.SetColor("_Color", colour);
            renderer.SetPropertyBlock(block);
        }

        private static float SignedAngle(float2 from, float2 to) =>
            math.atan2(from.x * to.y - from.y * to.x, math.dot(from, to));

        private static float2 Rotate(float2 direction, float radians)
        {
            math.sincos(radians, out float sin, out float cos);
            return new float2(
                direction.x * cos - direction.y * sin,
                direction.x * sin + direction.y * cos
            );
        }

        private sealed class SoldierView
        {
            public CombatWeaponKind Kind;
            public Transform Root;
            public Renderer Body;
            public Transform Hand;
            public Transform Gun;
            public Vector3 GunRest;
            public Transform Flash;

            public SoldierFrame Frame;
            public float2 Facing;
            public byte Faction;
            public bool Tinted;
            public int SeenTick;

            public double SwingStart;
            public double FlashUntil;
            public double RecoilStart;
        }

        private struct Bullet
        {
            public Transform Transform;
            public float2 From;
            public float2 To;
            public double Start;
            public float Duration;
        }
    }
}

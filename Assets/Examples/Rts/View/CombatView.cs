using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

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
    /// Where a soldier stands and which way it faces are written by a parallel transform job, the way the agent
    /// pool writes its views (§10): at sixteen hundred soldiers, writing them one by one on the main thread was
    /// most of this view's cost. Only the parts that animate - a hand mid-swing, a gun mid-kick, a flash - are
    /// written on the main thread, and only while they move.
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

        /// <summary>
        /// The live soldiers by slot, in lockstep with <see cref="_roots"/> and <see cref="_rootData"/>: only
        /// <see cref="AddSlot"/> and <see cref="RemoveSlot"/> change their shape, and both keep all three
        /// aligned by swapping the last slot into a hole.
        /// </summary>
        private readonly List<SoldierView> _bySlot = new();

        private TransformAccessArray _roots;
        private NativeList<RootData> _rootData;
        private readonly Stack<SoldierView> _idleSwords = new();
        private readonly Stack<SoldierView> _idleGuns = new();
        private readonly List<Entity> _unseen = new();

        /// <summary>Every weapon's reload time as last seen, and the frame it was seen on.</summary>
        private readonly Dictionary<Entity, (double NextShot, int Seen)> _lastShot = new();

        private readonly List<Bullet> _bullets = new();
        private readonly Stack<Transform> _idleBullets = new();

        private readonly List<GameObject> _created = new();

        /// <summary>
        /// One body material per side, made from the prefab's on first use. Shared materials rather than a
        /// per-renderer property block, because a property block takes a renderer out of the SRP batcher: at
        /// sixteen hundred soldiers that was thousands of unbatched draws a frame for two colours.
        /// </summary>
        private readonly Dictionary<byte, Material> _sideMaterials = new();

        private int _tick;

        public CombatView(CombatViewPrefabs prefabs, Transform root)
        {
            _prefabs = prefabs;
            _root = root;

            _roots = new TransformAccessArray(256);
            _rootData = new NativeList<RootData>(256, Allocator.Persistent);
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

                float2 facing = Heading(frame) ?? new float2(0f, 1f);
                view.Root.rotation = SimToWorld.Rotation(facing);
                AddSlot(view, facing);

                view.SwingStart = double.NegativeInfinity;
                view.FlashUntil = double.NegativeInfinity;
                view.RecoilStart = double.NegativeInfinity;

                // Whatever pose the last soldier left it in is written back to rest on the first frame.
                view.AtRest = false;
            }

            if (view.Faction != frame.Faction || !view.Tinted)
            {
                if (view.Body != null)
                {
                    view.Body.sharedMaterial = SideMaterial(frame.Faction, view.Body.sharedMaterial);
                }

                view.Faction = frame.Faction;
                view.Tinted = true;
            }

            RootData data = _rootData[view.Slot];
            data.Position = frame.Position;
            data.Velocity = frame.Velocity;
            data.MaxSpeed = frame.MaxSpeed;
            data.Aim = frame.Aim;
            data.HasAim = frame.HasAim;
            _rootData[view.Slot] = data;

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

            new RootJob
            {
                Roots = _rootData.AsArray(),
                Depth = Depth,
                MaxTurn = math.radians(TurnDegreesPerSecond) * deltaTime,
            }.Schedule(_roots).Complete();

            foreach (SoldierView view in _bySlot)
            {
                Animate(view, now);
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

            foreach (Material material in _sideMaterials.Values)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(material);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(material);
                }
            }

            if (_roots.isCreated)
            {
                _roots.Dispose();
            }

            if (_rootData.IsCreated)
            {
                _rootData.Dispose();
            }

            _sideMaterials.Clear();
            _created.Clear();
            _soldiers.Clear();
            _bySlot.Clear();
            _idleSwords.Clear();
            _idleGuns.Clear();
            _bullets.Clear();
            _idleBullets.Clear();
            _lastShot.Clear();
        }

        /// <summary>The moving parts of a weapon. The body's place and facing are the job's (see RootJob).</summary>
        private static void Animate(SoldierView view, double now)
        {
            if (view.Kind == CombatWeaponKind.Sword)
            {
                // Written only while a swing is playing, and once more when it ends. A soldier at guard is
                // most soldiers most of the time, and a transform written with the value it already has is
                // still a transform marked changed.
                bool swinging = now - view.SwingStart < SWING_SECONDS + RECOVER_SECONDS;
                if (view.Hand != null && (swinging || !view.AtRest))
                {
                    view.Hand.localRotation = Quaternion.Euler(0f, 0f, SwingAngle(now - view.SwingStart));
                    view.AtRest = !swinging;
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

            // The same rule for the kick of a gun as for the swing of a sword.
            bool recoiling = now - view.RecoilStart < RECOIL_SECONDS;
            if (view.Gun != null && (recoiling || !view.AtRest))
            {
                float recoil = 1f - math.saturate((float)(now - view.RecoilStart) / RECOIL_SECONDS);
                view.Gun.localPosition = view.GunRest - new Vector3(0f, RECOIL_DISTANCE * recoil, 0f);
                view.AtRest = !recoiling;
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

        /// <summary>
        /// Everything the transform job needs about one soldier: where it is, what it is doing, and which way it
        /// faces now - the one thing the job writes back, because the turn is eased over frames.
        /// </summary>
        private struct RootData
        {
            public float2 Position;
            public float2 Velocity;
            public float MaxSpeed;
            public float2 Aim;
            public bool HasAim;
            public float2 Facing;
        }

        [BurstCompile]
        private struct RootJob : IJobParallelForTransform
        {
            public NativeArray<RootData> Roots;
            public float Depth;

            /// <summary>Radians a soldier may turn this frame.</summary>
            public float MaxTurn;

            public void Execute(int index, TransformAccess transform)
            {
                RootData root = Roots[index];

                if (TryHeading(root, out float2 heading))
                {
                    float turn = math.clamp(SignedAngle(root.Facing, heading), -MaxTurn, MaxTurn);
                    root.Facing = Rotate(root.Facing, turn);
                    Roots[index] = root;
                }

                transform.position = SimToWorld.Position(root.Position, Depth);
                transform.rotation = SimToWorld.Rotation(root.Facing);
            }

            /// <summary>The job's half of <see cref="Heading"/>: the same rule, without the nullable.</summary>
            private static bool TryHeading(in RootData root, out float2 heading)
            {
                if (root.HasAim && math.lengthsq(root.Aim - root.Position) > math.EPSILON)
                {
                    heading = math.normalize(root.Aim - root.Position);
                    return true;
                }

                float speed = math.length(root.Velocity);
                if (speed > math.EPSILON && speed >= root.MaxSpeed * FACING_SPEED_FRACTION)
                {
                    heading = root.Velocity / speed;
                    return true;
                }

                heading = default;
                return false;
            }
        }

        private void Launch(float2 from, float2 to, double now)
        {
            float distance = math.distance(from, to);
            if (distance < 0.3f || _prefabs.Bullet == null)
            {
                return;
            }

            Transform bullet = _idleBullets.Count > 0
                ? _idleBullets.Pop()
                : Create(_prefabs.Bullet, parented: true).transform;
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
            };
        }

        private void Release(Entity entity, SoldierView view)
        {
            _soldiers.Remove(entity);
            RemoveSlot(view);
            view.Root.gameObject.SetActive(false);
            (view.Kind == CombatWeaponKind.Sword ? _idleSwords : _idleGuns).Push(view);
        }

        private void AddSlot(SoldierView view, float2 facing)
        {
            view.Slot = _bySlot.Count;
            _bySlot.Add(view);
            _roots.Add(view.Root);
            _rootData.Add(new RootData { Facing = facing });
        }

        private void RemoveSlot(SoldierView view)
        {
            int slot = view.Slot;
            int last = _bySlot.Count - 1;

            _roots.RemoveAtSwapBack(slot);
            _rootData.RemoveAtSwapBack(slot);
            _bySlot[slot] = _bySlot[last];
            _bySlot.RemoveAt(last);

            if (slot < _bySlot.Count)
            {
                _bySlot[slot].Slot = slot;
            }
        }

        /// <summary>
        /// Soldiers are scene roots, not children of this view's object, because a transform job only runs in
        /// parallel across roots - parenting them for a tidy hierarchy would quietly serialise it (the agent
        /// pool says the same). Bullets are few and stay under it.
        /// </summary>
        private GameObject Create(GameObject prefab, bool parented = false)
        {
            GameObject instance = UnityEngine.Object.Instantiate(prefab, parented ? _root : null);
            instance.SetActive(false);
            _created.Add(instance);
            return instance;
        }

        /// <summary>The body material for a side, made from <paramref name="source"/> the first time it is asked for.</summary>
        private Material SideMaterial(byte side, Material source)
        {
            if (_sideMaterials.TryGetValue(side, out Material material))
            {
                return material;
            }

            Color colour = RtsFactions.Colour(side);
            material = new Material(source) { name = $"{source.name} ({RtsFactions.Name(side)})" };
            material.SetColor("_BaseColor", colour);
            material.SetColor("_Color", colour);

            _sideMaterials[side] = material;
            return material;
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

            /// <summary>Index into the slot lists, kept current by every swap-back.</summary>
            public int Slot;

            public byte Faction;
            public bool Tinted;

            /// <summary>Whether the hand or the gun is already in its rest pose, so it need not be written again.</summary>
            public bool AtRest;
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

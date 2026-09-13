using GridNav;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>What an agent is made of, at the moment it is made.</summary>
    public struct AgentSpec
    {
        public float2 Position;

        public float MaxSpeed;

        /// <summary>Must stay below ~0.45 of a cell, or agents clip the corners of blocked cells (§3).</summary>
        public float Radius;

        /// <summary>Units carried per trip. Zero means the agent will never be given a haul.</summary>
        public int CarryCapacity;

        /// <summary>Non-positive means <see cref="AgentFactory.DEFAULT_HEALTH"/>. Nobody means zero.</summary>
        public int MaxHealth;

        /// <summary>Which side it is on. See <see cref="Rts.Faction"/>.</summary>
        public byte Faction;

        /// <summary>
        /// What it is armed with. An unarmed spec - the default - makes a worker; anything with a range and
        /// a damage makes a soldier. There is no third thing to set, because a soldier *is* an agent whose
        /// weapon is switched on (§14 step 13).
        /// </summary>
        public Weapon Weapon;

        /// <summary>How far an armed agent will chase. Ignored for a worker. See <see cref="Rts.Post"/>.</summary>
        public float Leash;

        /// <summary>An ordinary worker: the numbers everything in this project has always spawned with.</summary>
        public static AgentSpec Worker => new()
        {
            MaxSpeed = 3f,
            Radius = 0.42f,
            CarryCapacity = 10,
            MaxHealth = 100,
        };
    }

    /// <summary>
    /// The one place an agent is made (design §9, §14 step 12).
    ///
    /// §9 says there is one archetype for every agent whatever it ends up doing, and until a building could
    /// produce one that was easy to keep true by hand - there was a single system that spawned crowds. A test
    /// world is a second source and a scene builder a third, and hand-written copies of one archetype are
    /// chances for a component added later to be missing from all but one. A missing enableable component
    /// does not fail loudly; it silently drops the agent out of whichever query needed it, which is the kind
    /// of bug that gets found three steps further on.
    ///
    /// Static methods with no state of their own: what an agent is belongs here, *who* makes one does not.
    /// </summary>
    public static class AgentFactory
    {
        /// <summary>
        /// What an agent is worth when nothing says otherwise.
        ///
        /// A default rather than a floor of one, because a caller that has not thought about health has not
        /// asked for an agent that dies to a single hit - and until step 12 no caller had any reason to think
        /// about it at all.
        /// </summary>
        public const int DEFAULT_HEALTH = 100;

        /// <summary>
        /// How far a soldier will chase when nothing says otherwise, in cells. Long enough to cross a
        /// building's approach and short enough that a raider cannot walk a garrison off its post.
        /// </summary>
        public const float DEFAULT_LEASH = 10f;

        /// <summary>
        /// One archetype for every agent, whatever it ends up doing (§9). What a hauler, a worker and a
        /// soldier differ in is the contents of their <see cref="TaskStep"/> buffer and one enableable bit,
        /// not their components - which is what makes becoming a soldier free (§14 step 13).
        /// </summary>
        public static EntityArchetype Archetype(EntityManager entities) => entities.CreateArchetype(
            typeof(AgentMove),
            typeof(PathFollow),
            typeof(ArrivedTag),
            typeof(PathRoute),
            typeof(TaskStep),
            typeof(InsideBuilding),
            typeof(InteriorClaim),
            typeof(DoorUse),
            typeof(OnBridge),
            typeof(Carry),
            typeof(AssignedOrder),
            typeof(MovementWatchdog),
            typeof(ViewVisible),
            typeof(Health),
            typeof(Faction),
            typeof(Weapon),
            typeof(Post)
        );

        /// <summary>
        /// An agent standing where it was put, with nothing to do. Idle by definition, which is what gets it
        /// picked up by <see cref="IdleAssignSystem"/> and the order market on the next economy tick - so a
        /// caller that wants it to walk somewhere adds the step and nothing else.
        /// </summary>
        public static Entity Create(EntityManager entities, EntityArchetype archetype, in AgentSpec spec)
        {
            Entity agent = entities.CreateEntity(archetype);

            entities.SetComponentData(agent, new AgentMove
            {
                Entity = agent,
                Position = spec.Position,
                MaxSpeed = spec.MaxSpeed,
                Radius = spec.Radius,
            });

            entities.SetComponentData(agent, new PathFollow
            {
                ArriveDistance = 0.4f,

                // -1 is no chunk, which is what makes the first frame route rather than trust these.
                RoutedChunk = -1,

                Traversal = TraversalOf(spec.Weapon, spec.Faction),
            });

            entities.SetComponentData(agent, new Carry { Capacity = spec.CarryCapacity });
            entities.SetComponentData(agent, new MovementWatchdog { LastProgressPosition = spec.Position });
            entities.SetComponentData(agent, Health.Full(
                spec.MaxHealth > 0 ? spec.MaxHealth : DEFAULT_HEALTH));
            entities.SetComponentData(agent, new Faction { Id = spec.Faction });

            entities.SetComponentEnabled<PathFollow>(agent, false);
            entities.SetComponentEnabled<ArrivedTag>(agent, false);
            entities.SetComponentEnabled<InsideBuilding>(agent, false);
            entities.SetComponentEnabled<InteriorClaim>(agent, false);
            entities.SetComponentEnabled<DoorUse>(agent, false);
            entities.SetComponentEnabled<OnBridge>(agent, false);
            entities.SetComponentEnabled<AssignedOrder>(agent, false);

            if (spec.Weapon.IsArmed)
            {
                Arm(entities, agent, spec.Weapon, spec.Leash);
            }
            else
            {
                entities.SetComponentEnabled<Weapon>(agent, false);
            }

            return agent;
        }

        /// <summary>
        /// Makes an agent a soldier (design §14 step 13).
        ///
        /// The whole of becoming one, and it is deliberately this small: the weapon is already on the
        /// archetype, so arming is a bit and a few numbers with **no structural change at all** - no entity
        /// created, no chunk moved, nothing deferred to a queue. That is why a training camp converts the
        /// worker who did the shift instead of producing a second body: a conversion is free and a birth is
        /// not, and a camp that both eats bread and takes a person off the labour pool is the honest cost.
        ///
        /// It drops the carry capacity on the way through, because "zero means it will never be given a haul"
        /// is already the rule <see cref="Carry"/> states, and a soldier queueing for a crate of flour is not
        /// a thing anyone has to write a rule against.
        /// </summary>
        /// <summary>
        /// Which cost model an agent walks on: its side, and what its weapon can knock down. An unarmed
        /// weapon is a civilian, which is the default and covers every agent in the game that is not a
        /// soldier.
        /// </summary>
        public static Traversal TraversalOf(in Weapon weapon, byte faction) =>
            new(weapon.IsArmed ? weapon.Breach : BreachClass.None, faction);

        public static void Arm(EntityManager entities, Entity agent, in Weapon weapon, float leash)
        {
            if (!entities.Exists(agent) || !entities.HasComponent<Weapon>(agent))
            {
                return;
            }

            entities.SetComponentData(agent, weapon);
            entities.SetComponentEnabled<Weapon>(agent, true);

            // Arming can change how the agent routes, not only what it can hurt: a soldier that can break a
            // wall down prices one as a way through (§14.4). Written here so the two can never disagree.
            PathFollow follow = entities.GetComponentData<PathFollow>(agent);
            follow.Traversal = TraversalOf(weapon, entities.GetComponentData<Faction>(agent).Id);
            entities.SetComponentData(agent, follow);

            // Its post is where it stood when it was armed - the camp it walked out of, or wherever the
            // spawn put it. The player moves it by moving the soldier (§14 step 13).
            entities.SetComponentData(agent, new Post
            {
                Home = entities.GetComponentData<AgentMove>(agent).Position,
                Leash = math.max(leash, DEFAULT_LEASH),
            });

            Carry carry = entities.GetComponentData<Carry>(agent);
            carry.Capacity = 0;
            carry.Amount = 0;
            carry.Item = ItemId.None;
            entities.SetComponentData(agent, carry);
        }
    }
}

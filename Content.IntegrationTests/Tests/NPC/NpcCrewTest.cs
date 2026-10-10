using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Gravity;
using Content.Server.GameTicking;
using Content.Server.NPC.Crew;
using Content.Server.NPC.HTN;
using Content.Shared.Atmos;
using Content.Shared.CCVar;
using Content.Shared.Gravity;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.NPC;

[TestFixture]
[TestOf(typeof(NpcCrewSystem))]
public sealed class NpcCrewTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    /// <summary>
    /// Spawns a crew member and checks that they get their job outfit and an AI that actually moves them around.
    /// </summary>
    [Test]
    public async Task SpawnedCrewHasJobGearAndWanders()
    {
        var pair = Pair;
        var server = pair.Server;
        var entMan = server.EntMan;
        var crewSystem = entMan.System<NpcCrewSystem>();
        var inventory = entMan.System<InventorySystem>();
        var mobState = entMan.System<MobStateSystem>();
        var xforms = entMan.System<SharedTransformSystem>();

        var map = await pair.CreateTestMap();

        // The test map is a single tile, give the NPC some floor to wander on.
        await server.WaitPost(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            for (var x = -5; x <= 5; x++)
            {
                for (var y = -5; y <= 5; y++)
                {
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
                }
            }
        });

        // Walking needs gravity, and without air the NPC would suffocate while we wait for it to wander.
        await server.WaitPost(() =>
        {
            var moles = new float[Atmospherics.AdjustedNumberOfGases];
            moles[(int) Gas.Oxygen] = 21.824779f;
            moles[(int) Gas.Nitrogen] = 82.10312f;
            var gravity = entMan.EnsureComponent<GravityComponent>(map.MapUid);
            entMan.System<GravitySystem>().EnableGravity(map.MapUid, gravity);
            entMan.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, new GasMixture(moles, Atmospherics.T20C));
        });
        await pair.RunTicksSync(5);

        EntityUid? crew = null;
        await server.WaitPost(() => crew = crewSystem.TrySpawnCrew("Chef", map.GridCoords));
        Assert.That(crew, Is.Not.Null);
        var uid = crew!.Value;

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<NpcCrewComponent>(uid).Job?.Id, Is.EqualTo("Chef"));
            Assert.That(entMan.HasComponent<HTNComponent>(uid));

            // Chefs get a PDA in their ID slot from their starting gear.
            Assert.That(inventory.TryGetSlotEntity(uid, "id", out _), "Chef NPC has no ID.");
        });

        var start = xforms.GetWorldPosition(uid);
        var moved = false;

        // The idle behavior waits a few seconds between wanders, so give it plenty of time.
        for (var i = 0; i < 10 && !moved; i++)
        {
            await pair.RunSeconds(5);
            await server.WaitPost(() => moved = (xforms.GetWorldPosition(uid) - start).Length() > 0.5f);
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(mobState.IsAlive(uid));
            Assert.That(moved, "NPC crew member never moved from where they spawned.");
        });

        await server.WaitPost(() => entMan.System<SharedMapSystem>().DeleteMap(map.MapId));
    }
}

[TestFixture]
[TestOf(typeof(NpcCrewSystem))]
public sealed class NpcCrewRoundstartTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Dirty = true,
        DummyTicker = false,
        InLobby = true,
        Map = PoolManager.TestStation,
    };

    /// <summary>
    /// Checks that the configured number of NPC crew show up on the station once a round starts.
    /// </summary>
    [Test]
    public async Task CrewSpawnsAtRoundStart()
    {
        var pair = Pair;
        var server = pair.Server;
        var ticker = server.System<ServerGameTicker>();

        server.CfgMan.SetCVar(CCVars.NPCCrewRoundstartCount, 3);

        await server.WaitPost(() => ticker.StartRound());
        await pair.RunSeconds(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.Count<NpcCrewComponent>(), Is.EqualTo(3));
            Assert.That(server.EntMan.Count<HTNComponent>(), Is.GreaterThanOrEqualTo(3));
        });
    }
}

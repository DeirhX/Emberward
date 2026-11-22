namespace Emberward.Tests;

using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Emberward.Core;
using Emberward.Core.Input;
using Emberward.Core.Types;
using Emberward.Desktop.Net;
using Emberward.Relay;

public partial class RelayOverSocketsTests
{
    /// <summary>
    /// The tests that play a match through the relay. The relay's clock is off (<see cref="RelayHub.RunsClocks"/>):
    /// the test closes every tick itself, in lockstep with the clients (<see cref="PlayInLockstep"/>), so the match
    /// they play is the same on every run, however slow the machine. They still wait on real sockets, so they run on
    /// their own beside the other socket tests (<see cref="TimedTestGroup"/>).
    /// </summary>
    [Collection(TimedTestGroup.Name)]
    [Trait("Category", "Slow")]
    public class MatchPlayTests
    {
        /// <summary>
        /// How long one message may take to go round between a client and the relay. Nothing waits for a rate or a
        /// timer, so the limit only ends a test whose connection has stopped; it is long so that a loaded machine
        /// never reaches it.
        /// </summary>
        private static readonly TimeSpan DeliveryLimit = TimeSpan.FromSeconds(30);

        private static async Task<(WebApplication App, Uri Play, RelayHub Hub)> StartUnclockedRelayAsync()
        {
            var (app, play) = await StartRelayAsync();
            var hub = app.Services.GetRequiredService<RelayHub>();
            hub.RunsClocks = false;
            return (app, play, hub);
        }

        /// <summary>
        /// Pumps the connections until <paramref name="condition"/> holds. Fails after <see cref="DeliveryLimit"/>,
        /// saying what it waited for and where the match stood (<paramref name="where"/>).
        /// </summary>
        private static void Wait(string waitingFor, Func<bool> condition, Func<string> where, params RelayConnection[] pump)
        {
            var limit = Stopwatch.StartNew();
            while (!condition())
            {
                Assert.True(limit.Elapsed < DeliveryLimit,
                    $"Timed out after {DeliveryLimit.TotalSeconds:0} s waiting for {waitingFor}: {where()}");
                Thread.Sleep(5);
                foreach (var connection in pump)
                    connection.Pump();
            }
        }

        /// <summary>
        /// Plays the match on to <paramref name="until"/> ticks, one tick at a time: a tick is closed only once every
        /// client has stepped the ones before it (<paramref name="steppedTo"/>, which steps them) and every player still
        /// connected has sent the relay its input for it. Each bundle then holds exactly one input from each player,
        /// the same on every run, and no client can step past <paramref name="until"/>, because no later tick exists.
        /// </summary>
        private static void PlayInLockstep(MatchRoom room, int until, Func<int, bool> steppedTo, Func<string> where,
            params RelayConnection[] pump)
        {
            string At() => $"{where()}; the relay has closed {room.TicksClosed} ticks";
            for (int tick = room.TicksClosed; tick < until; tick++)
            {
                // A client sends an input after each step, so the first tick has none to wait for.
                if (tick > 0)
                {
                    int closed = tick;
                    Wait($"every client at tick {closed} with its input for it at the relay",
                        () => steppedTo(closed) && room.HasInputFromEveryPlayer, At, pump);
                }
                room.CloseTicksUpTo(tick + 1);
            }
            Wait($"every client at tick {until}", () => steppedTo(until), At, pump);
        }

        [Fact]
        public async Task TwoClients_PlayTheSameMatchThroughTheRelay()
        {
            var (app, play, hub) = await StartUnclockedRelayAsync();
            await using var _ = app;
            // The second client proposes another seed; the room's settings, from the first, win.
            using var a = NetLaunch.Join(Options(play, "Ann"), Config(seed: 42));
            using var b = NetLaunch.Join(Options(play, "Bob"), Config(seed: 77));
            Assert.Equal((0, 1), (a.Slot, b.Slot));
            Assert.Equal(42, b.Config.Seed);

            var connections = new[] { a, b };
            var worlds = connections.Select(c => TestHelpers.CreateSeededWorld(MapSize, c.Config.Seed)).ToArray();
            var sessions = connections.Select((c, i) => new NetSession(c, worlds[i])).ToArray();
            string Where() => $"Ann at tick {worlds[0].AdvanceCount} (started {a.Started}), Bob at {worlds[1].AdvanceCount} (started {b.Started})";
            Wait("both players in the roster", () => a.Roster.Count(n => n.Length > 0) == 2, Where, a);
            Assert.Equal("Waiting for players: 2 of 2 here.", a.Status);
            foreach (var (c, i) in connections.Select((c, i) => (c, i)))
                c.SendReady(WorldStateHash.ComputeReady(worlds[i]));
            Wait("the match to start", () => a.Started && b.Started, Where, a, b);
            Assert.Null(a.Status);
            var room = hub.FindRoom(RoomName)!;

            // Each client drives its tank and steps whatever ticks have arrived, like the game loop does.
            bool SteppedTo(int tick)
            {
                for (int i = 0; i < sessions.Length; i++)
                {
                    sessions[i].Receive();
                    while (sessions[i].TryStep())
                        sessions[i].SendInput(new ControllerOutput
                        {
                            MoveSpeed = new Offset(i == 0 ? 1 : -1, worlds[i].AdvanceCount / 10 % 2),
                            ShootPrimary = worlds[i].AdvanceCount % 7 == 0,
                        });
                }
                return worlds.All(w => w.AdvanceCount == tick);
            }

            const int Ticks = 60;
            PlayInLockstep(room, Ticks, SteppedTo, Where, a, b);

            Assert.Equal(WorldStateHash.Compute(worlds[0]), WorldStateHash.Compute(worlds[1]));
            // The inputs went round: the tanks are not where an idle match would have left them.
            var idle = TestHelpers.CreateSeededWorld(MapSize, 42);
            TestSimulation.AdvanceIdle(idle, Ticks);
            Assert.NotEqual(WorldStateHash.Compute(idle), WorldStateHash.Compute(worlds[0]));
            Assert.Null(a.Failure);
        }

        [Fact]
        public async Task PlayerWhoDropsOut_RejoinsAndCatchesUpToTheSameWorld()
        {
            var (app, play, hub) = await StartUnclockedRelayAsync();
            await using var _ = app;
            // Two players and two bots: the bots must run alike on the client that stayed and the one that came back.
            var config = NetMatch.ProposeConfig(42, FourPlayerMap, Core.LevelGen.LevelGenMode.Deterministic, players: 4);
            using var a = NetLaunch.Join(Options(play, "Ann"), config);
            var b = NetLaunch.Join(Options(play, "Bob"), config);
            var worldA = CreateFourTankWorld(42);
            var worldB = CreateFourTankWorld(42);
            var sessionA = new NetSession(a, worldA);
            var sessionB = new NetSession(b, worldB);
            a.SendReady(WorldStateHash.ComputeReady(worldA));
            b.SendReady(WorldStateHash.ComputeReady(worldB));
            a.RequestStart();
            string BeforeDrop() => $"Ann at tick {worldA.AdvanceCount} (started {a.Started}), Bob at {worldB.AdvanceCount} (started {b.Started})";
            Wait("the match to start", () => a.Started && b.Started, BeforeDrop, a, b);
            var room = hub.FindRoom(RoomName)!;

            // Steps what has arrived; the bots are chosen when the relay says the match has started. A client sends an
            // input after each step from tick `liveFrom` on; the steps before it replay ticks the match has already
            // played, whose inputs are long past.
            static void Play(NetSession session, RelayConnection connection, World world, int liveFrom = 0)
            {
                session.Receive();
                if (connection.Started && world.AdvanceCount == 0 && session.HumanMask != connection.HumanMask)
                    session.StartSeats(connection.Config.Seed, connection.HumanMask);
                while (session.TryStep())
                    if (world.AdvanceCount >= liveFrom)
                        session.SendInput(new ControllerOutput
                        {
                            MoveSpeed = new Offset(connection.Slot == 0 ? 1 : -1, world.AdvanceCount / 15 % 2),
                            ShootPrimary = true,
                        });
            }

            const int WarmUp = 20;
            PlayInLockstep(room, WarmUp, tick =>
            {
                Play(sessionA, a, worldA);
                Play(sessionB, b, worldB);
                return worldA.AdvanceCount == tick && worldB.AdvanceCount == tick;
            }, BeforeDrop, a, b);

            // Bob drops out. Once the relay has seen him go (the roster says so), Ann plays on alone to the tick Bob
            // comes back at.
            b.Dispose();
            string Alone() => $"Ann at tick {worldA.AdvanceCount}, {a.PlayersHere} players here";
            Wait("the relay to see Bob go", () => a.PlayersHere == 1, Alone, a);
            const int Rejoin = 60;
            PlayInLockstep(room, Rejoin, tick =>
            {
                Play(sessionA, a, worldA);
                return worldA.AdvanceCount == tick;
            }, Alone, a);

            // Bob starts the game again: a fresh world, the same room and name. He gets every tick so far from the
            // relay's log, and no tick is closed until he has stepped them all. While he replays them he sends nothing,
            // so when he is live the relay holds exactly one input from him, for the next tick, and the ticks after the
            // seam are as fixed as the ones before it.
            using var back = NetLaunch.Join(Options(play, "Bob"), config with { Seed = 1 });
            var worldBack = CreateFourTankWorld(back.Config.Seed);
            var sessionBack = new NetSession(back, worldBack);
            string AfterReturn() =>
                $"Ann at tick {worldA.AdvanceCount}, the returning Bob at {worldBack.AdvanceCount} (rejoined {back.Rejoined})";
            Wait($"the returning Bob to replay the {Rejoin} ticks so far", () =>
            {
                Play(sessionBack, back, worldBack, liveFrom: Rejoin);
                return worldBack.AdvanceCount == Rejoin;
            }, AfterReturn, a, back);

            Assert.True(back.Rejoined, "the relay took Bob back into his seat, not as a newcomer");
            Assert.Equal(1, back.Slot);
            Assert.Equal(0b0011, back.HumanMask);
            Assert.Equal(Rejoin, worldA.AdvanceCount);
            // The rejoining client's own world, stepped from the relay's log, is the one the client that stayed has.
            Assert.Equal(WorldStateHash.ComputeSync(worldA), WorldStateHash.ComputeSync(worldBack));

            // And the two go on together from there: ticks with both players' live inputs keep the worlds the same.
            const int Compared = Rejoin + 12;
            PlayInLockstep(room, Compared, tick =>
            {
                Play(sessionA, a, worldA);
                Play(sessionBack, back, worldBack);
                return worldA.AdvanceCount == tick && worldBack.AdvanceCount == tick;
            }, AfterReturn, a, back);
            Assert.Equal(WorldStateHash.ComputeSync(worldA), WorldStateHash.ComputeSync(worldBack));

            // The match is not an idle one: the bots and the players moved their tanks. A bot may be back where it
            // started by now (it goes home to cool after torching its way out through rock), so for a bot having
            // driven out of its base counts too.
            var untouched = CreateFourTankWorld(42);
            for (int slot = 0; slot < 4; slot++)
            {
                var tank = worldBack.TankList.Tanks[slot];
                bool isBot = (back.HumanMask & 1 << slot) == 0;
                Assert.True(tank.Position != untouched.TankList.Tanks[slot].Position || (isBot && tank.HasLeftBase),
                    $"the tank in slot {slot} never moved");
            }
            // The first latency sample comes with the first answered ping, which a client sends as soon as it pumps.
            Wait("a latency sample", () => back.LatencyMs != null, AfterReturn, back);
        }
    }
}

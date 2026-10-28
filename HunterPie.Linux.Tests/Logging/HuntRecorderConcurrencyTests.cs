using HunterPie.Core.Domain.Process.Entity;
using HunterPie.Core.Game;
using HunterPie.Core.Game.Entity.Enemy;
using HunterPie.Core.Game.Entity.Game;
using HunterPie.Core.Game.Entity.Player;
using HunterPie.Core.Game.Enums;
using HunterPie.Core.Scan.Service;
using HunterPie.Integrations.Datasources.Common.Entity.Enemy;
using HunterPie.Linux.Logging;
using NSubstitute;

namespace HunterPie.Linux.Tests.Logging;

[TestClass]
public class HuntRecorderConcurrencyTests
{
    private string _directory = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "hunterpie-recorder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, recursive: true);

    /// <summary>A monster whose events go through the same SmartEvent locking as the real ones</summary>
    private sealed class FakeMonster(IGameProcess process, IScanService scanService) : CommonMonster(process, scanService)
    {
        public override string Name => "Fake Monster";
        public override int Id { get; protected set; } = 1;
        public override float Health { get; protected set; } = 100;
        public override float MaxHealth { get; protected set; } = 100;
        public override float Stamina { get; protected set; }
        public override float MaxStamina { get; protected set; }
        public override float CaptureThreshold { get; protected set; }
        public override bool IsEnraged { get; protected set; }
        public override Target Target { get; protected set; }
        public override Target ManualTarget { get; protected set; }
        public override IReadOnlyCollection<IMonsterPart> Parts { get; } = [];
        public override IReadOnlyCollection<IMonsterAilment> Ailments { get; } = [];
        public override IMonsterAilment Enrage { get; } = Substitute.For<IMonsterAilment>();
        public override Crown Crown { get; protected set; }
        public override Element[] Weaknesses { get; } = [];
        public override string[] Types { get; } = [];
        public override VariantType Variant { get; protected set; }

        public void Kill() => _onDeath.Invoke(this, EventArgs.Empty);
    }

    // Before the fix, a monster dying while it despawned made two scan threads wait on each other forever: the death
    // event held its own lock while its handler waited for the recorder, and the despawn handler held the recorder's
    // lock while it unsubscribed from that same event.
    [TestMethod]
    public void AMonsterDyingWhileItDespawnsDoesNotDeadlock()
    {
        IGame game = Substitute.For<IGame>();
        IPlayer player = Substitute.For<IPlayer>();
        game.Player.Returns(player);

        var context = new Context(game, Substitute.For<IGameProcess>());
        var sink = new LogSink(_directory, DateTime.Now);
        var recorder = new HuntRecorder(context, sink);

        const int Rounds = 300;

        for (int round = 0; round < Rounds; round++)
        {
            var monster = new FakeMonster(Substitute.For<IGameProcess>(), Substitute.For<IScanService>());

            game.OnMonsterSpawn += Raise.Event<EventHandler<IMonster>>(game, monster);

            using var barrier = new Barrier(2);

            Task death = Task.Run(() =>
            {
                barrier.SignalAndWait();
                monster.Kill();
            });

            Task despawn = Task.Run(() =>
            {
                barrier.SignalAndWait();
                game.OnMonsterDespawn += Raise.Event<EventHandler<IMonster>>(game, monster);
            });

            Assert.IsTrue(
                Task.WaitAll([death, despawn], TimeSpan.FromSeconds(10)),
                $"The death and despawn handlers deadlocked in round {round}");
        }

        // Shutting down must not hang either
        Task disposing = Task.Run(() =>
        {
            recorder.Dispose();
            sink.Dispose();
        });

        Assert.IsTrue(disposing.Wait(TimeSpan.FromSeconds(10)), "Disposing the recorder hung");
    }
}

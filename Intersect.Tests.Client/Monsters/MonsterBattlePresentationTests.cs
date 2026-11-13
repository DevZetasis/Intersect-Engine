using Intersect.Framework.Core.GameObjects.Animations;
using Intersect.Client.Framework.Core.Sounds;
using Intersect.Framework.Core.GameObjects.Monsters;
using System.Reflection;
using System.Runtime.CompilerServices;
using Intersect.Client.Framework.Graphics;
using Intersect.Client.Interface.Game.Monsters;
using Intersect.Network.Packets.Server;
using NUnit.Framework;

namespace Intersect.Tests.Client.Monsters;

[TestFixture]
public class MonsterBattlePresentationTests
{
    private static readonly Guid Player = Guid.NewGuid(), Opponent = Guid.NewGuid(), Battle = Guid.NewGuid(), Owner = Guid.NewGuid();
    private static MonsterBattleStatePacket State(long sequence, Guid revision, Guid previous = default, MonsterBattlePresentationEvent[]? events = null,
        long playerHP = 100, long opponentHP = 100, MonsterBattleOutcome outcome = MonsterBattleOutcome.Active) => new()
    {
        PlayerId = Owner, BattleId = Battle, Sequence = sequence, Turn = sequence == 1 ? 1 : 2, Revision = revision, PreviousRevision = previous, Outcome = outcome,
        Player = new() { Id = Player, HP = playerHP, MaximumHP = 100 }, Opponent = new() { Id = Opponent, HP = opponentHP, MaximumHP = 100 }, Events = events ?? [],
    };
    private static MonsterBattlePresentationEvent Event(MonsterBattleEventKind kind, int order, bool player, long before = 0, long after = 0) => new()
    { Kind = kind, Order = order, ActorId = player ? Player : Opponent, TargetId = player ? Opponent : Player, HPBefore = before, HPAfter = after, Text = kind.ToString() + order };
    private static (MonsterBattlePlayback Playback, MonsterBattleStatePacket Start) Ready()
    {
        var model = new MonsterBattlePlayback(); var start = State(1, Guid.NewGuid()); model.Accept(start); return (model, start);
    }
    [Test]
    public void OrderedFactsDriveOpponentFirstAndSequentialHpEndpoints()
    {
        var (model, start) = Ready(); model.BeginAwaiting();
        model.Accept(State(2, Guid.NewGuid(), start.Revision, [Event(MonsterBattleEventKind.MoveUsed, 0, false), Event(MonsterBattleEventKind.DamageApplied, 1, false, 100, 60),
            Event(MonsterBattleEventKind.MoveUsed, 2, true), Event(MonsterBattleEventKind.DamageApplied, 3, true, 100, 70)], 60, 70));
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.PlayingSequence));
        Assert.That(model.CurrentEvent!.ActorId, Is.EqualTo(Opponent)); Assert.That(model.PlayerHP, Is.EqualTo(100));
        Assert.That(model.BeginAwaiting(), Is.False);
        model.Advance(); model.Tick(.2); Assert.That(model.PlayerHP, Is.InRange(60.01, 99.99)); Assert.That(model.OpponentHP, Is.EqualTo(100));
        model.Tick(.4); Assert.That(model.PlayerHP, Is.EqualTo(60)); Assert.That(model.CurrentEvent!.ActorId, Is.EqualTo(Player));
        model.Tick(10); Assert.That(model.PlayerHP, Is.EqualTo(60)); Assert.That(model.OpponentHP, Is.EqualTo(70));
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.CommandSelection));
    }
    [Test]
    public void PresentationCanFollowPlayerPriorityWithoutInferringOrderFromText()
    {
        var (model, start) = Ready(); var e = Event(MonsterBattleEventKind.MoveUsed, 0, true); e = e with { Text = "Arbitrary localized text" };
        model.Accept(State(2, Guid.NewGuid(), start.Revision, [e]));
        Assert.That(model.CurrentEvent!.ActorId, Is.EqualTo(Player)); Assert.That(model.Text, Is.EqualTo(e.Text));
    }
    [Test]
    public void MissesAndImmunityLeaveHpUntouchedThroughoutPlayback()
    {
        var (model, start) = Ready(); model.Accept(State(2, Guid.NewGuid(), start.Revision,
            [Event(MonsterBattleEventKind.MoveUsed, 0, true), Event(MonsterBattleEventKind.MoveMissed, 1, true),
             Event(MonsterBattleEventKind.MoveUsed, 2, false), Event(MonsterBattleEventKind.DamageApplied, 3, false, 100, 100)]));
        for (var i = 0; i < 50; i++) { model.Tick(.1); Assert.That(model.PlayerHP, Is.EqualTo(100)); Assert.That(model.OpponentHP, Is.EqualTo(100)); }
    }
    [Test]
    public void FaintFinishesBeforeResultAndSkipCompletesExactlyAtZero()
    {
        var (model, start) = Ready(); var end = State(2, Guid.NewGuid(), start.Revision,
            [Event(MonsterBattleEventKind.MoveUsed, 0, true), Event(MonsterBattleEventKind.DamageApplied, 1, true, 100, 0),
             Event(MonsterBattleEventKind.MonsterFainted, 2, true), Event(MonsterBattleEventKind.BattleFinished, 3, true)], opponentHP: 0, outcome: MonsterBattleOutcome.Victory);
        model.Accept(end); Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.PlayingSequence));
        model.Tick(.9); Assert.That(model.OpponentHP, Is.EqualTo(0)); Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.PlayingSequence));
        model.Skip(); Assert.That(model.OpponentFainted, Is.True); Assert.That(model.OpponentHP, Is.EqualTo(0));
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.ShowingResult));
    }
    [Test]
    public void DuplicateStaleAndOutOfOrderBatchesNeverReplay()
    {
        var (model, start) = Ready(); var next = State(2, Guid.NewGuid(), start.Revision, [Event(MonsterBattleEventKind.MoveUsed, 0, true)]);
        model.Accept(next); model.Skip(); Assert.That(model.Accept(next), Is.False); Assert.That(model.Accept(start), Is.False);
        var duplicate = State(3, next.Revision, start.Revision, next.Events); Assert.That(model.Accept(duplicate), Is.False);
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.CommandSelection));
        Assert.That(model.Accept(State(2, Guid.NewGuid())), Is.False);
    }
    [Test]
    public void MissingRevisionChainAndInvalidEventsUseSnapshotCorrection()
    {
        var (model, _) = Ready(); model.Accept(State(2, Guid.NewGuid(), Guid.NewGuid(), [Event(MonsterBattleEventKind.MoveUsed, 0, true)], 50, 25));
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.CommandSelection)); Assert.That(model.PlayerHP, Is.EqualTo(50));
        var previous = model.State!; model.Accept(State(3, Guid.NewGuid(), previous.Revision, [Event(MonsterBattleEventKind.DamageApplied, 4, true, 100, -1)], 50, 0));
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.CommandSelection)); Assert.That(model.OpponentHP, Is.EqualTo(0));
    }
    [Test]
    public void RejectionUnlocksAwaitingWithoutPlayingOldTurnAgain()
    {
        var (model, start) = Ready(); Assert.That(model.BeginAwaiting(), Is.True); Assert.That(model.BeginAwaiting(), Is.False);
        var rejection = State(2, start.Revision); rejection.IsCorrection = true; model.Accept(rejection);
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.CommandSelection)); Assert.That(model.CurrentEvent, Is.Null);
    }
    [Test]
    public void CancellationInterruptsPlaybackAndDisconnectResetsEverything()
    {
        var (model, start) = Ready(); var result = State(2, Guid.NewGuid(), start.Revision, [Event(MonsterBattleEventKind.MoveUsed, 0, true)]); model.Accept(result);
        model.Accept(State(3, Guid.NewGuid(), result.Revision, outcome: MonsterBattleOutcome.Cancelled));
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.ShowingResult)); Assert.That(model.CurrentEvent, Is.Null);
        model.Reset(); Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.Waiting)); Assert.That(model.State, Is.Null); Assert.That(model.PlayerHP, Is.Zero);
    }
    [Test]
    public void AllTerminalOutcomesStayTerminalAndClockInputsAreValidated()
    {
        foreach (var outcome in new[] { MonsterBattleOutcome.Victory, MonsterBattleOutcome.Defeat, MonsterBattleOutcome.Draw, MonsterBattleOutcome.Forfeit, MonsterBattleOutcome.Cancelled })
        {
            var model = new MonsterBattlePlayback(); model.Accept(State(1, Guid.NewGuid(), outcome: outcome));
            Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.ShowingResult)); Assert.That(model.BeginAwaiting(), Is.False);
        }
        var (active, _) = Ready(); Assert.Throws<ArgumentOutOfRangeException>(() => active.Tick(double.NaN)); Assert.Throws<ArgumentOutOfRangeException>(() => active.Tick(-1));
    }
    [Test]
    public void DedicatedArtFallbacksAndMissingAssetsAreSafe()
    {
        using var options = new OptionScope(); var texture = DispatchProxy.Create<IGameTexture, MonsterRosterLayoutTests.TextureStub>();
        var actor = new MonsterBattleActorView { FrontBattleSprite = "front", BackBattleSprite = "back", Sprite = "sheet" };
        IGameTexture? Load(string name) => name is "front" or "sheet" ? texture : null;
        var front = MonsterBattleArt.Resolve(actor, false, Load); var back = MonsterBattleArt.Resolve(actor, true, Load);
        Assert.That(front.Fallback, Is.False); Assert.That(front.Frame.Width, Is.EqualTo(texture.Width));
        Assert.That(back.Fallback, Is.True); Assert.That(back.Frame.Width, Is.EqualTo(texture.Width / Options.Instance.Sprites.NormalFrames));
        Assert.That(MonsterBattleArt.Resolve(actor, true, _ => throw new IOException()).Texture, Is.Null);
        Assert.That(MonsterBattleArt.Resolve(new(), false, _ => null).Texture, Is.Null);
    }
    [Test]
    public void BackgroundSelectionFallsBackToDefaultThenProcedural()
    {
        using var options = new OptionScope();
        var texture = DispatchProxy.Create<IGameTexture, MonsterRosterLayoutTests.TextureStub>();
        Assert.That(MonsterBattleArt.Background("custom", _ => texture), Is.SameAs(texture));
        Assert.That(MonsterBattleArt.Background("missing", name => name == "monster-battle-default.png" ? texture : null), Is.SameAs(texture));
        Assert.That(MonsterBattleArt.Background("missing", _ => null), Is.Null);
    }
    [Test]
    public void SpriteGeometryFitsVariedAspectRatiosAndDistinctSides()
    {
        foreach (var (w, h) in new[] { (600, 220), (900, 330), (1200, 460) })
        foreach (var (fw, fh) in new[] { (16, 32), (96, 96), (500, 100), (512, 1024) })
        foreach (var own in new[] { true, false })
        {
            var bounds = MonsterBattleArt.SpriteBounds(w, h, own, fw, fh);
            Assert.That(bounds.X, Is.GreaterThanOrEqualTo(0)); Assert.That(bounds.Y, Is.GreaterThanOrEqualTo(0));
            Assert.That(bounds.X + bounds.Width, Is.LessThanOrEqualTo(w)); Assert.That(bounds.Y + bounds.Height, Is.LessThanOrEqualTo(h));
            Assert.That(own ? bounds.X < w / 2 : bounds.X > w / 2, Is.True);
            Assert.That(Math.Abs(bounds.Width / (double)fw - bounds.Height / (double)fh), Is.LessThan(.07));
        }
    }
    private sealed class OptionScope : IDisposable
    {
        private readonly PropertyInfo _property = typeof(Options).GetProperty(nameof(Options.Instance))!;
        private readonly object? _previous;
        public OptionScope() { _previous = _property.GetValue(null); _property.SetValue(null, new Options()); }
        public void Dispose() => _property.SetValue(null, _previous);
    }


    [Test]
    public void SwitchKeepsOutgoingActorUntilSendOutAndDamagesIncomingActor()
    {
        var (model, start) = Ready();
        var incoming = new MonsterBattleActorView { Id = Guid.NewGuid(), Name = "Reserve", HP = 34, MaximumHP = 45, BackBattleSprite = "reserve.png" };
        var state = State(2, Guid.NewGuid(), start.Revision);
        state.Player = new() { Id = incoming.Id, Name = incoming.Name, HP = 20, MaximumHP = 45, BackBattleSprite = incoming.BackBattleSprite };
        state.Party = [new() { Slot = 0, Actor = start.Player }, new() { Slot = 1, Actor = state.Player }];
        state.Events = [
            new() { Kind = MonsterBattleEventKind.Withdraw, Order = 0, ActorId = Player, TargetId = Player },
            new() { Kind = MonsterBattleEventKind.SendOut, Order = 1, ActorId = incoming.Id, TargetId = incoming.Id, Incoming = incoming },
            new() { Kind = MonsterBattleEventKind.MoveUsed, Order = 2, ActorId = Opponent, TargetId = incoming.Id },
            new() { Kind = MonsterBattleEventKind.DamageApplied, Order = 3, ActorId = Opponent, TargetId = incoming.Id, HPBefore = 34, HPAfter = 20 },
        ];
        Assert.That(model.Accept(state), Is.True);
        Assert.That(model.DisplayedPlayer!.Id, Is.EqualTo(Player));
        Assert.That(model.PlayerHP, Is.EqualTo(100));
        model.Advance();
        Assert.That(model.DisplayedPlayer!.Id, Is.EqualTo(incoming.Id));
        Assert.That(model.DisplayedPlayer.BackBattleSprite, Is.EqualTo("reserve.png"));
        Assert.That(model.PlayerHP, Is.EqualTo(34));
        model.Tick(4);
        Assert.That(model.PlayerHP, Is.EqualTo(20));
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.CommandSelection));
    }

    [Test]
    public void FaintPlaybackCompletesBeforeReplacementAndSendOutClearsFaintedState()
    {
        var (model, start) = Ready();
        var faint = State(2, Guid.NewGuid(), start.Revision,
            [Event(MonsterBattleEventKind.DamageApplied, 0, false, 100, 0),
                Event(MonsterBattleEventKind.MonsterFainted, 1, false)], playerHP: 0);
        faint.Phase = MonsterBattlePhase.Replacement;
        model.Accept(faint);
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.PlayingSequence));
        model.Advance();
        Assert.That(model.CurrentEvent!.Kind, Is.EqualTo(MonsterBattleEventKind.MonsterFainted));
        model.Advance();
        Assert.That(model.PlayerFainted, Is.True);
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.Replacement));
        Assert.That(model.BeginAwaiting(), Is.True);

        var state = State(3, Guid.NewGuid(), faint.Revision);
        state.Player = new() { Id = Guid.NewGuid(), HP = 18, MaximumHP = 20 };
        state.Party = [new() { Slot = 1, Actor = state.Player }];
        state.Events = [new() { Kind = MonsterBattleEventKind.SendOut, ActorId = state.Player.Id, TargetId = state.Player.Id, Incoming = state.Player }];
        model.Accept(state);
        Assert.That(model.PlayerFainted, Is.False);
        Assert.That(model.PlayerHP, Is.EqualTo(18));
        model.Skip();
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.CommandSelection));
    }

    [Test]
    public void AuthoredAnchorsFollowActorAndTargetInBothDirections()
    {
        foreach (var playerAttacks in new[] { true, false })
        {
            var e = Event(MonsterBattleEventKind.MoveUsed, 0, playerAttacks);
            Assert.That(MonsterBattleAnimation.Anchors(e), Is.EqualTo((e.TargetId, Guid.Empty)));
            e.AnimationPosition = BattleMoveAnimationPosition.Attacker;
            Assert.That(MonsterBattleAnimation.Anchors(e), Is.EqualTo((e.ActorId, Guid.Empty)));
            e.AnimationPosition = BattleMoveAnimationPosition.Both;
            Assert.That(MonsterBattleAnimation.Anchors(e), Is.EqualTo((e.ActorId, e.TargetId)));
            e.TargetId = e.ActorId;
            Assert.That(MonsterBattleAnimation.Anchors(e), Is.EqualTo((e.ActorId, Guid.Empty)));
        }
    }

    [Test]
    public void SoundStartsOnceOnMoveEventAndSkipStopsCompletedAndActiveAudio()
    {
        var (model, start) = Ready();
        var animation = new AnimationDescriptor(Guid.NewGuid()) { Sound = "practice.wav", CompleteSound = true };
        var sounds = new List<SoundStub>();
        var adapter = new MonsterBattleAnimation([], [], id => id == animation.Id ? animation : null,
            _ => throw new IOException("Missing texture"), name =>
            {
                Assert.That(name, Is.EqualTo("practice.wav"));
                var sound = new SoundStub();
                sounds.Add(sound);
                return sound;
            });
        model.EventStarted = adapter.Begin;
        model.EventEnded = adapter.End;
        model.Interrupted = adapter.Cancel;
        model.EffectDuration = entry => entry.Kind == MonsterBattleEventKind.MoveUsed ? adapter.Duration : .5;
        var first = Event(MonsterBattleEventKind.MoveUsed, 0, true) with { AnimationId = animation.Id };
        var second = Event(MonsterBattleEventKind.MoveUsed, 2, false) with { AnimationId = animation.Id };
        var state = State(2, Guid.NewGuid(), start.Revision, [first, Event(MonsterBattleEventKind.MoveMissed, 1, true),
            second, Event(MonsterBattleEventKind.DamageApplied, 3, false, 100, 100)]);
        model.Accept(state);
        Assert.That(sounds.Count, Is.EqualTo(1));
        Assert.That(model.Accept(state), Is.False);
        Assert.That(sounds.Count, Is.EqualTo(1));
        model.Tick(.35);
        Assert.That(sounds[0].Loaded, Is.True);
        model.Advance();
        Assert.That(sounds.Count, Is.EqualTo(2));
        Assert.That(model.PlayerHP, Is.EqualTo(100));
        Assert.That(model.OpponentHP, Is.EqualTo(100));
        model.Skip();
        Assert.That(sounds.All(sound => !sound.Loaded), Is.True);
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.CommandSelection));

        var missing = first with { AnimationId = Guid.NewGuid() };
        adapter.Begin(missing);
        Assert.That(adapter.Duration, Is.EqualTo(.35));
        Assert.That(adapter.HasAuthoredImpact(missing), Is.False);
        Assert.That(sounds.Count, Is.EqualTo(2));
    }

    [Test]
    public void LongAuthoredLoopsAreCappedAndMissingLayersFallBack()
    {
        using var options = new OptionScope();
        var texture = DispatchProxy.Create<IGameTexture, MonsterRosterLayoutTests.TextureStub>();
        var animation = new AnimationDescriptor(Guid.NewGuid())
        {
            Lower = new AnimationLayer { Sprite = "test.png", XFrames = 2, YFrames = 2, FrameCount = 999, FrameSpeed = 999, LoopCount = int.MaxValue },
            Upper = new AnimationLayer { Sprite = "test.png", XFrames = 0 },
        };
        var entry = Event(MonsterBattleEventKind.MoveUsed, 0, true) with { AnimationId = animation.Id };
        var adapter = new MonsterBattleAnimation([], [], _ => animation, _ => texture, _ => null);
        adapter.Begin(entry);
        Assert.That(adapter.Duration, Is.EqualTo(4));
        Assert.That(adapter.HasAuthoredImpact(entry), Is.True);
        adapter.Cancel();
        Assert.That(adapter.HasAuthoredImpact(entry), Is.False);
        animation.Lower.FrameSpeed = 0;
        adapter.Begin(entry);
        Assert.That(adapter.Duration, Is.EqualTo(.35));
        Assert.That(adapter.HasAuthoredImpact(entry), Is.False);
    }

    private sealed class SoundStub : ISound
    {
        public bool Loaded { get; set; } = true;
        public bool Loop { get; set; }
        public void Stop() => Loaded = false;
        public bool Update() => Loaded;
    }

    [Test]
    public void StageAndHealingFactsPlayInOrderAndNextAndSkipSnapCorrectly()
    {
        var model = new MonsterBattlePlayback();
        var start = State(1, Guid.NewGuid(), playerHP: 45);
        model.Accept(start);
        var used = Event(MonsterBattleEventKind.MoveUsed, 0, true) with { TargetId = Player, AnimationId = Guid.NewGuid() };
        var stage = Event(MonsterBattleEventKind.StatStageChanged, 1, true) with
        {
            TargetId = Player, Stat = MonsterStat.Defense, StageAfter = 2,
        };
        var heal = Event(MonsterBattleEventKind.HPRecovered, 2, true, 45, 75) with { TargetId = Player };
        var state = State(2, Guid.NewGuid(), start.Revision, [used, stage, heal], playerHP: 75);
        state.Player.StatStages = [0, 2, 0, 0, 0];
        var starts = new List<MonsterBattleEventKind>();
        model.EventStarted = entry => starts.Add(entry.Kind);
        Assert.That(model.Accept(state), Is.True);
        Assert.That(model.PlayerStages.Defense, Is.Zero);
        Assert.That(model.PlayerHP, Is.EqualTo(45));
        model.Advance();
        Assert.That(model.PlayerStages.Defense, Is.EqualTo(2));
        model.Advance();
        model.Tick(.25);
        Assert.That(model.PlayerHP, Is.EqualTo(60).Within(.001));
        Assert.That(model.Accept(state), Is.False);
        model.Advance();
        Assert.That(model.PlayerHP, Is.EqualTo(75));
        Assert.That(starts, Is.EqualTo(new[] { MonsterBattleEventKind.MoveUsed, MonsterBattleEventKind.StatStageChanged, MonsterBattleEventKind.HPRecovered }));

        var second = State(3, Guid.NewGuid(), state.Revision, [used, heal with { Order = 1, HPBefore = 75, HPAfter = 90 }], playerHP: 90);
        second.Player.StatStages = state.Player.StatStages;
        model.Accept(second);
        model.Skip();
        Assert.That(model.PlayerHP, Is.EqualTo(90));
        Assert.That(model.PlayerStages.Defense, Is.EqualTo(2));
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.CommandSelection));
    }
}

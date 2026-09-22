using System.Numerics;
using System.Text;
using HKSK.Behavior;
using HKSK.Cache;
using HKSK.Havok;
using HKSK.Model;
using HKX2;

namespace SKAssets.Authoring.Tests;

/// <summary>
/// The sabre cat's behaviour, amended so that every clip the cat has is played by
/// something: sneaking, falls and jumps, swimming, backward and fast locomotion, the
/// idle repertoire, lying on the side, and dying.
/// </summary>
/// <remarks>
/// Everything is added beside what the sabre cat has, in its own files, with the
/// numbers the blends and thresholds need read off the cat's clips as imported --
/// speed as travel over duration, turn rate as the root's yaw over duration.
/// </remarks>
internal sealed class CatGraph
{
    private readonly ActorProject _actor;
    private readonly StringBuilder _log;

    public CatGraph(ActorProject actor, StringBuilder log)
    {
        _actor = actor;
        _log = log;
    }

    private static string A(string name) => $@"Animations\{name}.hkx";

    /// <summary>Travel over duration, units a second.</summary>
    public float Speed(string anim)
    {
        ClipMovement? m = _actor.Animation(A(anim))?.Motion;
        return m is null || m.Duration <= 0 ? 0f : m.Travel / m.Duration;
    }

    /// <summary>The root's yaw over duration, degrees a second, left positive.</summary>
    public float TurnRate(string anim)
    {
        ClipMovement? m = _actor.Animation(A(anim))?.Motion;
        if (m is null || m.Duration <= 0 || m.Rotations.Count == 0) return 0f;
        Quaternion q = m.Rotations[^1].Value;
        float yaw = MathF.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z));
        return yaw * 180f / MathF.PI / m.Duration;
    }

    // ------------------------------------------------------------------ helpers

    private static hkbStateMachine RandomMachine(GraphEditor ed, string name, string next, hkbTransitionEffect? blend,
        params (string State, hkbGenerator Generator, float Probability)[] states)
    {
        hkbStateMachine sm = ed.StateMachine(name, 0, next);
        var made = states.Select(s =>
        {
            var st = ed.State(sm, s.State, s.Generator);
            st.m_probability = s.Probability;
            return st;
        }).ToList();
        foreach (var st in made) ed.Wildcard(sm, next, st, blend);
        return sm;
    }

    /// <summary>Clips one after another; each fires its own event at its end, the last <paramref name="done"/>.</summary>
    private static hkbStateMachine Sequence(GraphEditor ed, string name, string? done, hkbTransitionEffect? blend,
        params (string Anim, ClipMode Mode)[] steps)
    {
        hkbStateMachine sm = ed.StateMachine(name);
        var states = new List<hkbStateMachineStateInfo>();
        for (int i = 0; i < steps.Length; i++)
        {
            bool last = i == steps.Length - 1;
            string? fires = last ? done : steps[i].Mode == ClipMode.Looping ? null : $"{name}_{i}";
            var clip = fires is null
                ? ed.Clip($"{name}_{steps[i].Anim}", A(steps[i].Anim), steps[i].Mode)
                : ed.Clip($"{name}_{steps[i].Anim}", A(steps[i].Anim), steps[i].Mode, triggers: (fires, 0f, true));
            states.Add(ed.State(sm, $"{name}_{i}", clip));
        }
        for (int i = 0; i + 1 < steps.Length; i++)
            if (steps[i].Mode != ClipMode.Looping)
                ed.Transition(states[i], $"{name}_{i}", states[i + 1], blend);
        return sm;
    }

    private hkbBlenderGenerator TurnBlend(GraphEditor ed, string name, string left, hkbGenerator mid, string right, ClipMode mode = ClipMode.Looping)
    {
        float l = MathF.Abs(TurnRate(left)), r = -MathF.Abs(TurnRate(right));
        _log.AppendLine($"  blend {name}: {left} {l:F1}, {r:F1} {right} deg/s");
        return ed.Blend(name, "TurnDeltaDamped",
            (ed.Clip(name + "_L", A(left), mode), l), (mid, 0f), (ed.Clip(name + "_R", A(right), mode), r));
    }

    private static hkbModifierGenerator AnimDriven(GraphEditor ed, string name, hkbGenerator generator)
    {
        var active = new BSIsActiveModifier { m_name = name + "_IsActive", m_enable = true };
        ed.Bind(active, "bIsActive0", "bAnimationDriven");
        return ed.Modified(name + "_MG", active, generator);
    }

    private static HavokFile Load(string folder, string name) =>
        HavokFile.Load(Directory.EnumerateFiles(folder, "*.hkx", SearchOption.AllDirectories)
            .First(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase)));

    // ------------------------------------------------------------------ the amendment

    public void Amend(string folder)
    {
        HavokFile q = Load(folder, "QuadrupedBehavior.hkx");
        HavokFile f = Load(folder, "ForwardLocomotion.hkx");
        HavokFile n = Load(folder, "NonCombatIdle.hkx");
        HavokFile r = Load(folder, ZzCatCreature.Name + "Behavior.hkx");

        Locomotion(new GraphEditor(f), new GraphEditor(q));
        Quadruped(new GraphEditor(q));
        Idles(new GraphEditor(n));
        Death(new GraphEditor(r));

        foreach (HavokFile file in new[] { q, f, n, r }) file.Save(file.Path);
    }

    /// <summary>The forward blends' speeds and turn rates, and the fast run in the top band.</summary>
    private void Locomotion(GraphEditor ed, GraphEditor q)
    {
        float walk = Speed("WalkForward"), trot = Speed("TrotForward"), run = Speed("RunForward"), fast = Speed("RunFast_F_RM");
        float trotFast = MathF.Min(2 * trot, 0.8f * run);
        float runStart = 0.98f * trotFast, walkStart = 0.92f * runStart;
        _log.AppendLine($"speeds: walk {walk:F1}, trot {trot:F1}, run {run:F1}, fast run {fast:F1}; trot-fast band {trotFast:F1}; runStart > {runStart:F1}, walkStart < {walkStart:F1}");

        var walkBlend = ed.Require<hkbBlenderGenerator>("ForwardWalkBlend");
        float[] walkBands = [0.15f * walk, walk, trot, trotFast];
        for (int i = 0; i < walkBands.Length; i++) walkBlend.m_children[i].m_weight = walkBands[i];
        var runBlend = ed.Require<hkbBlenderGenerator>("ForwardRunBlend");
        runBlend.m_children[0].m_weight = run;
        runBlend.m_children[1].m_weight = fast;

        foreach (var (blend, left) in new[] { ("WalkSlowBlend_SabreCat", "WalkForwardL"), ("WalkBlend_SabreCat", "WalkForwardL"),
                                               ("TrotBlend_SabreCat", "TrotForwardL"), ("TrotFastBlend_SabreCat", "TrotForwardL"),
                                               ("RunSlowBlend_SabreCat", "RunForwardL"), ("RunBlend_SabreCat", "RunFast_L_RM") })
        {
            string right = left.EndsWith("_L_RM") ? left.Replace("_L_", "_R_") : left[..^1] + "R";
            var b = ed.Require<hkbBlenderGenerator>(blend);
            b.m_children[0].m_weight = MathF.Abs(TurnRate(left));
            b.m_children[1].m_weight = 0f;
            b.m_children[2].m_weight = -MathF.Abs(TurnRate(right));
            _log.AppendLine($"  {blend}: {b.m_children[0].m_weight:F1} / {b.m_children[2].m_weight:F1} deg/s");
        }

        // The top run band plays the fast run.
        var fastBlend = ed.Require<hkbBlenderGenerator>("RunBlend_SabreCat");
        string[] fastClips = ["RunFast_L_RM", "RunFast_F_RM", "RunFast_R_RM"];
        // Renamed with it: the cache numbers a clip by its name, and an existing name keeps its number.
        string[] fastNames = ["RunFastForwardL", "RunFastForward", "RunFastForwardR"];
        for (int i = 0; i < 3; i++)
        {
            var clip = (hkbClipGenerator)fastBlend.m_children[i].m_generator!;
            clip.m_animationName = A(fastClips[i]);
            clip.m_name = fastNames[i];
        }

        var eem = ed.Require<hkbEvaluateExpressionModifier>("FowardLocomotion_EEM");
        eem.m_expressions!.m_expressionsData[0].m_expression = $"runStart if (SpeedSampled > {runStart:F1})";
        eem.m_expressions.m_expressionsData[1].m_expression = $"walkStart if (SpeedSampled < {walkStart:F1})";
    }

    private void Quadruped(GraphEditor ed)
    {
        var blend = ed.Find<hkbBlendingTransitionEffect>("DefaultBlend");
        var toDriven = ed.Find<hkbBlendingTransitionEffect>("DefaultBlendToAnimationDriven") ?? blend;
        var fromDriven = ed.Find<hkbBlendingTransitionEffect>("DefaultBlendFromAnimationDriven") ?? blend;

        // ---- the turn clips' rate, and the backward walk's
        float loopRate = MathF.Abs(TurnRate("TurnLoopingL"));
        ed.Require<hkbEvaluateExpressionModifier>("SabreCatTurnSpeedMult_EEM").m_expressions!.m_expressionsData[0].m_expression =
            $"turnSpeedMult = fabs(TurnDelta/{loopRate:F1})";
        int backRate = ed.VariableIndex("walkBackRate");
        if (backRate >= 0)
            ed.Data.m_variableInitialValues!.m_wordVariableValues[backRate].m_value = BitConverter.SingleToInt32Bits(Speed("WalkBackward"));
        _log.AppendLine($"turn loop rate {loopRate:F1} deg/s; walk back {Speed("WalkBackward"):F1} u/s");

        // ---- the kill moves are the sabre cat's and a human's, on rigs the cat does not have
        var qRoot = ed.Require<hkbStateMachine>("QuadrupedRootBehavior");
        _log.AppendLine($"paired kill state removed: {ed.RemoveState(qRoot, "SabreCatPairedKillState")}");

        // ---- backward: a turning blend over the three backward walks
        var backMachine = ed.Require<hkbStateMachine>("WalkBackwardBehavior");
        var backState = ed.StateOf(backMachine, "NonCanineWalkBackward")!;
        backState.m_generator = TurnBlend(ed, "WalkBackwardBlend_Cat", "Walk_BL_RM", backState.m_generator!, "Walk_BR_RM");

        // ---- sneaking, on iIsInSneak, which the engine sets for any actor that sneaks
        ed.IntVariable("iIsInSneak");
        var defaultBehavior = ed.Require<hkbStateMachine>("DefaultBehavior");
        var defaultState = ed.Require<hkbStateMachine>("RootBehavior").m_states.First(s => ReferenceEquals(s.m_generator, defaultBehavior));
        defaultState.m_generator = ed.Modified("CatSneak_MG",
            ed.Expressions("CatSneak_EEM", "catSneakStart if (iIsInSneak == 1)", "catSneakStop if (iIsInSneak == 0)"),
            defaultBehavior);

        var standing = ed.StateOf(defaultBehavior, "StandingState")!;
        var locomotion = ed.StateOf(defaultBehavior, "LocomotionState")!;

        var crouchIdle = RandomMachine(ed, "CrouchIdleBehavior", "catCrouchNext", blend,
            ("CrouchIdle1", ed.Clip("CrouchIdle1", A("IdleCombat1"), triggers: ("catCrouchNext", 0f, true)), 1f),
            ("CrouchIdle2", ed.Clip("CrouchIdle2", A("IdleCombat2"), triggers: ("catCrouchNext", 0f, true)), 1f));
        var sneakStanding = ed.StateMachine("SneakStandingBehavior");
        var crouchEnter = ed.State(sneakStanding, "CrouchEnter", ed.Clip("CrouchEnter", A("CrouchIdle_start"), triggers: ("catCrouchIdle", 0f, true)));
        var crouchIdling = ed.State(sneakStanding, "CrouchIdle", crouchIdle);
        var crouchTurnL = ed.State(sneakStanding, "CrouchTurnLeft", ed.Clip("CrouchTurnLeft", A("CrouchTurn_L_RM"), ClipMode.Looping));
        var crouchTurnR = ed.State(sneakStanding, "CrouchTurnRight", ed.Clip("CrouchTurnRight", A("CrouchTurn_R_RM"), ClipMode.Looping));
        ed.Transition(crouchEnter, "catCrouchIdle", crouchIdling, blend);
        ed.Transition(crouchIdling, "turnLeft", crouchTurnL, blend);
        ed.Transition(crouchIdling, "turnRight", crouchTurnR, blend);
        ed.Transition(crouchTurnL, "turnStop", crouchIdling, blend);
        ed.Transition(crouchTurnR, "turnStop", crouchIdling, blend);
        ed.Transition(crouchTurnL, "turnRight", crouchTurnR, blend);
        ed.Transition(crouchTurnR, "turnLeft", crouchTurnL, blend);

        var sneakMoving = ed.StateMachine("SneakLocomotionBehavior");
        var sneakForward = ed.State(sneakMoving, "SneakForward",
            TurnBlend(ed, "SneakForwardBlend", "Crouch_L_RM", ed.Clip("SneakForward", A("Crouch_F_RM"), ClipMode.Looping), "Crouch_R_RM"));
        var sneakBackward = ed.State(sneakMoving, "SneakBackward",
            TurnBlend(ed, "SneakBackwardBlend", "Crouch_BL_RM", ed.Clip("SneakBackward", A("Crouch_B_RM"), ClipMode.Looping), "Crouch_BR_RM"));
        ed.Transition(sneakForward, "moveBackward", sneakBackward, blend);
        ed.Transition(sneakBackward, "moveForward", sneakForward, blend);

        var sneakStandState = ed.State(defaultBehavior, "SneakStandingState", sneakStanding);
        var sneakMoveState = ed.State(defaultBehavior, "SneakLocomotionState", sneakMoving);
        var sneakExit = ed.State(defaultBehavior, "SneakExitState", ed.Clip("CrouchExit", A("CrouchIdle_end"), triggers: ("catCrouchExited", 0f, true)));
        ed.Transition(standing, "catSneakStart", sneakStandState, blend);
        ed.Transition(locomotion, "catSneakStart", sneakMoveState, blend);
        ed.Transition(sneakStandState, "moveStart", sneakMoveState, blend);
        ed.Transition(sneakMoveState, "moveStop", sneakStandState, blend);
        ed.Transition(sneakStandState, "catSneakStop", sneakExit, blend);
        ed.Transition(sneakMoveState, "catSneakStop", locomotion, blend);
        ed.Transition(sneakExit, "catCrouchExited", standing, blend);
        ed.Transition(sneakExit, "moveStart", locomotion, blend);

        // ---- falling, landing and jumping: the engine's actions, through the cat's idle records
        var rootBehavior = ed.Require<hkbStateMachine>("RootBehavior");
        ed.IntVariable("bAnimationDriven");

        var fallStanding = ed.StateMachine("FallStandingBehavior");
        var stepOff = ed.State(fallStanding, "StepOff", ed.Clip("StepOff", A("JumpStart_Down_RM"), triggers: ("catFallAir", 0f, true)));
        var airStanding = ed.State(fallStanding, "AirStanding", RandomMachine(ed, "AirStandingBehavior", "catAirNext", blend,
            ("AirLow", ed.Clip("AirLow", A("JumpAir_low"), ClipMode.Looping), 1f),
            ("AirHigh", ed.Clip("AirHigh", A("JumpAir_high"), ClipMode.Looping), 1f),
            ("AirUp", ed.Clip("AirUp", A("JumpAir_up"), ClipMode.Looping), 1f)));
        ed.Transition(stepOff, "catFallAir", airStanding, blend);

        var airMoving = RandomMachine(ed, "AirMovingBehavior", "catAirNext", blend,
            ("AirLowForward", ed.Clip("AirLowForward", A("JumpAir_low_F"), ClipMode.Looping), 1f),
            ("AirHighForward", ed.Clip("AirHighForward", A("JumpAir_high_F"), ClipMode.Looping), 1f),
            ("AirUpForward", ed.Clip("AirUpForward", A("JumpAir_up_F"), ClipMode.Looping), 1f),
            ("AirHorizontal", ed.Clip("AirHorizontal", A("JumpAir_horiz"), ClipMode.Looping), 1f));

        var landStanding = RandomMachine(ed, "LandStandingBehavior", "catLandNext", blend,
            ("Land", ed.Clip("Land", A("JumpLand"), triggers: ("returnToDefault", 0f, true)), 1f),
            ("LandInPlace", ed.Clip("LandInPlace", A("JumpLand_Place"), triggers: ("returnToDefault", 0f, true)), 1f));
        var landMoving = ed.Clip("LandForward", A("JumpLand_F_RM"), triggers: ("returnToDefault", 0f, true));

        var jumpStanding = RandomMachine(ed, "JumpStandingBehavior", "catJumpNext", blend,
            ("JumpInPlace", ed.Clip("JumpInPlace", A("JumpPlace_RM"), triggers: ("returnToDefault", 0f, true)), 1f),
            ("JumpUp", Sequence(ed, "JumpUpSequence", "catJumpUpAir", blend, ("JumpStart_place_RM", ClipMode.SinglePlay), ("JumpAir_up", ClipMode.Looping)), 1f));
        var jumpUpLand = RandomMachine(ed, "JumpUpLandBehavior", "catJumpUpNext", blend,
            ("JumpUpEnd1", ed.Clip("JumpUpEnd1", A("JumpUp1_end_RM"), triggers: ("returnToDefault", 0f, true)), 1f),
            ("JumpUpEnd2", ed.Clip("JumpUpEnd2", A("JumpUp2_end_RM"), triggers: ("returnToDefault", 0f, true)), 1f),
            ("JumpUpEnd3", ed.Clip("JumpUpEnd3", A("JumpUp3_end_RM"), triggers: ("returnToDefault", 0f, true)), 1f));
        var jumpForward = RandomMachine(ed, "JumpForwardBehavior", "catJumpNext", blend,
            ("JumpForwardWhole", ed.Clip("JumpForwardWhole", A("JumpFw_RM"), triggers: ("returnToDefault", 0f, true)), 1f),
            ("JumpForwardStart", Sequence(ed, "JumpForwardSequence", null, blend, ("JumpStart_F_RM", ClipMode.SinglePlay), ("JumpAir_high_F", ClipMode.Looping)), 1f));
        var jumpRunning = ed.Clip("JumpRunning", A("JumpRun_RM"), triggers: ("returnToDefault", 0f, true));

        int id = 60;
        hkbStateMachineStateInfo Root(string name, hkbGenerator g, bool driven) =>
            ed.State(rootBehavior, name, driven ? AnimDriven(ed, name, g) : g, id++);
        var sFallStanding = Root("CatFallStanding", fallStanding, false);
        var sFallMoving = Root("CatFallMoving", airMoving, false);
        var sLandStanding = Root("CatLandStanding", landStanding, true);
        var sLandMoving = Root("CatLandMoving", landMoving, true);
        var sJumpStanding = Root("CatJumpStanding", jumpStanding, true);
        var sJumpUpLand = Root("CatJumpUpLand", jumpUpLand, true);
        var sJumpForward = Root("CatJumpForward", jumpForward, true);
        var sJumpRunning = Root("CatJumpRunning", jumpRunning, true);
        float moving = 0.5f * Speed("WalkForward"), fastMoving = Speed("RunForward");
        ed.Wildcard(rootBehavior, "catFall", sFallStanding, blend, $"Speed < {moving:F1}");
        ed.Wildcard(rootBehavior, "catFall", sFallMoving, blend, $"Speed >= {moving:F1}");
        ed.Transition(sFallStanding, "catLand", sLandStanding, blend);
        ed.Transition(sFallMoving, "catLand", sLandMoving, toDriven);
        ed.Wildcard(rootBehavior, "catJump", sJumpStanding, toDriven, $"Speed < {moving:F1}");
        ed.Wildcard(rootBehavior, "catJump", sJumpForward, toDriven, $"(Speed >= {moving:F1}) && (Speed < {fastMoving:F1})");
        ed.Wildcard(rootBehavior, "catJump", sJumpRunning, toDriven, $"Speed >= {fastMoving:F1}");
        ed.Transition(sJumpStanding, "catLand", sJumpUpLand, blend);
        ed.Transition(sJumpForward, "catLand", sLandMoving, blend);

        // ---- swimming: in, still, forward and back with turning blends, and turning in place
        var swim = ed.Require<hkbStateMachine>("SwimBehavior");
        var swimForward = swim.m_states[0];
        swimForward.m_generator = TurnBlend(ed, "SwimForwardBlend", "Swim_L_RM", swimForward.m_generator!, "Swim_R_RM");
        var swimEnter = ed.State(swim, "SwimEnter", ed.Clip("SwimEnter", A("Swim_Enter_RM"), triggers: ("catSwimEntered", 0f, true)));
        var swimIdle = ed.State(swim, "SwimIdle", ed.Clip("SwimIdle", A("Swim_Idle"), ClipMode.Looping));
        var swimBack = ed.State(swim, "SwimBackward",
            TurnBlend(ed, "SwimBackwardBlend", "Swim_BL_RM", ed.Clip("SwimBackward", A("Swim_B_RM"), ClipMode.Looping), "Swim_BR_RM"));
        var swimTurnL = ed.State(swim, "SwimTurnLeft", ed.Clip("SwimTurnLeft", A("Swim_TurnL_RM"), ClipMode.Looping));
        var swimTurnR = ed.State(swim, "SwimTurnRight", ed.Clip("SwimTurnRight", A("Swim_TurnR_RM"), ClipMode.Looping));
        swim.m_startStateId = swimEnter.m_stateId;
        ed.Transition(swimEnter, "catSwimEntered", swimForward, blend, "Speed > 1");
        ed.Transition(swimEnter, "catSwimEntered", swimIdle, blend, "Speed <= 1");
        ed.Transition(swimIdle, "moveStart", swimForward, blend);
        ed.Transition(swimIdle, "turnLeft", swimTurnL, blend);
        ed.Transition(swimIdle, "turnRight", swimTurnR, blend);
        foreach (var turn in new[] { swimTurnL, swimTurnR })
        {
            ed.Transition(turn, "turnStop", swimIdle, blend);
            ed.Transition(turn, "moveStart", swimForward, blend);
        }
        ed.Transition(swimForward, "moveStop", swimIdle, blend);
        ed.Transition(swimForward, "moveBackward", swimBack, blend);
        ed.Transition(swimBack, "moveForward", swimForward, blend);
        ed.Transition(swimBack, "moveStop", swimIdle, blend);
        _ = fromDriven;
    }

    private void Idles(GraphEditor ed)
    {
        var blend = ed.Find<hkbBlendingTransitionEffect>("DefaultBlend");
        var slow = ed.Find<hkbBlendingTransitionEffect>("slowBlend") ?? blend;
        var root = ed.Require<hkbStateMachine>("NonCombatIdleBehavior_SabreCat");

        // ---- standing: the main idle among the rest of the repertoire, picked at random
        var main = ed.StateOf(root, "Default_Idle")!;
        var mainClip = (hkbClipGenerator)main.m_generator!;
        ed.AddTrigger(mainClip, "catNextIdle");
        (string, hkbGenerator, float) Once(string state, string anim, float p) =>
            (state, ed.Clip(state, A(anim), triggers: ("catNextIdle", 0f, true)), p);
        main.m_generator = RandomMachine(ed, "CatAmbientIdles", "catNextIdle", slow,
            ("MainIdle", mainClip, 4f),
            Once("Idle2", "Idle_2", 1f), Once("Idle3", "Idle_3", 1f), Once("Idle4", "Idle_4", 1f),
            Once("Idle5", "Idle_5", 1f), Once("Idle6", "Idle_6", 1f), Once("Idle7", "Idle_7", 1f),
            Once("Lick", "Lick", 0.5f), Once("Scratch", "Scratching", 0.5f),
            Once("SharpenClawsLow", "SharpenClaws_Horiz", 0.3f), Once("SharpenClawsHigh", "SharpenClaws_Vert", 0.3f),
            Once("CaressIdle", "Caress_idle", 0.3f), Once("Defecate", "Defecate", 0.1f),
            ("Drink", Sequence(ed, "DrinkSequence", "catNextIdle", slow,
                ("EatDrink_start", ClipMode.SinglePlay), ("Drinking", ClipMode.SinglePlay), ("EatDrink_end", ClipMode.SinglePlay)), 0.3f));

        // ---- feeding starts by lowering the head
        var feed = ed.Require<hkbStateMachine>("SpecialIdle_NoHeadtrackingBehavior");
        var feedLoop = ed.StateOf(feed, "Feed")!;
        var feedStart = ed.State(feed, "FeedStart", ed.Clip("FeedStart", A("EatDrink_start"), triggers: ("catFeedLoop", 0f, true)));
        feed.m_startStateId = feedStart.m_stateId;
        ed.Transition(feedStart, "catFeedLoop", feedLoop, slow);

        // ---- sitting: a caress among the sitting idles
        var sit = ed.Require<hkbStateMachine>("Default_Idle_SitBHR");
        var caressSit = ed.State(sit, "CaressSit", ed.Clip("CaressSit", A("Caress_sit"), triggers: ("returntoSitState", 0f, true)));
        ed.Transition(caressSit, "returntoSitState", ed.StateOf(sit, "DefaultSitState")!, slow);
        ed.Wildcard(sit, "PickNextSitIdle", caressSit, slow);

        // ---- lying on the belly: a caress, and sleep that starts and ends
        var lie = ed.Require<hkbStateMachine>("LieDownBehavior");
        var layDown = ed.StateOf(lie, "Idle_LayDown")!;
        var caressLie = ed.State(lie, "CaressLie", ed.Clip("CaressLie", A("Caress_lie"), triggers: ("idleLayExit", 0f, true)));
        ed.Transition(caressLie, "idleLayExit", layDown, slow);
        ed.Wildcard(lie, "PickNextLayIdle", caressLie, slow);
        var sleep = ed.StateOf(lie, "Idle_Sleep")!;
        var sleepMachine = ed.StateMachine("SleepSequence");
        var sleepStart = ed.State(sleepMachine, "SleepStart", ed.Clip("SleepStart", A("Lie_belly_sleep_start"), triggers: ("catSleep", 0f, true)));
        var sleeping = ed.State(sleepMachine, "Sleeping", sleep.m_generator!);
        ed.Transition(sleepStart, "catSleep", sleeping, slow);
        sleep.m_generator = sleepMachine;
        var sleepEnd = ed.State(lie, "SleepEnd", ed.Clip("SleepEnd", A("Lie_belly_sleep_end"), triggers: ("catSleepEnded", 0f, true)));
        ed.RemoveTransitions(sleep, "idleLayExit");
        ed.Transition(sleep, "idleLayExit", sleepEnd, slow);
        ed.Transition(sleepEnd, "catSleepEnded", layDown, slow);

        // ---- lying on the side: a mode of its own, reached from standing and from sitting
        var sideIdles = RandomMachine(ed, "LieSideBehavior", "catLieSideNext", slow,
            ("LieSide1", ed.Clip("LieSide1", A("BleedoutIdle"), triggers: ("catLieSideNext", 0f, true)), 2f),
            ("LieSide2", ed.Clip("LieSide2", A("Lie_side_loop_2"), triggers: ("catLieSideNext", 0f, true)), 2f),
            ("LieSideSleep", Sequence(ed, "SideSleepSequence", "catLieSideNext", slow,
                ("Lie_side_sleep_start", ClipMode.SinglePlay), ("Lie_side_sleep", ClipMode.SinglePlay), ("Lie_side_sleep_end", ClipMode.SinglePlay)), 1f));
        var sideClock = ed.ModifierList("LieSideClock",
            ed.EveryN("LieSideStopClock", "catLieSideNext", "catLieSideStop", 4, 2, true),
            ed.EveryN("LieSideToSitClock", "catLieSideNext", "catLieSideToSit", 7, 4, true));
        var defaultSit = ed.StateOf(root, "Default_Idle_Sit")!;
        var defaultLay = ed.StateOf(root, "Default_Idle_Lay")!;
        int id = 20;
        var sideEnter = ed.State(root, "LieSide_Enter", ed.Clip("LieSideEnter", A("BleedoutEnter"), triggers: ("catLieSideEntered", 0f, true)), id++);
        var side = ed.State(root, "LieSide", ed.Modified("LieSide_MG", sideClock, sideIdles), id++);
        var sideExit = ed.State(root, "LieSide_Exit", ed.Clip("LieSideExit", A("GetUpLeft"), triggers: ("catLieSideExited", 0f, true)), id++);
        var sideToSit = ed.State(root, "LieSideToSit", ed.Clip("LieSideToSit", A("Trans_LieSide_Sit"), triggers: ("SitEnter", 0f, true)), id++);
        var sitToSide = ed.State(root, "SitToLieSide", ed.Clip("SitToLieSide", A("Trans_Sit_LieSide"), triggers: ("catLieSideEntered", 0f, true)), id++);
        var sitToBelly = ed.State(root, "SitToLieBelly", ed.Clip("SitToLieBelly", A("Trans_Sit_LieBelly"), triggers: ("LieDownEnter", 0f, true)), id++);
        var bellyToSit = ed.State(root, "LieBellyToSit", ed.Clip("LieBellyToSit", A("Trans_LieBelly_Sit"), triggers: ("SitEnter", 0f, true)), id++);
        ed.Transition(main, "idleCatLieSideStart", sideEnter, slow);
        ed.Transition(sideEnter, "catLieSideEntered", side, slow);
        ed.Transition(side, "catLieSideStop", sideExit, slow);
        ed.Transition(side, "catLieSideToSit", sideToSit, slow);
        ed.Transition(sideExit, "catLieSideExited", main, slow);
        ed.Transition(sideToSit, "SitEnter", defaultSit, slow);
        ed.Transition(defaultSit, "catSitToLieSide", sitToSide, slow);
        ed.Transition(sitToSide, "catLieSideEntered", side, slow);
        ed.Transition(defaultSit, "catSitToLieBelly", sitToBelly, slow);
        ed.Transition(sitToBelly, "LieDownEnter", defaultLay, slow);
        ed.Transition(defaultLay, "catLieBellyToSit", bellyToSit, slow);
        ed.Transition(bellyToSit, "SitEnter", defaultSit, slow);

        // Now and then a sitting cat lies down and a lying one sits up.
        void Clock(hkbStateMachineStateInfo state, params hkbModifier[] clocks)
        {
            var mg = (hkbModifierGenerator)state.m_generator!;
            mg.m_modifier = ed.ModifierList(mg.m_name + "_Clock", [mg.m_modifier!, .. clocks]);
        }
        Clock(defaultSit,
            ed.EveryN("SitToLieBellyClock", "PickNextSitIdle", "catSitToLieBelly", 5, 3, true),
            ed.EveryN("SitToLieSideClock", "PickNextSitIdle", "catSitToLieSide", 7, 4, true));
        Clock(defaultLay, ed.EveryN("LieBellyToSitClock", "PickNextLayIdle", "catLieBellyToSit", 6, 4, true));
    }

    /// <summary>A directional death before the ragdoll: in combat or not, left or right.</summary>
    private void Death(GraphEditor ed)
    {
        var root = ed.Require<hkbStateMachine>("SabreCatRootBehavior");
        ed.IntVariable("iCombatStance");
        (string, hkbGenerator, float) Die(string anim) =>
            (anim, ed.Clip("Death_" + anim, A(anim), triggers: ("Ragdoll", 0f, true)), 1f);
        var calm = RandomMachine(ed, "DeathBehavior", "catDeathNext", null, Die("Die_L"), Die("Die_R"));
        var fighting = RandomMachine(ed, "CombatDeathBehavior", "catDeathNext", null, Die("Combat_Die_L"), Die("Combat_Die_R"));
        var dying = ed.StateMachine("AnimateToRagdollBehavior");
        ed.State(dying, "Death", calm, 0);
        ed.State(dying, "CombatDeath", fighting, 1);
        var animate = ed.State(root, "AnimateToRagdoll", dying, 6);
        ed.Wildcard(root, "DeathAnimation", animate, null, "iCombatStance == 0", toNestedStateId: 0);
        ed.Wildcard(root, "DeathAnimation", animate, null, "iCombatStance != 0", toNestedStateId: 1);
    }

    /// <summary>Every clip generator's name the graphs reach.</summary>
    public static IReadOnlySet<string> ClipNames(string folder)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(folder, "*.hkx", SearchOption.AllDirectories))
        {
            if (!path.Contains("behaviors", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (IHavokObject o in new GraphEditor(HavokFile.Load(path)).Reachable())
                if (o is hkbClipGenerator clip) names.Add(clip.m_name);
        }
        return names;
    }

    /// <summary>Every animation name the graphs' clip generators play.</summary>
    public static IReadOnlySet<string> Played(string folder)
    {
        var played = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(folder, "*.hkx", SearchOption.AllDirectories))
        {
            if (!path.Contains("behaviors", StringComparison.OrdinalIgnoreCase)) continue;
            HavokFile file = HavokFile.Load(path);
            foreach (IHavokObject o in new GraphEditor(file).Reachable())
                if (o is hkbClipGenerator clip) played.Add(clip.m_animationName);
        }
        return played;
    }
}

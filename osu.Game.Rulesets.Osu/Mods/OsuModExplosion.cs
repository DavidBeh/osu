// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Objects.Drawables;
using osu.Game.Rulesets.Osu.UI;
using osu.Game.Rulesets.UI;
using osuTK;

namespace osu.Game.Rulesets.Osu.Mods
{
    internal partial class OsuModExplosion : Mod, IApplicableToDrawableRuleset<OsuHitObject>, IUpdatableByPlayfield, IApplicableToDrawableHitObject, IApplicableToHitObject
    {
        public override string Name => "Explosion";
        public override LocalisableString Description => "Explode the circles!";
        public override double ScoreMultiplier => 1.0;
        public override string Acronym => "EX";

        public override IconUsage? Icon => FontAwesome.Solid.Magnet;

        public override ModType Type => ModType.DifficultyReduction;

        private DrawableOsuRuleset ruleset;

        private Dictionary<OsuHitObject, PhysicsState> physicsStates = new Dictionary<OsuHitObject, PhysicsState>();

        private LinkedList<OsuAction> actions = new LinkedList<OsuAction>();

        public void ApplyToDrawableRuleset(DrawableRuleset<OsuHitObject> drawableRuleset)
        {
            ruleset = (DrawableOsuRuleset)drawableRuleset;

            ruleset.KeyBindingInputManager.Add(new InputNotifier(this));
            //physicsStates.Clear();
        }

        private void HandleInput(OsuAction eAction)
        {
            if (eAction == OsuAction.Smoke) return;
            actions.AddLast(eAction);
        }

        public void Update(Playfield playfield)
        {
            float deltaTime = (float)playfield.Clock.ElapsedFrameTime / 1000f;


            var cursorPos = playfield.Cursor.AsNonNull().ActiveCursor.DrawPosition;
            var hitCount = actions.Count;
            actions.Clear();

            foreach (var drawableHitObject in playfield.HitObjectContainer.AliveObjects)
            {
                if (drawableHitObject is DrawableHitCircle circle && !circle.Result.HasResult)
                {
                    var state = physicsStates[(OsuHitObject)drawableHitObject.HitObject];
                    var offset = circle.Position - cursorPos;

                    if (hitCount > 0 && offset.Length < 400f && offset.Length > 0f)
                    {
                        // magnituede based on distance from cursor

                        float duration = 0.2f;
                        float factor = 400f;
                        float magnituede = (float)Math.Pow(1f - offset.Length / 390, 3) * (factor / duration);
                        Logger.Log("magmitude: " + magnituede, level: LogLevel.Verbose);
                        var force = offset.Normalized() * magnituede;
                        state.ApplyForce(force, duration);
                        // Log all values
                        //Logger.Log($"Force: {force}, Magnituede: {magnituede}, Offset: {offset}, Position: {circle.Position}, Cursor: {cursorPos}, DeltaTime {deltaTime} Velocity {state.Velocity}",
                        //level: LogLevel.Verbose);
                    }

                    var velobefore = state.Velocity;
                    state.Update(deltaTime);
                    //Logger.Log($"HitObject: {drawableHitObject}, Position: {circle.Position}, Velocity: {state.Velocity}", level: LogLevel.Verbose);
                    Logger.Log($"Before: {velobefore} After: {state.Velocity}", level: LogLevel.Verbose);

                    var destination = Vector2.Clamp(circle.Position + state.Velocity * deltaTime, Vector2.Zero, OsuPlayfield.BASE_SIZE);

                    if (destination.Y == Single.NaN)
                    {
                        return;
                    }

                    circle.Position = destination;
                }
            }
        }

        private partial class InputNotifier : Component, IKeyBindingHandler<OsuAction>
        {
            private readonly OsuModExplosion mod;

            public InputNotifier(OsuModExplosion mod)
            {
                this.mod = mod;
            }

            public bool OnPressed(KeyBindingPressEvent<OsuAction> e)
            {
                mod.HandleInput(e.Action);
                return false;
            }

            public void OnReleased(KeyBindingReleaseEvent<OsuAction> e)
            {
            }
        }

        public void ApplyToDrawableHitObject(DrawableHitObject drawable)
        {
            if (drawable is DrawableHitCircle circle)
            {
                //Logger.Log("Explosion mod applied to hit circle", level: LogLevel.Verbose);
            }
        }

        public void ApplyToHitObject(HitObject hitObject)
        {
            if (hitObject is HitCircle circle)
            {
                physicsStates[circle] = new PhysicsState();
            }
        }

        private class PhysicsState
        {
            public Vector2 Velocity { get; set; } = Vector2.Zero;
            public LinkedList<Force> Forces { get; } = new LinkedList<Force>();


            private float MaxVelocity => 400.0f;
            private float FrictionBreakpoint => 300f;
            private float FrictionCoefficient => 1000f;

            public void Update(float deltaTime)
            {
                var node = Forces.First;

                while (node != null)
                {
                    var next = node.Next;

                    var force = node.Value;
                    var old = force.Progress;
                    force.Progress = Math.Min(force.Progress + deltaTime, force.Duration);

                    deltaTime = force.Progress - old;
                    var normalizedProgress = force.Progress / force.Duration;
                    var velocityChange = force.Acceleration * deltaTime;
                    //Logger.Log($"New Velocity: {velocityChange} Nor {normalizedProgress}", level: LogLevel.Verbose);
                    Velocity += velocityChange;

                    if (force.Progress == force.Duration)
                    {
                        Forces.Remove(node);
                    }

                    node = next;
                }

                if (Velocity.Length > 0f)
                    Velocity = Math.Min(Velocity.Length, MaxVelocity) * Velocity.Normalized();

                // Apply Friction - proportional to the velocity
                ApplyFriction(deltaTime);
            }

            private void ApplyFriction(float deltaTime)
            {
                // Friction is proportional to velocity and opposite in direction
                // Apply friction only if the object is moving
                if (Velocity.Length > 0f)
                {
                    var frictionMagnitude = Velocity.Length * FrictionCoefficient * (float)Math.Pow(Math.Clamp(Velocity.Length / FrictionBreakpoint, 0f, 3f), 5);

                    var newMagnitude = Math.Max(0f, Velocity.Length - frictionMagnitude * deltaTime);

                    Velocity = Velocity.Normalized() * newMagnitude;

                    // Optionally: if velocity is too small, stop completely (to avoid very small speeds due to precision issues)
                    if (Velocity.Length < 3f && Forces.Count == 0)
                    {
                        Velocity = Vector2.Zero;
                    }
                }
            }

            public void ApplyForce(Vector2 acceleration, float duration)
            {
                Forces.AddLast(new Force
                {
                    Acceleration = acceleration,
                    Duration = duration,
                });
            }

            // https://easings.net/de#easeOutExpo
            private float HorizFlippedEaseOutExpo(float t)
            {
                return Math.Clamp(1f - (t == 1f ? 1f : 1f - (float)Math.Pow(2d, -10d * t)), 0f, 1f);
            }
        }

        private class Force
        {
            public Vector2 Acceleration { get; set; }
            public float Progress { get; set; }
            public float Duration { get; set; }
        }
    }
}

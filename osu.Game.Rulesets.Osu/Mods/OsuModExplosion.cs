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
using osu.Framework.Utils;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Objects.Drawables;
using osu.Game.Rulesets.Osu.UI;
using osu.Game.Rulesets.Osu.Utils;
using osu.Game.Rulesets.UI;
using osuTK;

namespace osu.Game.Rulesets.Osu.Mods
{
    internal partial class OsuModExplosion : Mod, IApplicableToDrawableRuleset<OsuHitObject>, IUpdatableByPlayfield, IApplicableToHitObject
    {
        public override string Name => "Explosion";
        public override LocalisableString Description => "Explode the circles!";
        public override double ScoreMultiplier => 1.0;
        public override string Acronym => "EX";

        public override IconUsage? Icon => FontAwesome.Solid.Magnet;

        public override ModType Type => ModType.DifficultyReduction;

        private DrawableOsuRuleset ruleset = null!;
        private int unprocessedKeyDownCount;
        private int unprocessedKeyUpCount;
        private Vector2 dampedCursorPos = Vector2.Zero;
        private Vector2 lastCursorPos = Vector2.Zero;

        public void ApplyToDrawableRuleset(DrawableRuleset<OsuHitObject> drawableRuleset)
        {
            ruleset = (DrawableOsuRuleset)drawableRuleset;
            ruleset.KeyBindingInputManager.Add(new InputNotifier(this));
        }

        private void HandleInput(OsuAction eAction, bool keyDown)
        {
            if (eAction == OsuAction.Smoke) return;

            if (keyDown)
            {
                unprocessedKeyDownCount++;
            }
            else
            {
                unprocessedKeyUpCount++;
            }
        }

        // https://keithmaggio.wordpress.com/2011/02/15/math-magician-lerp-slerp-and-nlerp/
        Vector3 Slerp(Vector3 start, Vector3 end, float percent)
        {
            // Dot product - the cosine of the angle between 2 vectors.
            float dot = Vector3.Dot(start, end);
            // Clamp it to be in the range of Acos()
            // This may be unnecessary, but floating point
            // precision can be a fickle mistress.
            Math.Clamp(dot, -1.0f, 1.0f);
            // Acos(dot) returns the angle between start and end,
            // And multiplying that by percent returns the angle between
            // start and the final result
            float theta = MathF.Acos(dot)*percent;
            Vector3 RelativeVec = end - start*dot;
            RelativeVec.Normalize();
            // Orthonormal basis
            // The final result.
            return ((start * MathF.Cos(theta)) + (RelativeVec * MathF.Sin(theta)));
        }

        public void Update(Playfield playfield)
        {
            float time = (float)playfield.Clock.CurrentTime;

            Vector2 cursorPos = playfield.Cursor.AsNonNull().ActiveCursor.DrawPosition;
            dampedCursorPos.X = (float)Interpolation.DampContinuously(dampedCursorPos.X, cursorPos.X, 150, playfield.Clock.ElapsedFrameTime);
            dampedCursorPos.Y = (float)Interpolation.DampContinuously(dampedCursorPos.Y, cursorPos.Y, 150, playfield.Clock.ElapsedFrameTime);
            if (Vector2.Distance(dampedCursorPos, cursorPos) < 0.00001f)
                dampedCursorPos = lastCursorPos;

            if (lastCursorPos.Equals(cursorPos) is false)
                lastCursorPos = cursorPos;

            foreach (DrawableHitObject? drawableHitObject in playfield.HitObjectContainer.AliveObjects)
            {
                if (drawableHitObject is not DrawableOsuHitObject)
                    continue;

                var drawableOsuHitObject = (DrawableOsuHitObject)drawableHitObject;

                if (drawableHitObject is DrawableHitCircle circle2 && circle2.Result.HasResult)
                    continue;
                if (drawableHitObject is DrawableSlider slider2 && slider2.HeadCircle.Result.HasResult)
                    continue;

                if (hitObjectMovement.TryGetValue(drawableOsuHitObject.HitObject, out var movement))
                {
                    float x = movement.xMovement.Advance(time);
                    float y = movement.yMovement.Advance(time);

                    drawableHitObject.Position = new Vector2(x, y);
                }

                if (unprocessedKeyDownCount + unprocessedKeyUpCount is 0)
                    continue;

                Vector2 forceReciever;

                switch (drawableOsuHitObject)
                {
                    case DrawableHitCircle circle when circle.Result.HasResult is false:
                        forceReciever = circle.Position;
                        break;

                    case DrawableSlider slider:
                        if (!slider.HeadCircle.Result.HasResult)
                            forceReciever = slider.Position;
                        else
                            forceReciever = slider.Position + slider.Ball.DrawPosition;
                        break;

                    default:
                        continue;
                }

                Vector2 dampedCursorMovementNormal = Vector2.Normalize(cursorPos - dampedCursorPos);
                Vector2 cursorNormal = Vector2.Normalize(forceReciever - cursorPos);

                float slerpBlend = Math.Clamp(1 - Vector2.Distance(cursorPos, forceReciever) / (0.3f * OsuPlayfield.BASE_SIZE.X), 0f, 1f);
                slerpBlend = MathF.Pow(slerpBlend, 2);
                Vector2 normal = Slerp(new(cursorNormal), new(dampedCursorMovementNormal), slerpBlend).Xy.Normalized();

                float distanceForceFactor = Math.Clamp(1 - Vector2.Distance(cursorPos, forceReciever) / (0.9f * OsuPlayfield.BASE_SIZE.X), 0f, 0.6f);
                distanceForceFactor = MathF.Pow(distanceForceFactor, 3);
                float baseForceFactor = 80f;

                void AddForce(float factor, float duration)
                {
                    Vector2 calculatedOffset = normal * distanceForceFactor * baseForceFactor * factor;
                    movement.xMovement.Forces.Add(new Force1D(calculatedOffset.X, time, time + duration, 3));
                    movement.yMovement.Forces.Add(new Force1D(calculatedOffset.Y, time, time + duration, 3));
                }

                if (unprocessedKeyUpCount is not 0)
                {
                    float forceFactor = -0.6f * unprocessedKeyUpCount;
                    AddForce(forceFactor, 800);
                }

                if (unprocessedKeyDownCount is not 0)
                {
                    float forceFactor = 1f * unprocessedKeyDownCount;
                    AddForce(forceFactor, 600);
                }
            }

            unprocessedKeyDownCount = 0;
            unprocessedKeyUpCount = 0;
        }

        private Vector2 GetDestination(Vector2 origin, Vector2 direction, float factor)
        {
            return origin + direction * factor;
        }

        private Dictionary<OsuHitObject, (HitObjectKinetics1D xMovement, HitObjectKinetics1D yMovement)> hitObjectMovement = new();

        public void ApplyToHitObject(HitObject hitObject)
        {
            if (hitObject is HitCircle || hitObject is Slider)
            {
                var osuHitObject = (OsuHitObject)hitObject;
                this.hitObjectMovement[osuHitObject] = (new(osuHitObject, true), new(osuHitObject, false));
            }
        }

        public record Force1D(float FullOffset, float OriginalStart, float OriginalEnd, float Exponent, float Start, float End)
        {
            public Force1D(float FullOffset, float OriginalStart, float OriginalEnd, float Exponent)
                : this(FullOffset, OriginalStart, OriginalEnd, Exponent, OriginalStart, OriginalEnd)
            {
            }

            private float Progress(float t) => (t - OriginalStart) / (OriginalEnd - OriginalStart);
            private float ProgressInterpolated(float t) => 1 - MathF.Pow(1 - Progress(t), Exponent);

            public float this[float t]
            {
                get
                {
                    if (t <= Start || OriginalEnd == OriginalStart || Start >= End) return 0;
                    float StartOffset = ProgressInterpolated(Start) * FullOffset;
                    return ProgressInterpolated(Math.Min(End, t)) * FullOffset - StartOffset;
                }
            }

            public Force1D CreateSubforce(float? minStart = null, float? maxEnd = null, float? fullOffset = null)
            {
                var newForce = this with
                {
                    Start = Math.Min(minStart ?? this.Start, this.End),
                    End = Math.Max(maxEnd ?? this.End, this.Start),
                    FullOffset = fullOffset ?? FullOffset,
                };
                return newForce;
            }
        }

        private partial class HitObjectKinetics1D
        {
            public readonly List<Force1D> Forces = new List<Force1D>();

            private float lastTime = float.MinValue;
            private readonly float initialPosition;

            private readonly float minPosition;
            private readonly float maxPosition;

            public HitObjectKinetics1D(OsuHitObject hitObject, bool isX)
            {
                initialPosition = isX ? hitObject.Position.X : hitObject.Position.Y;

                switch (hitObject)
                {
                    case HitCircle:
                        minPosition = 0;
                        maxPosition = isX ? OsuPlayfield.BASE_SIZE.X : OsuPlayfield.BASE_SIZE.Y;
                        break;

                    case Slider slider:
                        var possibleMovementBounds = OsuHitObjectGenerationUtils.CalculatePossibleMovementBounds(slider);
                        minPosition = isX ? possibleMovementBounds.Left : possibleMovementBounds.Top;
                        maxPosition = isX ? possibleMovementBounds.Right : possibleMovementBounds.Bottom;
                        break;

                    default:
                        throw new NotImplementedException();
                }
            }

            public float Advance(float destinationTime)
            {
                float position = initialPosition;

                foreach (var force in Forces)
                {
                    float forceValue = force[destinationTime];
                    position += forceValue;
                }

                void HandleCollision(bool isMin)
                {
                    int count = Forces.Count;

                    for (var i = 0; i < count; i++)
                    {
                        var force = Forces[i];
                        if (force.End <= lastTime || force.Start >= destinationTime) continue;

                        if (isMin && force.FullOffset < 0 || !isMin && force.FullOffset > 0)
                        {
                            Forces[i] = force with { End = lastTime };
                            //Forces.Add(force with { Start = lastTime, FullOffset = -force.FullOffset * 0.5f });
                        }
                    }
                }

                if (position < minPosition)
                {
                    HandleCollision(true);
                }
                else if (position > maxPosition)
                {
                    HandleCollision(false);
                }
                else
                {
                    lastTime = destinationTime;
                    return position;
                }

                position = initialPosition;

                foreach (var force in Forces)
                {
                    float forceValue = force[destinationTime];
                    position += forceValue;
                }

                lastTime = destinationTime;
                return position;
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
                mod.HandleInput(e.Action, true);
                return false;
            }

            public void OnReleased(KeyBindingReleaseEvent<OsuAction> e)
            {
                mod.HandleInput(e.Action, false);
            }
        }
    }
}

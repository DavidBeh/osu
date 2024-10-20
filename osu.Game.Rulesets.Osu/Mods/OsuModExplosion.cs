// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Framework.Utils;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Judgements;
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
    internal partial class OsuModExplosion : Mod, IApplicableToDrawableRuleset<OsuHitObject>, IUpdatableByPlayfield, IApplicableToHitObject, IApplicableToDrawableHitObject, IApplicableToBeatmap
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

        private Dictionary<OsuHitObject, (HitObjectKinetics1D xMovement, HitObjectKinetics1D yMovement)> hitObjectMovement = new();

        public void ApplyToDrawableRuleset(DrawableRuleset<OsuHitObject> drawableRuleset)
        {
            ruleset = (DrawableOsuRuleset)drawableRuleset;
            ruleset.KeyBindingInputManager.Add(new InputNotifier(this));


            Logger.Log("Applying to ruleset", "OsuModExplosion", LogLevel.Debug);
            hitObjectMovement.Clear();

            foreach (var beatmapHitObject in ruleset.Beatmap.HitObjects)
            {
                if (beatmapHitObject is not OsuHitObject osuHitObject)
                    continue;
                if (beatmapHitObject is not HitCircle and not Slider)
                    continue;

                hitObjectMovement[osuHitObject] = (new(osuHitObject, true), new(osuHitObject, false));
            }
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
                if (drawableHitObject is not DrawableOsuHitObject drawableOsuHitObject)
                    continue;

                if (drawableOsuHitObject is DrawableHitCircle circle2 && circle2.Result.HasResult)
                    continue;

                if (drawableOsuHitObject is DrawableSlider slider2 && slider2.HeadCircle.Result.HasResult)
                    continue;

                /*
                if (drawableOsuHitObject is not DrawableHitCircle) // TODO implement others (slider head)
                    continue;
                    */

                if (hitObjectMovement.TryGetValue(drawableOsuHitObject.HitObject, out var movement))
                {
                    float x = movement.xMovement.Advance(time);
                    float y = movement.yMovement.Advance(time);

                    if (x != drawableOsuHitObject.Position.X || y != drawableOsuHitObject.Position.Y)
                        Logger.Log($"Moving {drawableOsuHitObject.HitObject} to {x}, {y}", "OsuModExplosion", LogLevel.Debug);
                    drawableOsuHitObject.Position = new Vector2(x, y);
                }
                /*

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
                */
            }

            unprocessedKeyDownCount = 0;
            unprocessedKeyUpCount = 0;
        }

        public void ApplyToHitObject(HitObject hitObject)
        {
            /*
            if (hitObject is HitCircle || hitObject is Slider)
            {
                var osuHitObject = (OsuHitObject)hitObject;
                this.hitObjectMovement[osuHitObject] = (new(osuHitObject, true), new(osuHitObject, false));
            }*/
        }

        public void ApplyToDrawableHitObject(DrawableHitObject drawable)
        {
            void onDrawableOnOnNewResult(DrawableHitObject drawableHitObject, JudgementResult result)
            {
                if (drawableHitObject is not DrawableOsuHitObject drawableOsuHitObject) return;

                Logger.Log($"{drawableHitObject.GetType()}", "OsuModExplosion", LogLevel.Debug);
                if (drawableHitObject is DrawableSliderHead)
                    Logger.Log("SliderHead", "OsuModExplosion", LogLevel.Debug);
                switch (drawableOsuHitObject.HitObject)
                {
                    case HitCircle and not SliderEndCircle when result.IsHit: // TODO implement others (slider head)
                        AddForce(drawableOsuHitObject);
                        break;
                }
            }

            drawable.OnNewResult += onDrawableOnOnNewResult;
        }

        private void AddForce(DrawableOsuHitObject source)
        {
            Vector2 forceSourcePos = getPosition(source);
            float time = (float)ruleset.Playfield.Clock.CurrentTime;

            foreach (var targetDrawableHitObject in ruleset.Playfield.HitObjectContainer.AliveObjects.OfType<DrawableOsuHitObject>())
            {
                if (targetDrawableHitObject is not DrawableHitCircle and not DrawableSlider)
                    continue;
                if (targetDrawableHitObject.Result.HasResult || source == targetDrawableHitObject)
                    continue;

                Vector2 forceDestPos = getPosition(targetDrawableHitObject);
                Vector2 direction = Vector2.Normalize(forceDestPos - forceSourcePos);

                if (float.IsRealNumber(direction.X) is false || float.IsRealNumber(direction.Y) is false)
                    continue;

                if (hitObjectMovement.TryGetValue(targetDrawableHitObject.HitObject, out var movement) is false)
                {
                    Logger.Log($"HitObject {source.HitObject} not found in hitObjectMovement", "OsuModExplosion", LogLevel.Error);
                    continue;
                }

                float distance = Vector2.Distance(forceSourcePos, forceDestPos);

                float distanceForceFactor = MathF.Pow(Math.Clamp(1 - distance / (0.9f * OsuPlayfield.BASE_SIZE.Y), 0f, 0.8f), 2);

                float duration = 600;
                Vector2 calculatedOffset = direction * distanceForceFactor * 20f;
                Logger.Log($"Adding force at time {time} to {targetDrawableHitObject.HitObject} from {source.HitObject} with offset {calculatedOffset}", "OsuModExplosion", LogLevel.Debug);
                movement.xMovement.Forces.Add(new Force1D(calculatedOffset.X, time, time + duration, 3));
                movement.yMovement.Forces.Add(new Force1D(calculatedOffset.Y, time, time + duration, 3));
            }
        }

        private Vector2 getPosition(DrawableOsuHitObject drawableObject)
        {
            switch (drawableObject)
            {
                // SliderHeads are derived from HitCircles,
                // so we must handle them before to avoid them using the wrong positioning logic
                case DrawableSliderHead sliderHead:
                    return sliderHead.DrawableSlider.Position + sliderHead.Position;

                // get position of slider head
                case DrawableSlider slider:
                    return slider.Position + slider.Ball.DrawPosition;

                // Using hitobject position will cause issues with HitCircle placement due to stack leniency.
                case DrawableHitCircle:
                    return drawableObject.Position;

                default:
                    return drawableObject.HitObject.Position;
            }
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
            float theta = MathF.Acos(dot) * percent;
            Vector3 RelativeVec = end - start * dot;
            RelativeVec.Normalize();
            // Orthonormal basis
            // The final result.
            return ((start * MathF.Cos(theta)) + (RelativeVec * MathF.Sin(theta)));
        }

        private Vector2 GetDestination(Vector2 origin, Vector2 direction, float factor)
        {
            return origin + direction * factor;
        }

        public record Force1D(float FullOffset, float OriginalStart, float OriginalEnd, float Exponent, float Start, float End)
        {
            public float this[float t]
            {
                get
                {
                    if (t <= Start || OriginalEnd == OriginalStart || Start >= End) return 0;
                    float StartOffset = ProgressInterpolated(Start) * FullOffset;
                    return ProgressInterpolated(Math.Min(End, t)) * FullOffset - StartOffset;
                }
            }

            public Force1D(float FullOffset, float OriginalStart, float OriginalEnd, float Exponent)
                : this(FullOffset, OriginalStart, OriginalEnd, Exponent, OriginalStart, OriginalEnd)
            {
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

            private float Progress(float t) => (t - OriginalStart) / (OriginalEnd - OriginalStart);
            private float ProgressInterpolated(float t) => 1 - MathF.Pow(1 - Progress(t), Exponent);
        }

        private partial class HitObjectKinetics1D
        {
            public readonly List<Force1D> Forces = new List<Force1D>();
            private readonly float initialPosition;

            private readonly float minPosition;
            private readonly float maxPosition;

            private float lastTime = float.MinValue;

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
                        Logger.Log("HitObjectKinetics1D created for unsupported hitobject type", "OsuModExplosion", LogLevel.Error);
                        break;
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

        public void ApplyToBeatmap(IBeatmap beatmap)
        {

        }
    }
}

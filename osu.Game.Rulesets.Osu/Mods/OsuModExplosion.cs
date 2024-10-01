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
using osu.Game.Rulesets.UI;
using osuTK;
using Logger = osu.Framework.Logging.Logger;

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

        public void Update(Playfield playfield)
        {
            float time = (float)playfield.Clock.CurrentTime;

            Vector2 cursorPos = playfield.Cursor.AsNonNull().ActiveCursor.DrawPosition;

            foreach (DrawableHitObject? drawableHitObject in playfield.HitObjectContainer.AliveObjects)
            {
                if (drawableHitObject is DrawableHitCircle circle)
                {
                    if (unprocessedKeyDownCount + unprocessedKeyUpCount is not 0)
                    {
                        float forceFactor = 200f;
                        float variableForceFactor = Math.Clamp(1 - Vector2.Distance(cursorPos, circle.Position) / OsuPlayfield.BASE_SIZE.X, 0f, 0.5f);
                        Vector2 normal = Vector2.Normalize(circle.Position - cursorPos);
                        if (circle.Result.HasResult)
                            continue;

                        if (unprocessedKeyDownCount is not 0)
                        {
                            Logger.Log("Key down");
                            var destination = Vector2.Clamp(GetDestination(circle.Position, normal, forceFactor * variableForceFactor * unprocessedKeyDownCount), Vector2.Zero, OsuPlayfield.BASE_SIZE);
                            //var destination = Vector2.Clamp(2 * circle.Position - cursorPos, Vector2.Zero, OsuPlayfield.BASE_SIZE);

                            // Log Position and Destination
                            //Logger.Log($"Position: {circle.Position} Destination: {destination}");
                            AddForce(circle.HitObject, time, 700, destination);

                        }

                        if (unprocessedKeyUpCount is not 0)
                        {
                            //Logger.Log("Key up");

                            var destination = Vector2.Clamp(GetDestination(circle.Position, normal, -0.5f * forceFactor * variableForceFactor * unprocessedKeyUpCount), Vector2.Zero, OsuPlayfield.BASE_SIZE);
                            Logger.Log($"Position: {circle.Position} Destination: {destination}");

                            AddForce(circle.HitObject, time, 700, destination);
                        }
                    }

                    float x = drawableHitObject.Position.X;
                    float y = drawableHitObject.Position.Y;

                    foreach (var (startTime, halfTime, destination) in hitObjectMovement[drawableHitObject.HitObject])
                    {
                        if (time < startTime)
                            continue;

                       float dt = (float)Math.Min(time - startTime, playfield.Clock.ElapsedFrameTime);
                       //float dt = (float)playfield.Clock.ElapsedFrameTime;

                        x = (float)Interpolation.DampContinuously(x, destination.X, halfTime, dt);
                        y = (float)Interpolation.DampContinuously(y, destination.Y, halfTime, dt);
                    }

                    drawableHitObject.Position = new Vector2(x, y);
                }


            }
            unprocessedKeyDownCount = 0;
            unprocessedKeyUpCount = 0;
        }

        private Vector2 GetDestination(Vector2 origin, Vector2 direction, float factor)
        {
            return origin + direction * factor;
        }

        private void AddForce(OsuHitObject hitObject, float startTime, float halfTime, Vector2 destination)
        {
            hitObjectMovement[hitObject].Add((startTime, halfTime, destination));
        }

        public Dictionary<HitObject, List<(float startTime, float halfTime, Vector2 destination)>> hitObjectMovement =
            new Dictionary<HitObject, List<(float startTime, float halfTime, Vector2 destination)>>();

        public void ApplyToHitObject(HitObject hitObject)
        {
            if (hitObject is HitCircle circle)
                this.hitObjectMovement[circle] = new List<(float startTime, float halfTime, Vector2 destination)>();
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

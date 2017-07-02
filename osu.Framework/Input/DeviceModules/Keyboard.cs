using System.Linq;
using System.Collections.Generic;
using OpenTK;
using OpenTK.Input;
using osu.Framework.Graphics;
using osu.Framework.Timing;

namespace osu.Framework.Input.DeviceModules
{
    public class Keyboard : IDeviceModule
    {
        InputManager manager;

        private readonly string name = "KEYBOARD";

        /// <summary>
        /// The initial delay before key repeat begins.
        /// </summary>
        private const int repeat_initial_delay = 250;

        /// <summary>
        /// The delay between key repeats after the initial repeat.
        /// </summary>
        private const int repeat_tick_rate = 70;

        private double keyboardRepeatTime;

        Drawable FocusedDrawable;

        /// <summary>
        /// The sequential list in which to handle keyboard input.
        /// </summary>
        private readonly List<Drawable> keyboardInputQueue = new List<Drawable>();

        public string Name => name;

        public Keyboard(InputManager manager)
        {
            this.manager = manager;
        }

        public Drawable FocusTarget()
        {
            return keyboardInputQueue.FirstOrDefault(target => target.RequestsFocus);
        }

        public void ClearQueue()
        {
            //mouseDownInputQueue.Clear();
            keyboardInputQueue.Clear();
        }

        public bool isPresent => true;

        public void SetUpQueue(List<Drawable> d)
        {
            keyboardInputQueue.AddRange(d);
        }

        public IEnumerable<InputState> CreateDistinctInputState(InputState i, InputState last)
        {
            foreach (var releasedKey in last.Keyboard?.Keys.Except(i.Keyboard.Keys) ?? new Key[] { })
            {
                var intermediateState = last.Clone();
                if (intermediateState.Keyboard == null) intermediateState.Keyboard = new KeyboardState();

                intermediateState.Keyboard.Keys = intermediateState.Keyboard.Keys.Where(d => d != releasedKey);

                last = intermediateState;
                yield return intermediateState;
            }

            foreach (var pressedKey in i.Keyboard.Keys.Except(last.Keyboard?.Keys ?? new Key[] { }))
            {
                var intermediateState = last.Clone();
                if (intermediateState.Keyboard == null) intermediateState.Keyboard = new KeyboardState();

                intermediateState.Keyboard.Keys = intermediateState.Keyboard.Keys.Union(new[] { pressedKey });

                last = intermediateState;
                yield return intermediateState;
            }
        }


        public double UpdateEvents(InputState state, InputState lastt, Drawable focused, IFrameBasedClock clock)
        {
            if (state.Keyboard == null) state.Keyboard = new KeyboardState();

            double LastActionTime = clock.TimeInfo.Current;
            KeyboardState keyboard = (KeyboardState)state.Keyboard;
            // >>><<<
            FocusedDrawable = focused;

            if (!keyboard.Keys.Any())
                keyboardRepeatTime = 0;

            var last = state.Last?.Keyboard;

            if (last == null) return -1;

            foreach (var k in last.Keys)
            {
                if (!keyboard.Keys.Contains(k))
                    handleKeyUp(state, k);
            }

            foreach (Key k in keyboard.Keys.Distinct())
            {
                bool isModifier = k == Key.LControl || k == Key.RControl
                                  || k == Key.LAlt || k == Key.RAlt
                                  || k == Key.LShift || k == Key.RShift
                                  || k == Key.LWin || k == Key.RWin;

                

                bool isRepetition = last.Keys.Contains(k);

                if (isModifier)
                {
                    //modifiers shouldn't affect or report key repeat
                    if (!isRepetition)
                        handleKeyDown(state, k, false);
                    continue;
                }

                if (isRepetition)
                {
                    if (keyboardRepeatTime <= 0)
                    {
                        keyboardRepeatTime += repeat_tick_rate;
                        handleKeyDown(state, k, true);
                    }
                }
                else
                {
                    keyboardRepeatTime = repeat_initial_delay;
                    handleKeyDown(state, k, false);
                }
            }

            return LastActionTime;
        }

        private bool handleKeyDown(InputState state, Key key, bool repeat)
        {
            KeyDownEventArgs args = new KeyDownEventArgs
            {
                Key = key,
                Repeat = repeat
            };

            if (!manager.unfocusIfNoLongerValid())
            {
                if (args.Key == Key.Escape)
                {
                    manager.ChangeFocus(null);
                    return true;
                }
                if (FocusedDrawable.TriggerOnKeyDown(state, args))
                    return true;
            }

            return keyboardInputQueue.Any(target => target.TriggerOnKeyDown(state, args));
        }

        private bool handleKeyUp(InputState state, Key key)
        {
            KeyUpEventArgs args = new KeyUpEventArgs
            {
                Key = key
            };

            if (!manager.unfocusIfNoLongerValid() && (FocusedDrawable?.TriggerOnKeyUp(state, args) ?? false))
                return true;

            return keyboardInputQueue.Any(target => target.TriggerOnKeyUp(state, args));
        }
    }
}
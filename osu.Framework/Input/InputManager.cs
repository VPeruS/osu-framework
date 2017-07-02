// Copyright (c) 2007-2017 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-framework/master/LICENCE

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Handlers;
using OpenTK;
using OpenTK.Input;
using osu.Framework.Platform;

namespace osu.Framework.Input
{
    public abstract class InputManager : Container, IRequireHighFrequencyMousePosition
    {
        /// <summary>
        /// The time of the last input action.
        /// </summary>
        public double LastActionTime;

        protected GameHost Host;

        internal Drawable FocusedDrawable;

        protected abstract IEnumerable<IDeviceModule> InputDevices { get; }
        protected abstract IEnumerable<InputHandler> InputHandlers { get; }

        /// <summary>
        /// The last processed state.
        /// </summary>
        public InputState CurrentState = new InputState();

        /// <summary>
        /// Contains all hovered <see cref="Drawable"/>s in top-down order.
        /// Top-down in this case means reverse draw order, i.e. the front-most visible
        /// <see cref="Drawable"/> first, and <see cref="Container"/>s after their children.
        /// </summary>
        public IEnumerable<Drawable> HoveredDrawables => hoveredDrawables;
        private readonly List<Drawable> hoveredDrawables = new List<Drawable>();

        protected InputManager()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader(permitNulls: true)]
        private void load(GameHost host)
        {
            Host = host;
        }

        /// <summary>
        /// Reset current focused drawable to the top-most drawable which is <see cref="Drawable.RequestsFocus"/>.
        /// </summary>
        public void TriggerFocusContention()
        {
            if (FocusedDrawable != this)
                ChangeFocus(null);
        }

        /// <summary>
        /// Changes the currently-focused drawable. First checks that <see cref="potentialFocusTarget"/> is in a valid state to receive focus,
        /// then unfocuses the current <see cref="FocusedDrawable"/> and focuses <see cref="potentialFocusTarget"/>.
        /// <see cref="potentialFocusTarget"/> can be null to reset focus.
        /// If the given drawable is already focused, nothing happens and no events are fired.
        /// </summary>
        /// <param name="potentialFocusTarget">The drawable to become focused.</param>
        /// <returns>True if the given drawable is now focused (or focus is dropped in the case of a null target).</returns>
        public bool ChangeFocus(Drawable potentialFocusTarget) => ChangeFocus(potentialFocusTarget, CurrentState);

        /// <summary>
        /// Changes the currently-focused drawable. First checks that <see cref="potentialFocusTarget"/> is in a valid state to receive focus,
        /// then unfocuses the current <see cref="FocusedDrawable"/> and focuses <see cref="potentialFocusTarget"/>.
        /// <see cref="potentialFocusTarget"/> can be null to reset focus.
        /// If the given drawable is already focused, nothing happens and no events are fired.
        /// </summary>
        /// <param name="potentialFocusTarget">The drawable to become focused.</param>
        /// <param name="state">The <see cref="InputState"/> associated with the focusing event.</param>
        /// <returns>True if the given drawable is now focused (or focus is dropped in the case of a null target).</returns>
        internal bool ChangeFocus(Drawable potentialFocusTarget, InputState state)
        {
            if (potentialFocusTarget == FocusedDrawable)
                return true;

            if (potentialFocusTarget != null && (!potentialFocusTarget.IsPresent || !potentialFocusTarget.AcceptsFocus))
                return false;

            var previousFocus = FocusedDrawable;

            FocusedDrawable = null;

            if (previousFocus != null)
            {
                previousFocus.HasFocus = false;
                previousFocus.TriggerOnFocusLost(state);

                if (FocusedDrawable != null) throw new InvalidOperationException($"Focus cannot be changed inside {nameof(OnFocusLost)}");
            }

            FocusedDrawable = potentialFocusTarget;

            if (FocusedDrawable != null)
            {
                FocusedDrawable.HasFocus = true;
                FocusedDrawable.TriggerOnFocus(state);
            }

            return true;
        }

        internal override bool BuildKeyboardInputQueue(List<Drawable> queue) => false;

        internal override bool BuildMouseInputQueue(Vector2 screenSpaceMousePos, List<Drawable> queue) => false;

        protected override void Update()
        {
            var pendingStates = createDistinctInputStates(GetPendingStates()).ToArray();

            unfocusIfNoLongerValid();

            //we need to make sure the code in the foreach below is run at least once even if we have no new pending states.
            if (pendingStates.Length == 0)
                pendingStates = new[] { new InputState() };

            foreach (var s in pendingStates)
            {
                var last = CurrentState;

                //avoid lingering references that would stay forever.
                last.Last = null;

                CurrentState = s;
                CurrentState.Last = last;

                TransformState(CurrentState);
                updateInputQueues();//CurrentState);

                foreach (var device in InputDevices)
                {
                    if (device.isPresent)
                    {
                        var time = device.UpdateEvents(CurrentState, last, FocusedDrawable, Clock);
                        //if (time > 0)
                        //    LastActionTime = time;
                    }
                }
            }

            if (FocusedDrawable == null)
                focusTopMostRequestingDrawable();

            base.Update();
        }

        /// <summary>
        /// In order to provide a reliable event system to drawables, we want to ensure that we reprocess input queues (via the
        /// main loop in<see cref="updateInputQueues(InputState)"/> after each and every button or key change. This allows
        /// correct behaviour in a case where the input queues change based on triggered by a button or key.
        /// </summary>
        /// <param name="states">A list of <see cref="InputState"/>s</param>
        /// <returns>Processed states such that at most one button change occurs between any two consecutive states.</returns>
        private IEnumerable<InputState> createDistinctInputStates(List<InputState> states)
        {
            InputState last = CurrentState;
 
            foreach (var i in states)
            {
                yield return i;

                foreach (var d in InputDevices)
                {
                    if (i.ByName(d.Name)){
                        foreach (var v in d.CreateDistinctInputState(i, last))
                            yield return v;
                    }
                }
            }
        }

        protected virtual List<InputState> GetPendingStates()
        {
            var pendingStates = new List<InputState>();

            foreach (var h in InputHandlers)
            {
                if (h.IsActive && h.Enabled)
                    pendingStates.AddRange(h.GetPendingStates());
                else
                    h.GetPendingStates();
            }

            return pendingStates;
        }

        protected virtual void TransformState(InputState inputState)
        {
        }

        private void updateInputQueues()//InputState state)
        {
            foreach (var device in InputDevices)
            {
                if (true)//state.ByName(device.Name))
                {
                    List<Drawable> n = new List<Drawable>();

                    device.ClearQueue();

                    foreach (Drawable d in AliveInternalChildren)
                        d.BuildInputQueue(device.Name, n);
                    Console.WriteLine("Child {0}", n.Count);

                    n.Reverse();
                    device.SetUpQueue(n);
                }
            }   
        }

        /// <summary>
        /// Unfocus the current focused drawable if it is no longer in a valid state.
        /// </summary>
        /// <returns>true if there is no longer a focus.</returns>
        internal bool unfocusIfNoLongerValid()
        {
            if (FocusedDrawable == null) return true;

            bool stillValid = FocusedDrawable.IsPresent && FocusedDrawable.Parent != null;

            if (stillValid)
            {
                //ensure we are visible
                IContainer d = FocusedDrawable.Parent;
                while (d != null)
                {
                    if (!d.IsPresent)
                    {
                        stillValid = false;
                        break;
                    }
                    d = d.Parent;
                }
            }

            if (stillValid)
                return false;

            ChangeFocus(null);
            return true;
        }

        private void focusTopMostRequestingDrawable()
        {
            Drawable nextFocused = new FillFlowContainer();
            foreach (var d in InputDevices)
            {
                if (d.FocusTarget() == null)
                {
                    nextFocused = d.FocusTarget();
                    break;
                }
            }
            ChangeFocus(nextFocused);
        }
    }

    public enum ConfineMouseMode
    {
        Never,
        Fullscreen,
        Always
    }
}

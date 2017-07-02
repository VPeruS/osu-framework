using osu.Framework.Graphics;
using System;
using System.Collections.Generic;
using osu.Framework.Graphics.Containers;
using osu.Framework.Allocation;
using osu.Framework.Timing;
using System.Linq;
using OpenTK;
using OpenTK.Input;

namespace osu.Framework.Input.DeviceModules
{
    public class Mouse : IDeviceModule
    {
        private readonly string name = "MOUSE";

        /// <summary>
        /// The maximum time between two clicks for a double-click to be considered.
        /// </summary>
        private const int double_click_time = 250;

        /// <summary>
        /// The distance that must be moved before a drag begins.
        /// </summary>
        private const float drag_start_distance = 0;

        /// <summary>
        /// The distance that must be moved until a dragged click becomes invalid.
        /// </summary>
        private const float click_drag_distance = 40;

        private bool isDragging;

        private bool isValidClick;

        private double lastClickTime;

        private Drawable draggingDrawable;

        private Drawable FocusedDrawable;

        private readonly List<Drawable> hoveredDrawables = new List<Drawable>();
        private Drawable hoverHandledDrawable;

        private List<Drawable> mouseDownInputQueue = new List<Drawable>();

        /// <summary>
        /// The sequential list in which to handle mouse input.
        /// </summary>
        private readonly List<Drawable> mouseInputQueue = new List<Drawable>();

        InputManager manager;

        public Mouse(InputManager manager)
        {
            this.manager = manager;
        }

        public string Name => name;

        public Drawable FocusTarget()
        {
            return mouseInputQueue.FirstOrDefault(target => target.RequestsFocus);
        }

        public void ClearQueue()
        {
            //mouseDownInputQueue.Clear();
            mouseInputQueue.Clear();
        }

        public bool isPresent => true;

        public void SetUpQueue(List<Drawable> d)
        {
            mouseInputQueue.AddRange(d);
            mouseDownInputQueue = new List<Drawable>();
        }

        public IEnumerable<InputState> CreateDistinctInputState(InputState i, InputState last)
        {
            for (MouseButton b = 0; b < MouseButton.LastButton; b++)
            {
                if (i.Mouse.IsPressed(b) != (last.Mouse?.IsPressed(b) ?? false))
                {
                    var intermediateState = last.Clone();
                    if (intermediateState.Mouse == null) intermediateState.Mouse = new MouseState();

                    //add our single local change
                    intermediateState.Mouse.SetPressed(b, i.Mouse.IsPressed(b));

                    last = intermediateState;
                    yield return intermediateState;
                }
            }
        }

        public double UpdateEvents(InputState state, InputState lastt, Drawable focused, IFrameBasedClock clock)
        {

            if (state.Mouse == null)
            {
                state.Mouse = new MouseState();
                return 0;
            }

            FocusedDrawable = focused;
            double LastActionTime = clock.TimeInfo.Current;

            MouseState mouse = (MouseState)state.Mouse;

            var last = state.Last?.Mouse as MouseState;

            if (last == null) return -1;

            if (mouse.Position != last.Position)
            {
                handleMouseMove(state);
                if (isDragging)
                    handleMouseDrag(state);
            }

            for (MouseButton b = 0; b < MouseButton.LastButton; b++)
            {
                var lastPressed = last.IsPressed(b);

                if (lastPressed != mouse.IsPressed(b))
                {
                    if (lastPressed)
                        handleMouseUp(state, b);
                    else
                        handleMouseDown(state, b);
                }
            }

            if (mouse.WheelDelta != 0)
                handleWheel(state);

            if (mouse.HasAnyButtonPressed)
            {
                if (!last.HasAnyButtonPressed)
                {
                    //stuff which only happens once after the mousedown state
                    mouse.PositionMouseDown = state.Mouse.Position;
                    // >>><<<
                    LastActionTime = clock.TimeInfo.Current;

                    if (mouse.IsPressed(MouseButton.Left))
                    {
                        isValidClick = true;

                        if (clock.TimeInfo.Current - lastClickTime < double_click_time)
                        {
                            if (handleMouseDoubleClick(state))
                                //when we handle a double-click we want to block a normal click from firing.
                                isValidClick = false;

                            lastClickTime = 0;
                        }

                        lastClickTime = clock.TimeInfo.Current;
                    }
                }

                if (!isDragging && Vector2.Distance(mouse.PositionMouseDown ?? mouse.Position, mouse.Position) > drag_start_distance)
                {
                    isDragging = true;
                    handleMouseDragStart(state);
                }
            }
            else if (last.HasAnyButtonPressed)
            {
                if (isValidClick && (draggingDrawable == null || Vector2.Distance(mouse.PositionMouseDown ?? mouse.Position, mouse.Position) < click_drag_distance))
                    handleMouseClick(state);

                mouseDownInputQueue = null;
                mouse.PositionMouseDown = null;
                isValidClick = false;

                if (isDragging)
                {
                    isDragging = false;
                    handleMouseDragEnd(state);
                }
            }

            if (state.Mouse != null)
            {
                foreach (var d in mouseInputQueue)
                    if (d is IRequireHighFrequencyMousePosition)
                    if (d.TriggerOnMouseMove(state)) break;
            }

            return LastActionTime;
        }

        private bool handleMouseDown(InputState state, MouseButton button)
        {
            MouseDownEventArgs args = new MouseDownEventArgs
            {
                Button = button
            };

            mouseDownInputQueue = new List<Drawable>(mouseInputQueue);

            return mouseInputQueue.Find(target => target.TriggerOnMouseDown(state, args)) != null;
        }

        private bool handleMouseUp(InputState state, MouseButton button)
        {
            if (mouseDownInputQueue == null) return false;

            MouseUpEventArgs args = new MouseUpEventArgs
            {
                Button = button
            };

            //extra check for IsAlive because we are using an outdated queue.
            return mouseDownInputQueue.Any(target => target.IsAlive && target.IsPresent && target.TriggerOnMouseUp(state, args));
        }

        private bool handleMouseMove(InputState state)
        {
            Console.WriteLine("{0} {1}", state.Mouse.Position.X, state.Mouse.Position.Y);
            Console.WriteLine("Length {0}", mouseInputQueue.Count);
            return mouseInputQueue.Any(target => target.TriggerOnMouseMove(state));
        }

        private bool handleMouseClick(InputState state)
        {
            var intersectingQueue = mouseInputQueue.Intersect(mouseDownInputQueue);

            Drawable focusTarget = null;

            // click pass, triggering an OnClick on all drawables up to the first which returns true.
            // an extra IsHovered check is performed because we are using an outdated queue (for valid reasons which we need to document).
            var clickedDrawable = intersectingQueue.FirstOrDefault(t => t.IsHovered(state.Mouse.Position) && t.TriggerOnClick(state));

            if (clickedDrawable != null)
            {
                focusTarget = clickedDrawable;

                if (!focusTarget.AcceptsFocus)
                {
                    // search upwards from the clicked drawable until we find something to handle focus.
                    Drawable previousFocused = FocusedDrawable;

                    while (focusTarget?.AcceptsFocus == false)
                        focusTarget = focusTarget.Parent as Drawable;

                    if (focusTarget != null && previousFocused != null)
                    {
                        // we found a focusable target above us.
                        // now search upwards from previousFocused to check whether focusTarget is a common parent.
                        Drawable search = previousFocused;
                        while (search != null && search != focusTarget)
                            search = search.Parent as Drawable;

                        if (focusTarget == search)
                            // we have a common parent, so let's keep focus on the previously focused target.
                            focusTarget = previousFocused;
                    }
                }
            }

            manager.ChangeFocus(focusTarget, state);
            return clickedDrawable != null;
        }

        private bool handleMouseDoubleClick(InputState state)
        {
            return mouseInputQueue.Any(target => target.TriggerOnDoubleClick(state));
        }

        private bool handleMouseDrag(InputState state)
        {
            //Once a drawable is dragged, it remains in a dragged state until the drag is finished.
            return draggingDrawable?.TriggerOnDrag(state) ?? false;
        }

        private bool handleMouseDragStart(InputState state)
        {
            draggingDrawable = mouseDownInputQueue?.FirstOrDefault(target => target.IsAlive && target.TriggerOnDragStart(state));
            return draggingDrawable != null;
        }

        private bool handleMouseDragEnd(InputState state)
        {
            if (draggingDrawable == null)
                return false;

            bool result = draggingDrawable.TriggerOnDragEnd(state);
            draggingDrawable = null;

            return result;
        }

        private bool handleWheel(InputState state)
        {
            return mouseInputQueue.Any(target => target.TriggerOnWheel(state));
        }

        private void updateHoverEvents(InputState state)
        {
            Drawable lastHoverHandledDrawable = hoverHandledDrawable;
            hoverHandledDrawable = null;

            List<Drawable> lastHoveredDrawables = new List<Drawable>(hoveredDrawables);
            hoveredDrawables.Clear();

            // First, we need to construct hoveredDrawables for the current frame
            foreach (Drawable d in mouseInputQueue)
            {
                hoveredDrawables.Add(d);

                // Don't need to re-hover those that are already hovered
                if (d.Hovering)
                {
                    // Check if this drawable previously handled hover, and assume it would once more
                    if (d == lastHoverHandledDrawable)
                    {
                        hoverHandledDrawable = lastHoverHandledDrawable;
                        break;
                    }

                    continue;
                }

                d.Hovering = true;
                if (d.TriggerOnHover(state))
                {
                    hoverHandledDrawable = d;
                    break;
                }
            }

            // Unhover all previously hovered drawables which are no longer hovered.
            foreach (Drawable d in lastHoveredDrawables.Except(hoveredDrawables))
            {
                d.Hovering = false;
                d.TriggerOnHoverLost(state);
            }
        }
    }
}
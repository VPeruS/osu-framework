using osu.Framework.Graphics;
using osu.Framework.Timing;
using System.Collections.Generic;

namespace osu.Framework.Input
{
    // Every device has internal Query for drawables that require input
    public interface IDeviceModule
    {
        bool isPresent { get; }
        string Name { get; }

        IEnumerable<InputState> CreateDistinctInputState(InputState i, InputState last);

        double UpdateEvents(InputState state, InputState last, Drawable focused, IFrameBasedClock clock);

        void ClearQueue();
        void SetUpQueue(List<Drawable> d);

        Drawable FocusTarget();
    }
}
// Copyright (c) 2007-2017 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-framework/master/LICENCE

using osu.Framework.Threading;
using osu.Framework.Timing;

namespace osu.Framework.Graphics.Transforms
{
    public abstract class Transform<TValue, T> : ITransform<T>
    {
        public double StartTime { get; set; }
        public double EndTime { get; set; }

        public TValue StartValue { get; protected set; }
        public TValue EndValue { get; set; }

        private double loopDelay;
        private int loopCount;
        private int currentLoopCount;

        public EasingTypes Easing;

        public long CreationID { get; private set; }

        public double Duration => EndTime - StartTime;

        public FrameTimeInfo? Time { get; private set; }

        private static readonly AtomicCounter creation_counter = new AtomicCounter();

        public Transform()
        {
            CreationID = creation_counter.Increment();
        }

        public void UpdateTime(FrameTimeInfo time)
        {
            Time = time;
        }

        public void Loop(double delay, int loopCount = -1)
        {
            loopDelay = delay;
            this.loopCount = loopCount;
        }

        public bool HasNextIteration => Time?.Current > EndTime && loopCount != currentLoopCount;

        public abstract void Apply(T d);

        public abstract void ReadIntoStartValue(T d);

        public override string ToString()
        {
            return string.Format("Transform({2}) {0}-{1}", StartTime, EndTime, typeof(TValue));
        }

        public void NextIteration()
        {
            currentLoopCount++;
            double duration = Duration;
            StartTime = EndTime + loopDelay;
            EndTime = StartTime + duration;
        }
    }
}

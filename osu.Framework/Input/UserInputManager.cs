// Copyright (c) 2007-2017 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-framework/master/LICENCE

using System.Collections.Generic;
using osu.Framework.Input.Handlers;
using System.Linq;

namespace osu.Framework.Input
{
    public class UserInputManager : InputManager
    {
        public UserInputManager()
        {
            inputDevices.Add(new osu.Framework.Input.DeviceModules.Mouse(this));
            inputDevices.Add(new osu.Framework.Input.DeviceModules.Keyboard(this));
        }

        List<IDeviceModule> inputDevices = new List<IDeviceModule>();

        protected override IEnumerable<InputHandler> InputHandlers => Host.AvailableInputHandlers;

        protected override IEnumerable<IDeviceModule> InputDevices => inputDevices;
    }
}

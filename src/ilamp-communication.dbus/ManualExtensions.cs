using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Tmds.DBus;

namespace bluez.DBus
{
    [DBusInterface("org.bluez.Profile1")]
    public interface IProfile1
    {
        void Release();
        void NewConnection(ObjectPath device, SafeFileHandle fd, IDictionary<string, object> properties);
        void RequestDisconnection(ObjectPath device);
    }


    [DBusInterface("org.bluez.Agent1")]
    public interface IAgent1
    {
        void Release();
        string RequestPinCode(ObjectPath device);
        void DisplayPinCode(ObjectPath device, string pinCode);
        uint RequestPasskey(ObjectPath device);
        void DisplayPasskey(ObjectPath device, uint passkey, ushort entered);
        void RequestConfirmation(ObjectPath device, uint passkey);
        void RequestAuthorization(ObjectPath device);
        void AuthorizeService(ObjectPath device, string uuid);
        void Cancel();
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Tmds.DBus;
using System.IO;

namespace bluez.DBus
{
    [DBusInterface("org.bluez.Profile1")]
    public interface IProfile1 : IDBusObject
    {
        Task ReleaseAsync();
        Task NewConnectionAsync(ObjectPath device, SafeFileHandle fd, IDictionary<string, object> properties);
        Task RequestDisconnectionAsync(ObjectPath device);
    }


    [DBusInterface("org.bluez.Agent1")]
    public interface IAgent1 : IDBusObject
    {
        Task ReleaseAsync();
        Task<string> RequestPinCodeAsync(ObjectPath device);
        Task DisplayPinCodeAsync(ObjectPath device, string pinCode);
        Task<uint> RequestPasskeyAsync(ObjectPath device);
        Task DisplayPasskeyAsync(ObjectPath device, uint passkey, ushort entered);
        Task RequestConfirmationAsync(ObjectPath device, uint passkey);
        Task RequestAuthorizationAsync(ObjectPath device);
        Task AuthorizeServiceAsync(ObjectPath device, string uuid);
        Task CancelAsync();
    }
}


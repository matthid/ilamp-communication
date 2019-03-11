using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Tmds.DBus;
using System.IO;
using System.Runtime.InteropServices;

namespace bluez.DBus
{
    /// <summary>
    /// Generic file descriptor SafeHandle.
    /// </summary>
    public class ClosableSafeHandle : SafeHandle
    {
        /// <summary>
        /// Creates a new CloseSafeHandle.
        /// </summary>
        /// <param name="preexistingHandle">An IntPtr object that represents the pre-existing handle to use.</param>
        /// <param name="ownsHandle"><c>true</c> to reliably release the handle during the finalization phase; <c>false</c> to prevent reliable release.</param>
        public ClosableSafeHandle(IntPtr preexistingHandle, bool ownsHandle)
            : base(new IntPtr(-1), ownsHandle)
        {
            SetHandle(preexistingHandle);
        }

        /// <summary>
        /// Gets a value that indicates whether the handle is invalid.
        /// </summary>
        public override bool IsInvalid
        {
            get { return handle == new IntPtr(-1); }
        }

        /// <summary>
        /// When overridden in a derived class, executes the code required to free the handle.
        /// </summary>
        protected override bool ReleaseHandle()
        {
            return MyReleaseHandle();
        }

        internal bool MyReleaseHandle()
        {
            Console.WriteLine("HANDLE WAS CLOSED!!");
            return close(handle.ToInt32()) == 0;
        }

        [DllImport("libc", SetLastError = true)]
        internal static extern int close(int fd);
    }

    /// <summary>
    /// Generic file descriptor SafeHandle.
    /// </summary>
    public class MyUnclosableSafeHandle : SafeHandle
    {
        private ClosableSafeHandle rent;

        /// <summary>
        /// Creates a new CloseSafeHandle.
        /// </summary>
        /// <param name="preexistingHandle">An IntPtr object that represents the pre-existing handle to use.</param>
        /// <param name="ownsHandle"><c>true</c> to reliably release the handle during the finalization phase; <c>false</c> to prevent reliable release.</param>
        public MyUnclosableSafeHandle(IntPtr preexistingHandle, bool ownsHandle)
            : base(new IntPtr(-1), ownsHandle)
        {
            this.rent = new ClosableSafeHandle(preexistingHandle, ownsHandle);

            SetHandle(preexistingHandle);
        }

        public ClosableSafeHandle MakeUnclosable()
        {
            ClosableSafeHandle bk = rent;
            rent = null;
            return bk;
        }

        /// <summary>
        /// Gets a value that indicates whether the handle is invalid.
        /// </summary>
        public override bool IsInvalid
        {
            get { return handle == new IntPtr(-1); }
        }

        /// <summary>
        /// When overridden in a derived class, executes the code required to free the handle.
        /// </summary>
        protected override bool ReleaseHandle()
        {
            return rent?.MyReleaseHandle() ?? true;
        }
    }

    [DBusInterface("org.bluez.Profile1")]
    public interface IProfile1 : IDBusObject
    {
        Task ReleaseAsync();
        Task NewConnectionAsync(ObjectPath device, MyUnclosableSafeHandle fd, IDictionary<string, object> properties);
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


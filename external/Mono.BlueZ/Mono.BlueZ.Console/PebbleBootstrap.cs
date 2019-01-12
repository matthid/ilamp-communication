﻿using System;
using System.Threading;
using System.Globalization;
using System.Linq;
using DBus;
using Mono.BlueZ.DBus;
using org.freedesktop.DBus;
using System.Collections.Generic;
using System.IO;

namespace Mono.BlueZ.Console
{
	public class PebbleBootstrap
	{
		private Bus _system;
		//private Bus _session;
		public Exception _startupException{ get; private set; }
		private ManualResetEvent _started = new ManualResetEvent(false);
	
		public PebbleBootstrap()
		{
			// Run a message loop for DBus on a new thread.
			var t = new Thread(DBusLoop);
			t.IsBackground = true;
			t.Start();
			_started.WaitOne(60 * 1000);
			_started.Close();
			if (_startupException != null) 
			{
				throw _startupException;
			}
			else 
			{
				//System.Console.WriteLine ("Bus connected at " + _system.UniqueName);
			}
		}

		public void Run(bool doDiscovery,string adapterName)
		{
			var devices = new List<Pebble> ();
			string Service = "org.bluez";
			//Important!: there is a flaw in dbus-sharp such that you can only register one interface
			//at each path, so we have to put these at 2 seperate paths, otherwise I'd probably just put them 
			//both at root
			var agentPath = new ObjectPath ("/agent");
			var profilePath = new ObjectPath ("/profiles");
			var blueZPath = new ObjectPath ("/org/bluez");

			//still don't really understand this.  The pebble advertises this uuid,
			//and this is what works, but all my reading tells me that the uuid for the 
			//serial service should be the 2nd one
			string pebbleSerialUUID = "00000000-deca-fade-deca-deafdecacaff";
			//string serialServiceUUID = "00001101-0000-1000-8000-00805F9B34FB";

			//these properties are defined by bluez in /doc/profile-api.txt
			//but it turns out the defaults work just fine
			var properties = new Dictionary<string,object> ();
			//properties ["AutoConnect"] = true;
			//properties ["Name"] = "Serial Port";
			//properties ["Service"] = pebbleSerialUUID;
			//properties ["Role"] = "client";
			//properties ["PSM"] = (ushort)1;
			//properties ["RequireAuthentication"] = false;
			//properties ["RequireAuthorization"] = false;
			//properties ["Channel"] = (ushort)1;

			//get a proxy for the profile manager so we can register our profile
			var profileManager = GetObject<ProfileManager1> (Service, blueZPath);
			//create and register our profile
			var profile = new DemoProfile ();
			_system.Register (profilePath, profile);
			profileManager.RegisterProfile (profilePath
				, pebbleSerialUUID
				, properties);
			System.Console.WriteLine ("Registered profile with BlueZ");

			//get a copy of the object manager so we can browse the "tree" of bluetooth items
			var manager = GetObject<org.freedesktop.DBus.ObjectManager> (Service, ObjectPath.Root);
			//register these events so we can tell when things are added/removed (eg: discovery)
			manager.InterfacesAdded += (p, i) => {
				System.Console.WriteLine (p + " Discovered");
			};
			manager.InterfacesRemoved += (p, i) => {
				System.Console.WriteLine (p + " Lost");
			};

			//get the agent manager so we can register our agent
			var agentManager = GetObject<AgentManager1> (Service, blueZPath);
			var agent = new DemoAgent ();
			//register our agent and make it the default
			_system.Register (agentPath, agent);
			agentManager.RegisterAgent (agentPath, "KeyboardDisplay");
			agentManager.RequestDefaultAgent (agentPath);

			try
			{
				//get the bluetooth object tree
				var managedObjects = manager.GetManagedObjects();
				//find our adapter
				ObjectPath adapterPath = null;
				foreach (var obj in managedObjects.Keys) {
					if (managedObjects [obj].ContainsKey (typeof(Adapter1).DBusInterfaceName ())) {
						if (string.IsNullOrEmpty(adapterName) || obj.ToString ().EndsWith (adapterName)) {
							System.Console.WriteLine ("Adapter found at" + obj);
							adapterPath = obj;
							break;
						}
					}
				}

				if (adapterPath == null) {
					System.Console.WriteLine ("Couldn't find adapter " + adapterName);
					return;
				}

				//get a dbus proxy to the adapter
				var adapter = GetObject<Adapter1> (Service, adapterPath);

				if(doDiscovery)
				{
					//scan for any new devices
					System.Console.WriteLine("Starting Discovery...");
					adapter.StartDiscovery ();
					Thread.Sleep(5000);//totally arbitrary constant, the best kind
					//Thread.Sleep ((int)adapter.DiscoverableTimeout * 1000);

					//refresh the object graph to get any devices that were discovered
					//arguably we should do this in the objectmanager added/removed events and skip the full
					//refresh, but I'm lazy.
					managedObjects = manager.GetManagedObjects();
				}


				foreach (var obj in managedObjects.Keys) {
					if (obj.ToString ().StartsWith (adapterPath.ToString ())) {
						if (managedObjects [obj].ContainsKey (typeof(Device1).DBusInterfaceName ())) {

							var managedObject = managedObjects [obj];
							var name = (string)managedObject[typeof(Device1).DBusInterfaceName()]["Name"];

							if (name.StartsWith ("Pebble")) {
								System.Console.WriteLine ("Device " + name + " at " + obj);
								var device = _system.GetObject<Device1> (Service, obj);
								devices.Add (new Pebble(){Name=name,Device=device,Path=obj});

								if (!device.Paired) {
									System.Console.WriteLine (name + " not paired, attempting to pair");
									device.Pair ();
									System.Console.WriteLine ("Paired");
								}
								if (!device.Trusted) {
									System.Console.WriteLine (name + " not trust, attempting to trust");
									device.Trusted=true;
									System.Console.WriteLine ("Trusted");
								}
							}
						}
					}
				}

				profile.NewConnectionAction=(path,fd,props)=>{
					var pebble = devices.Single(x=>x.Path==path);
					System.Console.WriteLine("Received FD "+fd+" for "+pebble.Name);
					fd.SetBlocking();
					System.Console.WriteLine(props.Count+" properties");
					foreach(var k in props.Keys)
					{
						System.Console.WriteLine(k+":"+props[k]);
					}

					pebble.FileDescriptor = fd;
					pebble.Stream = fd.OpenAsStream(true);

					System.Console.WriteLine("Saying Hello...");
					var pebbleHello = PebbleHello();

					pebble.Stream.Write(pebbleHello,0,pebbleHello.Length);
					pebble.Stream.Flush();
				};

				foreach (var device in devices) {
					try{
						System.Console.WriteLine("Attempting Connection to "+device.Name);
						System.Console.WriteLine("Paired:"+device.Device.Paired.ToString());
						System.Console.WriteLine("Trusted:"+device.Device.Trusted.ToString());
						System.Console.WriteLine("UUIDs:");
						foreach(var uuid in device.Device.UUIDs)
						{
							System.Console.WriteLine(uuid);
						}

						System.Console.WriteLine("Connecting...");
						device.Device.ConnectProfile(pebbleSerialUUID);
						System.Console.WriteLine("Connected");

					}
					catch(Exception ex){
						System.Console.WriteLine("Bootstrap handler");
						System.Console.WriteLine (ex.Message);
						System.Console.WriteLine(ex.StackTrace);
					}
				}

				while(true){
					byte[] buffer = new byte[72];
					foreach(var pebble in devices)
					{
						int count = pebble.Stream.Read(buffer,0,72);
						System.Console.WriteLine("Received data from "+pebble.Name);
						string read = System.Text.ASCIIEncoding.ASCII.GetString(buffer);
						System.Console.WriteLine(read);
					}
				}
			}
			finally 
			{
				foreach (var pebble in devices) 
				{
					System.Console.WriteLine ("Shutting down " + pebble.Name);
					try{
						System.Console.WriteLine("Closing FD");
					pebble.FileDescriptor.Close ();
					}catch{}
					try{
						System.Console.WriteLine("Disconnecting");
					pebble.Device.Disconnect ();
					}catch{}
				}

				agentManager.UnregisterAgent (agentPath);
				profileManager.UnregisterProfile (profilePath);
			}
		}

		private T GetObject<T>(string busName, ObjectPath path)
		{
			var obj = _system.GetObject<T>(busName, path);
			return obj;
		}
	
		private void DBusLoop()
		{
			try 
			{
				_system=Bus.System;
			} catch (Exception ex) {
				_startupException = ex;
				return;
			} finally {
				_started.Set();
			}

			while (true) {
				_system.Iterate();
			}
		}

		public const byte PEBBLE_CLIENT_VERSION = 2;

		private static byte[] PebbleHello()
		{
			byte[] prefix = { PEBBLE_CLIENT_VERSION, 0xFF, 0xFF, 0xFF, 0xFF };
			byte[] session = GetBytes( (uint)SessionCaps.GAMMA_RAY );
			byte[] remote = GetBytes( (uint)( RemoteCaps.Telephony | RemoteCaps.SMS | RemoteCaps.Android ) );

			byte[] msg = CombineArrays( prefix, session, remote );
			return msg;
		}

		public static byte[] GetBytes( uint value )
		{
			byte[] bytes = BitConverter.GetBytes( value );
			if ( BitConverter.IsLittleEndian )
				Array.Reverse( bytes );
			return bytes;
		}

		public static byte[] CombineArrays( params byte[][] array )
		{
			var rv = new byte[array.Select( x => x.Length ).Sum()];

			for ( int i = 0, insertionPoint = 0; i < array.Length; insertionPoint += array[i].Length, i++ )
				Array.Copy( array[i], 0, rv, insertionPoint, array[i].Length );
			return rv;
		}
	}

	public class Pebble
	{
		public ObjectPath Path{get;set;}
		public string Name{get;set;}
		public Device1 Device{get;set;}
		public FileDescriptor FileDescriptor{get;set;}
		public System.IO.Stream Stream{get;set;}
	}

	[Flags]
	public enum RemoteCaps : uint
	{
		Unknown = 0,
		IOS = 1,
		Android = 2,
		OSX = 3,
		Linux = 4,
		Windows = 5,
		Telephony = 16,
		SMS = 32,
		GPS = 64,
		BTLE = 128,
		// 240? No, that doesn't make sense.  But it's apparently true.
		CameraFront = 240,
		CameraRear = 256,
		Accelerometer = 512,
		Gyro = 1024,
		Compass = 2048
	}

	public enum SessionCaps : uint
	{
		GAMMA_RAY = 0x80000000
	}
}


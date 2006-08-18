// Copyright 2006 Alp Toker <alp@atoker.com>
// This software is made available under the MIT License
// See COPYING for details

using System;
using NDesk.DBus;
using org.freedesktop.DBus;

public class ManagedDBusTest
{
	public static void Main ()
	{
		Connection conn = new Connection ();

		ObjectPath opath = new ObjectPath ("/org/freedesktop/DBus");
		string name = "org.freedesktop.DBus";

		DProxy prox = new DProxy (conn, opath, name, typeof (Bus));
		Bus bus = (Bus)prox.GetTransparentProxy ();

		bus.NameAcquired += delegate (string name) {
			Console.WriteLine ("NameAcquired: " + name);
		};

		bus.Hello ();

		//hack to process the NameAcquired signal synchronously
		conn.HandleMessage (conn.ReadMessage ());

		bus.AddMatch ("type='signal'");
		bus.AddMatch ("type='method_call'");
		bus.AddMatch ("type='method_return'");
		bus.AddMatch ("type='error'");

		while (true) {
			Message msg = conn.ReadMessage ();
			Console.WriteLine ("Message:");
			Console.WriteLine ("\t" + "Type: " + msg.MessageType);
			foreach (HeaderField hf in msg.HeaderFields)
				Console.WriteLine ("\t" + hf.Code + ": " + hf.Value);

			if (msg.Body != null) {
				Console.WriteLine ("\tBody:");
				//System.IO.MemoryStream ms = new System.IO.MemoryStream (msg.Body);
				//System.IO.MemoryStream ms = msg.Body;
				Signature sig = msg.Signature;

				byte[] ts = System.Text.Encoding.ASCII.GetBytes (sig.Value);
				foreach (DType dtype in ts) {
					if (dtype == DType.Invalid)
						continue;
					object arg;
					Message.GetValue (msg.Body, dtype, out arg);
					Console.WriteLine ("\t\t" + dtype + ": " + arg);
				}
			}

			Console.WriteLine ();
		}
	}
}

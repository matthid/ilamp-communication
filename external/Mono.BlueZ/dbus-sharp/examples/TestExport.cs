// Copyright 2006 Alp Toker <alp@atoker.com>
// This software is made available under the MIT License
// See COPYING for details

using System;
using System.Collections.Generic;
using DBus;
using org.freedesktop.DBus;

//NOTE: MarshalByRefObject use is not recommended in new code
//See TestExportInterface.cs for a more current example
public class ManagedDBusTestExport
{
	public static void Main ()
	{
		Bus bus = Bus.Session;

		string bus_name = "org.ndesk.test";
		ObjectPath path = new ObjectPath ("/org/ndesk/test");

		DemoObject demo;

		if (bus.RequestName (bus_name) == RequestNameReply.PrimaryOwner) {
			//create a new instance of the object to be exported
			demo = new DemoObject ();
			bus.Register (path, demo);

			//run the main loop
			while (true)
				bus.Iterate ();
		} else {
			//import a remote to a local proxy
			demo = bus.GetObject<DemoObject> (bus_name, path);
		}

		demo.Say ("Hello world!");
		demo.Say ("Sibérie");
		demo.Say (21);
		demo.SayByteArray (new byte[] {0, 2, 1}, "test string");
		demo.SayByteEnumArray (new BEnum[] {BEnum.Zero, BEnum.Two, BEnum.One}, "test string two");
		Console.WriteLine (demo.EchoCaps ("foo bar"));
		Console.WriteLine (demo.GetEnum ());
		demo.CheckEnum (DemoEnum.Bar);
		demo.CheckEnum (demo.GetEnum ());

		Console.WriteLine ();
		long someLong = demo.GetSomeLong ();
		Console.WriteLine ("someLong: " + someLong);

		Console.WriteLine ();
		ulong someULong = demo.GetSomeULong ();
		Console.WriteLine ("someULong: " + someULong);

		/*
		Console.WriteLine ();
		string outVal;
		demo.ReturnOut (out outVal);
		Console.WriteLine ("outVal: " + outVal);
		*/

		Console.WriteLine ();
		string[] texts = {"one", "two", "three"};
		texts = demo.EchoCapsArr (texts);
		foreach (string text in texts)
			Console.WriteLine (text);

		Console.WriteLine ();
		string[][] arrarr = demo.ArrArr ();
		Console.WriteLine (arrarr[1][0]);

		Console.WriteLine ();
		int[] vals = demo.TextToInts ("1 2 3");
		foreach (int val in vals)
			Console.WriteLine (val);

		Console.WriteLine ();
		MyTuple fooTuple = demo.GetTuple ();
		Console.WriteLine ("A: " + fooTuple.A);
		Console.WriteLine ("B: " + fooTuple.B);

		Console.WriteLine ();
		//KeyValuePair<string,string>[] kvps = demo.GetDict ();
		IDictionary<string,string> dict = demo.GetDict ();
		foreach (KeyValuePair<string,string> kvp in dict)
			Console.WriteLine (kvp.Key + ": " + kvp.Value);

		Console.WriteLine ();
		demo.SomeEvent += delegate (string arg1, object arg2, double arg3, MyTuple mt) {Console.WriteLine ("SomeEvent handler: " + arg1 + ", " + arg2 + ", " + arg3 + ", " + mt.A + ", " + mt.B);};
		demo.SomeEvent += delegate (string arg1, object arg2, double arg3, MyTuple mt) {Console.WriteLine ("SomeEvent handler two: " + arg1 + ", " + arg2 + ", " + arg3 + ", " + mt.A + ", " + mt.B);};
		demo.FireOffSomeEvent ();
		//handle the raised signal
		//bus.Iterate ();

		Console.WriteLine ();
		demo.SomeEvent += HandleSomeEventA;
		demo.FireOffSomeEvent ();
		//handle the raised signal
		//bus.Iterate ();

		Console.WriteLine ();
		demo.SomeEvent -= HandleSomeEventA;
		demo.FireOffSomeEvent ();
		//handle the raised signal
		//bus.Iterate ();

		Console.WriteLine ();
		{
			object tmp = demo.GetArrayOfInts ();
			int[] arr = (int[])tmp;
			Console.WriteLine ("Array of ints as variant: " + arr[0] + " " + arr[1]);
		}

		Console.WriteLine ();
		{
			demo.UseSomeVariant ("hello");
			demo.UseSomeVariant (21);
		}

		Console.WriteLine ();
		{
			Console.WriteLine ("get SomeProp: " + demo.SomeProp);
			demo.SomeProp = 4;
		}
	}

	public static void HandleSomeEventA (string arg1, object arg2, double arg3, MyTuple mt)
	{
		Console.WriteLine ("SomeEvent handler A: " + arg1 + ", " + arg2 + ", " + arg3 + ", " + mt.A + ", " + mt.B);
	}

	public static void HandleSomeEventB (string arg1, object arg2, double arg3, MyTuple mt)
	{
		Console.WriteLine ("SomeEvent handler B: " + arg1 + ", " + arg2 + ", " + arg3 + ", " + mt.A + ", " + mt.B);
	}
}

[Interface ("org.ndesk.test")]
public class DemoObject : MarshalByRefObject
{
	public void Say (string text)
	{
		Console.WriteLine ("string: " + text);
	}

	public void Say (object var)
	{
		Console.WriteLine ("variant: " + var);
	}

	public void SayByteArray (byte[] data, string str)
	{
		for (int i = 0 ; i != data.Length ; i++)
			Console.WriteLine ("data[" + i + "]: " + data[i]);

		Console.WriteLine (str);
	}

	public void SayByteEnumArray (BEnum[] data, string str)
	{
		for (int i = 0 ; i != data.Length ; i++)
			Console.WriteLine ("data[" + i + "]: " + data[i]);

		Console.WriteLine (str);
	}

	public string EchoCaps (string text)
	{
		return text.ToUpper ();
	}

	public long GetSomeLong ()
	{
		return Int64.MaxValue;
	}

	public ulong GetSomeULong ()
	{
		return UInt64.MaxValue;
	}

	public void CheckEnum (DemoEnum e)
	{
		Console.WriteLine (e);
	}

	public DemoEnum GetEnum ()
	{
		return DemoEnum.Bar;
	}

	//this doesn't work yet, except for introspection
	public DemoEnum EnumState
	{
		get {
			return DemoEnum.Bar;
		} set {
			Console.WriteLine ("EnumState prop set to " + value);
		}
	}

	/*
	public void ReturnOut (out string val)
	{
		val = "out value";
	}
	*/

	public string[] EchoCapsArr (string[] texts)
	{
		string[] retTexts = new string[texts.Length];

		for (int i = 0 ; i != texts.Length ; i++)
			retTexts[i] = texts[i].ToUpper ();

		return retTexts;
	}

	public string[][] ArrArr ()
	{
		string[][] ret = new string[2][];

		ret[0] = new string[] {"one", "two"};
		ret[1] = new string[] {"three", "four"};

		return ret;
	}

	public int[] TextToInts (string text)
	{
		string[] parts = text.Split (' ');
		int[] rets = new int[parts.Length];

		for (int i = 0 ; i != parts.Length ; i++)
			rets[i] = Int32.Parse (parts[i]);

		return rets;
	}

	public MyTuple GetTuple ()
	{
		MyTuple tup;

		tup.A = "alpha";
		tup.B = "beta";

		return tup;
	}

	public IDictionary<string,string> GetDict ()
	{
		Dictionary<string,string> dict = new Dictionary<string,string> ();

		dict["one"] = "1";
		dict["two"] = "2";

		return dict;
	}

	/*
	public KeyValuePair<string,string>[] GetDict ()
	{
		KeyValuePair<string,string>[] rets = new KeyValuePair<string,string>[2];

		//rets[0] = new KeyValuePair<string,string> ("one", "1");
		//rets[1] = new KeyValuePair<string,string> ("two", "2");

		rets[0] = new KeyValuePair<string,string> ("second", " from example-service.py");
		rets[1] = new KeyValuePair<string,string> ("first", "Hello Dict");

		return rets;
	}
	*/

	public event SomeEventHandler SomeEvent;

	public void FireOffSomeEvent ()
	{
		Console.WriteLine ("Asked to fire off SomeEvent");

		MyTuple mt;
		mt.A = "a";
		mt.B = "b";

		if (SomeEvent != null) {
			SomeEvent ("some string", 21, 19.84, mt);
			Console.WriteLine ("Fired off SomeEvent");
		}
	}

	public object GetArrayOfInts ()
	{
		int[] arr = new int[2];
		arr[0] = 21;
		arr[1] = 22;

		return arr;
	}

	public void UseSomeVariant (object value)
	{
		Console.WriteLine ("variant value: " + value);
	}

	public int SomeProp
	{
		get {
			return 1;
		}
		set {
			Console.WriteLine ("set prop SomeProp: " + value);
		}
	}
}

public enum DemoEnum
{
	Foo,
	Bar,
}

public enum BEnum : byte
{
	Zero = 0,
	One = 1,
	Two = 2,
}


public struct MyTuple
{
	public string A;
	public string B;
}

public delegate void SomeEventHandler (string arg1, object arg2, double arg3, MyTuple mt);

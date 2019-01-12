using System;
using System.Collections.Generic;

using DBus;
using org.freedesktop.DBus;

namespace Mono.BlueZ.DBus
{
	// exposed by client application to indicate it supports a given profile
	[Interface("org.bluez.GattProfile1")]
	public interface GattProfile1
	{
		void Release();

        IList<string> UUIDs { get; }
	}
}

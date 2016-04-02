using System;

namespace DBus
{
	public class FileDescriptor:IDisposable
	{
		public int FD{get;private set;}
		public bool Owns{ get; private set;}
		public FileDescriptor (int fd)
		{
			FD=fd;
			Owns = true;
		}

		//Do we expose Unix.Stream or just the generic IO.Stream?  Decisions...
		public /*Mono.Unix.Unix*/System.IO.Stream OpenAsStream(bool transferOwnership)
		{
			var stream = new Mono.Unix.UnixStream (FD, transferOwnership);
			Owns = !transferOwnership;
			return stream;
		}

		//TODO: move this somewhere better
		public void SetBlocking()
		{
			Mono.Unix.Native.Syscall.fcntl(FD
				,Mono.Unix.Native.FcntlCommand.F_SETFL
				,Mono.Unix.Native.Syscall.fcntl(FD,
					Mono.Unix.Native.FcntlCommand.F_GETFL,0)
				^ (int)Mono.Unix.Native.OpenFlags.O_NONBLOCK);
		}

		public void Close()
		{
			Mono.Unix.Native.Syscall.close (FD);
		}

		public void Dispose()
		{
			if (Owns) {
				Close ();
			}
		}
	}
}


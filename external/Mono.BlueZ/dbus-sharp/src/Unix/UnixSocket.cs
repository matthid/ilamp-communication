// Copyright 2008 Alp Toker <alp@atoker.com>
// This software is made available under the MIT License
// See COPYING for details

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DBus.Unix
{
	// size_t
	using SizeT = System.UIntPtr;
	// ssize_t
	using SSizeT = System.IntPtr;
	// socklen_t: assumed to be 4 bytes
	// uid_t: assumed to be 4 bytes

	//[StructLayout(LayoutKind.Sequential, Pack=1)]
	unsafe struct IOVector
	{
		public IOVector (IntPtr bbase, int length)
		{
			this.Base = (void*)bbase;
			this.length = (SizeT)length;
		}

		//public IntPtr Base;
		public void* Base;

		public SizeT length;
		public int Length
		{
			get {
				return (int)length;
			} set {
				length = (SizeT)value;
			}
		}
	}

	unsafe class UnixSocket
	{
		internal const string LIBC = "libc";

		// Solaris provides socket functionality in libsocket rather than libc.
		// We use a dllmap in the .config to deal with this.
		internal const string LIBSOCKET = "libc";

		public const short AF_UNIX = 1;
		// FIXME: SOCK_STREAM is 2 on Solaris
		public const short SOCK_STREAM = 1;

		[DllImport (LIBC, CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
		internal static extern IntPtr fork ();

		[DllImport (LIBC, CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
		internal static extern int dup2 (int fd, int fd2);

		[DllImport (LIBC, CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
		internal static extern int open ([MarshalAs(UnmanagedType.LPStr)] string path, int oflag);

		[DllImport (LIBC, CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
		internal static extern IntPtr setsid ();


		[DllImport (LIBC, CallingConvention=CallingConvention.Cdecl, SetLastError=true)]
		internal static extern int close (int fd);

		[DllImport (LIBSOCKET, CallingConvention=CallingConvention.Cdecl, SetLastError=true)]
		protected static extern int socket (int domain, int type, int protocol);

		[DllImport (LIBSOCKET, CallingConvention=CallingConvention.Cdecl, SetLastError=true)]
		protected static extern int connect (int sockfd, byte[] serv_addr, uint addrlen);

		[DllImport (LIBSOCKET, CallingConvention=CallingConvention.Cdecl, SetLastError=true)]
		protected static extern int bind (int sockfd, byte[] my_addr, uint addrlen);

		[DllImport (LIBSOCKET, CallingConvention=CallingConvention.Cdecl, SetLastError=true)]
		protected static extern int listen (int sockfd, int backlog);

		//TODO: this prototype is probably wrong, fix it
		[DllImport (LIBSOCKET, CallingConvention=CallingConvention.Cdecl, SetLastError=true)]
		protected static extern int accept (int sockfd, void* addr, ref uint addrlen);

		//TODO: confirm and make use of these functions
		[DllImport (LIBSOCKET, CallingConvention=CallingConvention.Cdecl, SetLastError=true)]
		protected static extern int getsockopt (int s, int optname, IntPtr optval, ref uint optlen);

		[DllImport (LIBSOCKET, CallingConvention=CallingConvention.Cdecl, SetLastError=true)]
		protected static extern int setsockopt (int s, int optname, IntPtr optval, uint optlen);

		[DllImport (LIBC, CallingConvention=CallingConvention.Cdecl, SetLastError=true)]
		unsafe static extern SSizeT read (int fd, byte* buf, SizeT count);

		[DllImport (LIBC, CallingConvention=CallingConvention.Cdecl, SetLastError=true)]
		unsafe static extern SSizeT write (int fd, byte* buf, SizeT count);

		[DllImport (LIBC, CallingConvention=CallingConvention.Cdecl, SetLastError=true)]
		unsafe static extern SSizeT readv (int fd, IOVector* iov, int iovcnt);

		[DllImport (LIBC, CallingConvention=CallingConvention.Cdecl, SetLastError=true)]
		unsafe static extern SSizeT writev (int fd, IOVector* iov, int iovcnt);

		// Linux
		//[DllImport (LIBC, CallingConvention=CallingConvention.Cdecl, SetLastError=true)]
		//static extern int vmsplice (int fd, IOVector* iov, uint nr_segs, uint flags);

		[DllImport (LIBSOCKET, CallingConvention=CallingConvention.Cdecl, SetLastError=true)]
		public static extern SSizeT recvmsg (int s, void* msg, int flags);

		[DllImport (LIBSOCKET, CallingConvention=CallingConvention.Cdecl, SetLastError=true)]
		public static extern SSizeT sendmsg (int s, void* msg, int flags);

		public int Handle;
		bool ownsHandle = false;

		public UnixSocket (int handle) : this (handle, false)
		{
		}

		public UnixSocket (int handle, bool ownsHandle)
		{
			this.Handle = handle;
			this.ownsHandle = ownsHandle;
			// TODO: SafeHandle?
		}

		public UnixSocket ()
		{
			//TODO: don't hard-code PF_UNIX and SOCK_STREAM or SocketType.Stream
			//AddressFamily family, SocketType type, ProtocolType proto

			int r = socket (AF_UNIX, SOCK_STREAM, 0);
			if (r < 0)
				throw UnixError.GetLastUnixException ();

			Handle = r;
			ownsHandle = true;
		}

		~UnixSocket ()
		{
			if (ownsHandle && Handle > 0)
				Close ();
		}

		protected bool connected = false;

		//TODO: consider memory management
		public void Close ()
		{
			int r = 0;

			do {
				r = close (Handle);
			} while (r < 0 && UnixError.ShouldRetry);

			if (r < 0)
				throw UnixError.GetLastUnixException ();

			Handle = -1;
			connected = false;
		}

		public void Connect()
		{
			int r = 0;

			do {
				r = connect (Handle, null, 0);
			} while (r < 0 && UnixError.ShouldRetry);

			if (r < 0)
				throw UnixError.GetLastUnixException ();

			connected = true;
		}

		//TODO: consider memory management
		public void Connect (byte[] remote_end)
		{
			int r = 0;

			do {
				r = connect (Handle, remote_end, (uint)remote_end.Length);
			} while (r < 0 && UnixError.ShouldRetry);

			if (r < 0)
				throw UnixError.GetLastUnixException ();

			connected = true;
		}

		//assigns a name to the socket
		public void Bind (byte[] local_end)
		{
			int r = bind (Handle, local_end, (uint)local_end.Length);
			if (r < 0)
				throw UnixError.GetLastUnixException ();
		}

		public void Listen (int backlog)
		{
			int r = listen (Handle, backlog);
			if (r < 0)
				throw UnixError.GetLastUnixException ();
		}

		public UnixSocket Accept ()
		{
			byte[] addr = new byte[110];
			uint addrlen = (uint)addr.Length;

			fixed (byte* addrP = addr) {
				int r = 0;

				do {
					r = accept (Handle, addrP, ref addrlen);
				} while (r < 0 && UnixError.ShouldRetry);

				if (r < 0)
					throw UnixError.GetLastUnixException ();

				//TODO: use the returned addr
				//string str = Encoding.Default.GetString (addr, 0, (int)addrlen);
				return new UnixSocket (r, true);
			}
		}

		unsafe public int Read (byte[] buf, int offset, int count)
		{
			fixed (byte* bufP = buf)
				return Read (bufP + offset, count);
		}

		public int Write (byte[] buf, int offset, int count)
		{
			fixed (byte* bufP = buf)
				return Write (bufP + offset, count);
		}

		unsafe public int Read (byte* bufP, int count)
		{
			int r = 0;

			do {
				r = (int)read (Handle, bufP, (SizeT)count);
			} while (r < 0 && UnixError.ShouldRetry);

			if (r < 0) {
				var error = Marshal.GetLastWin32Error ();
				System.Console.WriteLine ("Error " + error);

				throw UnixError.GetLastUnixException ();
			}

			return r;
		}

		public int Write (byte* bufP, int count)
		{
			int r = 0;

			do {
				r = (int)write (Handle, bufP, (SizeT)count);
			} while (r < 0 && UnixError.ShouldRetry);

			if (r < 0)
				throw UnixError.GetLastUnixException ();

			return r;
		}



		public ReceivedMessage RecvMsg ()
		{
//			int r = 0;
//
//			do {
//				r = (int)recvmsg (Handle, bufP, flags);
//			} while (r < 0 && UnixError.ShouldRetry);
//
//			if (r < 0)
//				throw UnixError.GetLastUnixException ();
//
//			return r;

			var buffer2 = new byte[1024*10];
			var cmsg2 = new byte[1024*10];
			var msghdr2 = new Mono.Unix.Native.Msghdr {
				msg_control = cmsg2,
				msg_controllen = cmsg2.Length,
			};
			var result = new ReceivedMessage ();
			fixed (byte* ptr_buffer2 = buffer2) {
				var iovecs2 = new Mono.Unix.Native.Iovec[] {
					new Mono.Unix.Native.Iovec {
						iov_base = (IntPtr) ptr_buffer2,
						iov_len = (ulong) buffer2.Length,
					},
				};
				msghdr2.msg_iov = iovecs2;
				msghdr2.msg_iovlen = 1;
				//System.Console.WriteLine ("recvmsg");
				var ret = Mono.Unix.Native.Syscall.recvmsg (Handle, msghdr2,0);

				if (ret ==-1)  {
					var lastError = Marshal.GetLastWin32Error ();
					System.Console.WriteLine ("Last error " + lastError);
					System.Console.WriteLine ("Description" + Mono.Unix.UnixMarshal.GetErrorDescription((Mono.Unix.Native.Errno)lastError));
					Mono.Unix.UnixMarshal.ThrowExceptionForLastError ();
				}

				result.Read = ret;
				result.Message = buffer2;
			}
				
			#if UNIXFDS
			var fds = new global::System.Collections.Generic.List<int> ();
			for (long offset = Mono.Unix.Native.Syscall.CMSG_FIRSTHDR (msghdr2); offset != -1; offset = Mono.Unix.Native.Syscall.CMSG_NXTHDR (msghdr2, offset)) {
				var recvHdr = Mono.Unix.Native.Cmsghdr.ReadFromBuffer (msghdr2, offset);
				var recvDataOffset = Mono.Unix.Native.Syscall.CMSG_DATA (msghdr2, offset);
				var bytes = recvHdr.cmsg_len - (recvDataOffset - offset);
				//Assert.AreEqual (bytes % sizeof (int), 0);
				var fdCount = bytes / sizeof (int);
				//System.Console.WriteLine("based on struct size, there should be "+fdCount+" fds");
				fixed (byte* ptr = msghdr2.msg_control)
				for (int i = 0; i < fdCount; i++)
					fds.Add (((int*) (ptr + recvDataOffset))[i]);
			}
			if(fds.Count>0)
			{
				result.FileDescriptors = fds.ToArray ();
			}
			#endif
			return result;
		}

		public int SendMsg (void* bufP, int flags)
		{
			int r = 0;

			do {
				r = (int)sendmsg (Handle, bufP, flags);
			} while (r < 0 && UnixError.ShouldRetry);

			if (r < 0)
				throw UnixError.GetLastUnixException ();

			return r;
		}

		public int ReadV (IOVector* iov, int count)
		{
			//FIXME: Handle EINTR here or elsewhere
			//FIXME: handle r != count
			//TODO: check offset correctness

			int r = (int)readv (Handle, iov, count);
			if (r < 0)
				throw UnixError.GetLastUnixException ();

			return r;
		}

		public int WriteV (IOVector* iov, int count)
		{
			//FIXME: Handle EINTR here or elsewhere
			//FIXME: handle r != count
			//TODO: check offset correctness

			int r = (int)writev (Handle, iov, count);
			if (r < 0)
				throw UnixError.GetLastUnixException ();

			return r;
		}

		public int Write (IOVector[] iov, int offset, int count)
		{
			//FIXME: Handle EINTR here or elsewhere
			//FIXME: handle r != count
			//TODO: check offset correctness

			fixed (IOVector* bufP = &iov[offset]) {
				int r = (int)writev (Handle, bufP + offset, count);
				if (r < 0)
					throw UnixError.GetLastUnixException ();

				return r;
			}
		}

		public int Write (IOVector[] iov)
		{
			return Write (iov, 0, iov.Length);
		}

	}

	public class ReceivedMessage
	{
		public long Read;
		public byte[] Message;
		public int[] FileDescriptors;
	}
}

// Learn more about F# at http://fsharp.org

open System
open Mono.BlueZ.DBus
open DBus

type MyProfile () =
    interface Profile1 with
        member x.NewConnection(device, fd, properties) =
            ()
        member x.RequestDisconnection(device) =
            ()
        member x.Release () = ()

[<EntryPoint>]
let main argv =

    Console.WriteLine("Platform: {0}", Environment.OSVersion.Platform);

    printfn "Connecting DBus!"

    let con = new DBusConnection()
    printfn "Get ProfileManager!"
    let profileManager = con.System.GetObject<ProfileManager1>("org.bluez", new ObjectPath("/org/bluez"))

    printfn "Exiting...!"
    0 // return an integer exit code

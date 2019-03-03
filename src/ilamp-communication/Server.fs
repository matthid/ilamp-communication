// Learn more about F# at http://fsharp.org

open System
open bluez.DBus
open Tmds.DBus
open System.IO
open Elmish
open System.Threading
open System.Collections.Generic
open System.Threading.Tasks
open Fable.PowerPack.Keyboard
open System.Buffers
open System.Text
open System.Runtime.InteropServices
open System
open Microsoft.Extensions.Configuration
open Tmds.DBus
open Tmds.DBus
open System

type MacAddress = string
type LampConfigType = HorevoConfig
type LampConfig =
    { Name : string; Type:LampConfigType; Address: MacAddress }

type TypeSafeConfig (config:IConfiguration) =
    let parseType t =
        match t with
        | "Horevo" -> HorevoConfig
        | _ -> failwithf "Unknown config %s" t
    let parseAddress a =
        a
    member x.Lamps =
        let section = config.GetSection "Lamps"
        section.GetChildren()
            |> Seq.map (fun sec ->
                { Name = sec.Key; Type = parseType sec.["Type"]; Address = parseAddress sec.["Address"]})

type Percent = double
type Brightness = Percent // percent 0 ~> 1
type Color = System.Drawing.Color

type IConnectedLamp =
    abstract Id : string
    abstract SetColorRaw : byte -> color:Color -> Async<unit>
    abstract SetColor : brightness:Percent -> color:Color -> Async<unit>
    abstract SetWarmBrightnessRaw : byte -> Async<unit>
    abstract SetWarmBrightness : brightness:Percent -> Async<unit>
    abstract SetWarmColorRaw : byte -> Async<unit>
    abstract SetWarmColor : color:Percent -> Async<unit>
    abstract SetModeRaw : colorMode:byte -> enabled:byte -> Async<unit>
    abstract SetMode : colorMode:bool -> enabled:bool -> Async<unit>

type ILampManager =
    inherit IDisposable
    
    abstract Lamps : IEnumerable<IConnectedLamp> 

type RentMemory (poolMemory, mem) =
    let mutable isDisposed = false
    member x.Memory =
        if isDisposed then raise <| ObjectDisposedException("Already disposed")
        mem
    member x.Dispose() =
        if not isDisposed then
            isDisposed <- true
            ArrayPool.Shared.Return(poolMemory, false)
    interface IDisposable with
        member x.Dispose() = x.Dispose()

let parseHex (sin:string) =
    
    // parse 30:31:32:33:34:35:36:37
    let s = sin.Trim(':')
    let withDel = s.Contains (':')
    let byteLength, isOk =
        if not withDel then s.Length / 2, s.Length % 2 = 0
        else 
            ((s.Length - 2) / 3) + 1, s.Length > 2 && (s.Length - 2) % 3 = 0
    if not isOk then
        raise <| ArgumentException(sprintf "can not parse '%s' as hex string" sin)

    let res = ArrayPool.Shared.Rent byteLength
    let multiplier = if withDel then 3 else 2
    for i in 0 .. byteLength-1 do 
        let byteValue = s.Substring(i * multiplier, 2)
        System.Diagnostics.Debug.Assert(not withDel || i = 0 || s.[i * multiplier - 1] = ':', if i = 0 then "i = 0" else sprintf "s.[i * multiplier - 1] = '%c'" s.[i * multiplier - 1])
        res.[i] <- System.Byte.Parse(byteValue, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture)
    new RentMemory(res, new Memory<byte>(res, 0, byteLength))

let toHexString includeDots (b:ReadOnlyMemory<byte>) =
    let baseLength = b.Length * 2
    let sb = StringBuilder(if includeDots then baseLength + (b.Length - 1) else baseLength)
    let span = b.Span
    for i in 0..b.Length - 1 do
        sb.Append(span.[i].ToString("X2")) |> ignore
        if includeDots && i < b.Length - 1 then sb.Append ':' |> ignore
    sb.ToString()

let tryParseHex (sin:string) =
    try Some (parseHex sin) with _ -> None

module ReflectionBased =
    let rec printEnumerable (d:System.Collections.IEnumerable) : string =
        "[ " + String.Join(";\n  ",
            d |> Seq.cast<obj> |> Seq.map (fun k -> (printValue k).Replace("\n", "\n  "))) + " ]"
    and printKeyValuePair (k, v) =
        let genValue = printValue v
        if genValue.Contains "\n" then
            sprintf "%A ->\n    %s" k (genValue.Replace("\n", "\n    "))
        else sprintf "%A -> %s" k (genValue)
    and printValue (a) : string =
        if isNull a then "<NULL>"
        else
            let t = a.GetType()
            match a with
            | :? System.String as s -> sprintf "%A" s
            | :? System.Collections.IEnumerable as d -> printEnumerable d
            | _ when t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<KeyValuePair<_,_>> ->
                let t = a.GetType()
                let kvpKey = t.GetProperty("Key").GetValue(a, null);
                let kvpValue = t.GetProperty("Value").GetValue(a, null);
                printKeyValuePair (kvpKey, kvpValue)
            | _ -> 
                sprintf "%A" a


type HorevoAgent () =
    let path =  ObjectPath ("/ilamp/bluez/agent")
    member x.ObjectPath = path
    interface IAgent1 with
        member x.ReleaseAsync() =
            printfn "MyAgent/ReleaseAsync"
            Task.CompletedTask
        member x.RequestPinCodeAsync(device) =
            printfn "RequestPinCodeAsync"
            // The return value should be a string of 1-16 characters
            // length. The string can be alphanumeric.
            Task.FromResult "01234567"
        member x.DisplayPinCodeAsync(device, pinCode) =
            printfn "DisplayPinCodeAsync: %s" pinCode
            Task.CompletedTask
        member x.RequestPasskeyAsync(device) =
            printfn "RequestPasskeyAsync"
            // 0-999999
            Task.FromResult (0u)

        member x.DisplayPasskeyAsync(device, passkey, entered) =
            printfn "DisplayPasskeyAsync %A %A" passkey entered
            Task.CompletedTask
        member x.RequestConfirmationAsync(device, passkey) =
            printfn "RequestConfirmationAsync %A" passkey
            Task.CompletedTask

        member x.RequestAuthorizationAsync(device)=
            printfn "RequestAuthorizationAsync"
            Task.CompletedTask
        member x.AuthorizeServiceAsync(device, uuid) =
            printfn "AuthorizeServiceAsync %A" uuid
            Task.CompletedTask

        member x.CancelAsync() =
            printfn "MyAgent.CancelAsync"
            Task.CompletedTask

    interface IDBusObject with
        member x.ObjectPath = x.ObjectPath

type ObjectInfo =
    { Path : ObjectPath
      AdapterProps : Adapter1Properties option
      LEAdvertisingProps : LEAdvertisingManager1Properties option
      DeviceProps : Device1Properties option }
    member x.IsAdapter = x.AdapterProps.IsSome
    member x.IsDevice = x.DeviceProps.IsSome

type LogLevel =
    | Debug
    | Info
    | Warning
    | Error
    | Fatal

type LogMessage = { Message : string; Level : LogLevel; Exception : exn }

let log cont d exn level message =
    d (cont { Exception = exn; Message = message; Level = level })


let trySet (values:IDictionary<_,_>) key f =
    match values.TryGetValue key with
    | true, v -> f v
    | _ -> ()

// let isServer, dbusBus = false, Connection.System
let bluezName = "org.bluez"

let getDevice key (kv:IDictionary<_,IDictionary<_,obj>>) =
    let adapter =
        match kv.TryGetValue "org.bluez.Adapter1" with
        | true, values ->
            let adapterInfo = Adapter1Properties()
            trySet values "Address" (fun e -> adapterInfo.Address <- e :?> _)
            trySet values "AddressType" (fun e -> adapterInfo.AddressType <- e :?> _)
            trySet values "Name" (fun e -> adapterInfo.Name <- e :?> _)
            trySet values "Alias" (fun e -> adapterInfo.Alias <- e :?> _)
            trySet values "Class" (fun e -> adapterInfo.Class <- e :?> _)
            trySet values "Powered" (fun e -> adapterInfo.Powered <- e :?> _)
            trySet values "Discoverable" (fun e -> adapterInfo.Discoverable <- e :?> _)
            trySet values "DiscoverableTimeout" (fun e -> adapterInfo.DiscoverableTimeout <- e :?> _)
            trySet values "Pairable" (fun e -> adapterInfo.Pairable <- e :?> _)
            trySet values "PairableTimeout" (fun e -> adapterInfo.PairableTimeout <- e :?> _)
            trySet values "UUIDs" (fun e -> adapterInfo.UUIDs <- e :?> _)
            trySet values "Modalias" (fun e -> adapterInfo.Modalias <- e :?> _)
            Some adapterInfo
        | _ -> None
    let leadvertising =
        match kv.TryGetValue "org.bluez.LEAdvertisingManager1" with
        | true, values ->
            let advertisingProps = LEAdvertisingManager1Properties()
            trySet values "ActiveInstances" (fun e -> advertisingProps.ActiveInstances <- e :?> _)
            trySet values "SupportedInstances" (fun e -> advertisingProps.SupportedInstances <- e :?> _)
            trySet values "SupportedIncludes" (fun e -> advertisingProps.SupportedIncludes <- e :?> _)
            Some advertisingProps
        | _ -> None
                    
    let device =
        match kv.TryGetValue "org.bluez.Device1" with
        | true, values ->
            let deviceProps = Device1Properties()
            trySet values "Address" (fun e -> deviceProps.Address <- e :?> _)
            trySet values "AddressType" (fun e -> deviceProps.AddressType <- e :?> _)
            trySet values "Name" (fun e -> deviceProps.Name <- e :?> _)
            trySet values "Alias" (fun e -> deviceProps.Alias <- e :?> _)
            trySet values "Appearance" (fun e -> deviceProps.Appearance <- e :?> _)
            trySet values "Paired" (fun e -> deviceProps.Paired <- e :?> _)
            trySet values "Trusted" (fun e -> deviceProps.Trusted <- e :?> _)
            trySet values "Blocked" (fun e -> deviceProps.Blocked <- e :?> _)
            trySet values "LegacyPairing" (fun e -> deviceProps.LegacyPairing <- e :?> _)
            trySet values "Connected" (fun e -> deviceProps.Connected <- e :?> _)
            trySet values "UUIDs" (fun e -> deviceProps.UUIDs <- e :?> _)
            trySet values "Adapter" (fun e -> deviceProps.Adapter <- e :?> _)
            trySet values "ServicesResolved" (fun e -> deviceProps.ServicesResolved <- e :?> _)
            Some deviceProps
        | _ -> None
    
    // TODO: print info if unknown interfaces appear here!

    { Path = key
      AdapterProps = adapter
      LEAdvertisingProps = leadvertising
      DeviceProps = device }

let getDeviceProperties (dbusBus:Connection) (p:ObjectPath) =
    async {
        let device = dbusBus.CreateProxy<IDevice1>(bluezName, p)
        let! props = device.GetAllAsync() |> Async.AwaitTask
        return props
    }

type HorevoProfileCallback =
    | LogMessage of LogMessage
    | ReceivedData of HorevoProfileConnection * RentMemory
    | LampConnected of HorevoProfileConnection
    | LampDisconnected of HorevoProfileConnection
    | Heartbeat of HorevoProfileConnection

and HorevoProfileConnection(dispatch : Dispatch<HorevoProfileCallback>, props:Device1Properties, device:ObjectPath, uuid:Guid, hnd: SafeHandle, stream:Stream, p:HorevoProfile) as x =
    let logExn exn level message = log LogMessage dispatch exn level message
    let log level message = log LogMessage dispatch null level message
    let tokenSource = new CancellationTokenSource()
    let handle = hnd
    let startReaderTask (stream:Stream) (tok:CancellationToken) =
        async {
            let lastBeat = System.Diagnostics.Stopwatch.StartNew() 
            try
                // TODO: change logic to first read header and then the rest...
                while not tok.IsCancellationRequested do
                    let rented = ArrayPool.Shared.Rent(1024)
                    let mem = new Memory<byte>(rented)
                    //printfn "starting to read.."
                    let handleErr (e : Mono.Unix.UnixIOException) = async {
                        //if e.NativeErrorCode = 0 then
                        //    printfn "ReadAsync -> Success exception"
                        //    dispatch(ReceivedBytes(x, new RentMemory(rented, mem)))
                        //    () // Success
                        //else
                            match e.ErrorCode with
                            | Mono.Unix.Native.Errno.EWOULDBLOCK ->
                                // data not available yet
                                //use heartbeat = parseHex "01fe0000510210000000008000000080"
                                // to prevent timeout?
                                do! Async.Sleep 500
                                if (lastBeat.ElapsedMilliseconds > 500L) then
                                    //do! x.Heartbeat()
                                    dispatch (Heartbeat x) // (UserInput "send 01fe0000510210000000008000000080")
                                    lastBeat.Restart()
                            | _ ->
                                raise <| exn("UnixIOException in read task", e) }
                    try
                        let! read = stream.ReadAsync(mem, tok).AsTask() |> Async.AwaitTask
                        if read > 0 then
                            let readHex = toHexString false (Memory.op_Implicit (mem.Slice(0, read)))
                            log LogLevel.Info (sprintf "read '%s' ('%d') bytes of data!" readHex read)
                            dispatch(ReceivedData(x, new RentMemory(rented, mem.Slice(0, read))))
                        else
                            // seems to happen...
                            ()
                    with
                    | :? System.AggregateException as agg ->
                        match agg.Flatten().InnerException with
                        | :? Mono.Unix.UnixIOException as e -> return! handleErr e
                        | a -> raise <| exn("unknown error in read task", a)

                    | :? Mono.Unix.UnixIOException as e ->
                        return! handleErr e

                
                log LogLevel.Info "reading task finished."
                stream.Close()
                stream.Dispose()
            with 
            | e ->
                logExn e LogLevel.Fatal "Reading task failed"
                printfn "reading task crashed: %A" e
        }
        |> Async.StartAsTask

    let readerTask = startReaderTask stream tokenSource.Token

    let sendAsync (r:RentMemory) =
        async {
            try
                try
                    do! stream.WriteAsync(Memory.op_Implicit r.Memory).AsTask() |> Async.AwaitTask
                    do! stream.FlushAsync() |> Async.AwaitTask
                with e ->
                    let msg = sprintf "Error while trying to send '%s'" (toHexString false (Memory.op_Implicit r.Memory))
                    logExn e LogLevel.Error msg
                    raise <| exn (msg, e)
            finally
                r.Dispose()
        }
    
    let sendHex s =
        async {
            use msg = parseHex s
            log LogLevel.Debug (sprintf "Sending '%s' ('%d') bytes!" s msg.Memory.Length)
            do! sendAsync msg
            //str.Write (Span.op_Implicit msg.Memory.Span)
        }

    member x.Device = device
    member x.UUID = uuid
    member x.Profile = p
    member x.SendHex s = sendHex s
    member x.Heartbeat () = sendHex "01fe0000510210000000008000000080"
    member x.InitConnection () = 
        async {
            do! sendHex "3031323334353637"
            do! x.Heartbeat()
        }
    member x.Id = props.Address
    member x.SetColorRaw b (color:Color) =
        let hexCode = sprintf "01fe000051811c0000000000000000000d0a02030c%02x%02x%02x%02x0e0000" b color.R color.G color.B
        sendHex hexCode
    member x.SetColor (b:Percent) color =
        x.SetColorRaw (byte (255. * b)) color   
    
    member x.SetWarmBrightnessRaw (b:Byte) =
        let hexCode = sprintf "01fe00005181180000000000000000000d07010302%02x0e00" b
        sendHex hexCode
    member x.SetWarmBrightness (b:Percent) =
        x.SetWarmBrightnessRaw (byte (16. * b))   

    member x.SetWarmColorRaw (b:Byte) =
        let hexCode = sprintf "01fe00005181180000000000000000000d07010303%02x0e00" b
        sendHex hexCode
    member x.SetWarmColor (color:Percent) =
        x.SetWarmColorRaw (byte (255. * color))   
    
    member x.SetModeRaw (colorMode:byte) (enabled:byte) =
        let hexCode = sprintf "01fe00005181180000000000000000000d07%02x0301%02x0e00" colorMode enabled
        sendHex hexCode
    member x.SetMode (colorMode:bool) (enabled:bool) =
        x.SetModeRaw 
            (if colorMode then 0x02uy else 0x01uy)
            (if enabled then 0x01uy else 0x02uy)

    interface IConnectedLamp with
        member x.Id = x.Id
        member x.SetColorRaw b color = x.SetColorRaw b color
        member x.SetColor brightness color = x.SetColor brightness color
        member x.SetWarmBrightnessRaw b = x.SetWarmBrightnessRaw b
        member x.SetWarmBrightness brightness = x.SetWarmBrightness brightness
        member x.SetWarmColorRaw color = x.SetWarmColorRaw color
        member x.SetWarmColor color = x.SetWarmColor color
        member x.SetModeRaw colorMode enabled = x.SetModeRaw colorMode enabled
        member x.SetMode colorMode enabled = x.SetMode colorMode enabled

    member x.Close() =
        tokenSource.Cancel()
        readerTask.Wait()
        handle.Close()

and HorevoProfile (dbus:Connection, dispatch: Dispatch<HorevoProfileCallback>, uuid:Guid) =
    let logExn exn level message = log LogMessage dispatch exn level message
    let log level message = log LogMessage dispatch null level message
    let connections = ResizeArray<HorevoProfileConnection>()
    let path =  ObjectPath ("/ilamp/bluez/myprofile/" + uuid.ToString().Replace("-", ""))
    member x.ObjectPath = path
    interface IProfile1 with
        member x.ReleaseAsync() = 
            log LogLevel.Info "ReleaseAsync ()"
            Task.CompletedTask
        member x.NewConnectionAsync(device, fd, properties) =
            async {
                try
                    log LogLevel.Info (sprintf "new connection %O => %A" device properties)
                    let real = fd.MakeUnclosable()
                    let hnd = int (real.DangerousGetHandle())
                    let str = new Mono.Unix.UnixStream(hnd, false)
                    let! prop = getDeviceProperties dbus device
                    let con = HorevoProfileConnection(dispatch, prop, device, uuid, real, str, x)
                    connections.Add(con)
                    do! con.InitConnection()
                    dispatch (LampConnected(con))
                with e ->
                    logExn e LogLevel.Error "Error in NewConnectionAsync"
                    raise <| exn("Error in NewConnectionAsync", e)
            }
            |> Async.StartAsTask
            :> Task
        member x.RequestDisconnectionAsync(device) =
            log LogLevel.Info (sprintf "connection closed %O" device)
            for d in connections.FindAll(fun con -> con.Device = device) do
                dispatch(LampDisconnected(d))
                d.Close()
            connections.RemoveAll(fun con -> con.Device = device) |> ignore
            Task.CompletedTask
    interface IDBusObject with
        member x.ObjectPath = x.ObjectPath

    interface ILampManager with
        member x.Dispose () =
            ()
        member x.Lamps = connections |> Seq.map (fun i -> i :> IConnectedLamp)

    member x.TryFindConnection device =
        let results =
            connections.FindAll(fun con -> con.Device = device)
            |> Seq.toList
        if results.Length > 1 then failwithf "expected only one connection but found %d streams for %O" results.Length device
        results |> List.tryHead



type InterfaceAddedRemovedMessage =
    | ObjectConnected of ObjectInfo
    | ObjectDisconnected of ObjectPath

let connectInterface (dbusBus:Connection) (dispatch:Dispatch<InterfaceAddedRemovedMessage>) =
    async {
        let agent = HorevoAgent()
        do! dbusBus.RegisterObjectAsync(agent) |> Async.AwaitTask
        let agentMgr = dbusBus.CreateProxy<IAgentManager1>(bluezName, ObjectPath "/org/bluez")
        do! agentMgr.RegisterAgentAsync(agent.ObjectPath, "KeyboardDisplay") |> Async.AwaitTask
        do! agentMgr.RequestDefaultAgentAsync(agent.ObjectPath) |> Async.AwaitTask
        let manager = dbusBus.CreateProxy<IObjectManager>(bluezName, ObjectPath.Root)
        let! d =
            manager.WatchInterfacesAddedAsync(
                (fun struct (path, dict) ->
                    dispatch (ObjectConnected (getDevice path dict))),
                (fun exn ->
                    System.Console.WriteLine("Error while adding interface: {0}", exn)))
                |> Async.AwaitTask
        let! d =
            manager.WatchInterfacesRemovedAsync(
                (fun struct (path, dict) ->
                    dispatch (ObjectDisconnected (path))),
                (fun exn ->
                    System.Console.WriteLine("Error while removing interface: {0}", exn)))
                |> Async.AwaitTask
        let! d = manager.GetManagedObjectsAsync() |> Async.AwaitTask
        let connectInfo =
            d
            |> Seq.map (fun kv -> getDevice kv.Key kv.Value)
            |> Seq.toList
        return manager, connectInfo
    }

let connectDevice (dbusBus:Connection) (p:ObjectPath) =
    async {
        let device = dbusBus.CreateProxy<IDevice1>(bluezName, p)
        do! device.ConnectAsync() |> Async.AwaitTask
    }
    

let disconnectDevice (dbusBus:Connection) (p:ObjectPath) =
    async {
        let device = dbusBus.CreateProxy<IDevice1>(bluezName, p)
        do! device.DisconnectAsync() |> Async.AwaitTask
    }

let startDiscover (dbusBus:Connection) (p:ObjectPath) =
    async {
        let adapter = dbusBus.CreateProxy<IAdapter1>(bluezName, p)
        do! adapter.StartDiscoveryAsync() |> Async.AwaitTask
    }


let registerHorevoProfile (dbusBus:Connection) dispatch (uuid:Guid) =
    async {
        let profile = new HorevoProfile(dbusBus, dispatch, uuid)
        do! dbusBus.RegisterObjectAsync(profile) |> Async.AwaitTask
        let profileManager = dbusBus.CreateProxy<IProfileManager1>(bluezName, ObjectPath "/org/bluez")
        let dic = new Dictionary<string, obj>()
        //dic.["Channel"] <- uint16 0us
        //dic.["AutoConnect"] <- true
        //dic.["Role"] <- "client"
        //dic.["Name"] <- "SerialPort"
        //dic.["Service"] <- "00000003-0000-1000-8000-00805F9B34FB" // RFCOMM, see https://www.bluetooth.com/specifications/assigned-numbers/service-discovery
        do! profileManager.RegisterProfileAsync(profile.ObjectPath, uuid.ToString(), dic) |> Async.AwaitTask
        return profile
    }


let unregisterHorevoProfile (dbusBus:Connection) (profile:HorevoProfile) =
    async {
        let profileManager = dbusBus.CreateProxy<IProfileManager1>(bluezName, ObjectPath "/org/bluez")
        do! profileManager.UnregisterProfileAsync(profile.ObjectPath) |> Async.AwaitTask
        
        dbusBus.UnregisterObject(profile.ObjectPath)

        (profile :> IDisposable).Dispose()
        return ()
    }

let connectProfile (dbusBus:Connection) (profile:HorevoProfile) (uuid:Guid) (devicePath:ObjectPath) =
    async {
        let device = dbusBus.CreateProxy<IDevice1>(bluezName, devicePath)
        // register dbus if needed
        do! device.ConnectProfileAsync(uuid.ToString()) |> Async.AwaitTask
    }

let pairDevice (dbusBus:Connection) (p:ObjectPath) =
    async {
        let device = dbusBus.CreateProxy<IDevice1>(bluezName, p)
        do! device.PairAsync() |> Async.AwaitTask
        do! device.SetAsync("Trusted", true) |> Async.AwaitTask
    }

let unpairDevice (dbusBus:Connection) (p:ObjectPath) =
    async {
        let device = dbusBus.CreateProxy<IDevice1>(bluezName, p)
        let! adapterPath = device.GetAdapterAsync() |> Async.AwaitTask
        let adapter = dbusBus.CreateProxy<IAdapter1>(bluezName, adapterPath)
        do! adapter.RemoveDeviceAsync(p) |> Async.AwaitTask
    }


let getAdapters (con:Connection) (manager:IObjectManager) =
    async{
        let! d = manager.GetManagedObjectsAsync() |> Async.AwaitTask
        return
            d
            |> Seq.toList
            |> List.map (fun kv -> kv.Key, kv.Value)
            |> List.filter (fun (key, value) ->
                value.ContainsKey "org.bluez.Adapter1")
            |> List.map (fun (key, value) ->
                (key, value), lazy con.CreateProxy<IAdapter1>(bluezName, key), lazy con.CreateProxy<IGattManager1>(bluezName, key))
    }

type ManagerInitState =
    | NotInit
    | InitError of Exception
    | Init of ILampManager

let horevoProfileUuid = Guid.Parse "00001101-0000-1000-8000-00805F9B34FB"
let startConnectionLoop dbusBus profile o =
    async {
        let mutable connected = false
        while not connected do
            try
                printfn "Starting RFCOMM ... %O" o.Path
                do! connectProfile dbusBus profile horevoProfileUuid o.Path
                connected <- true
            with
            | e ->
                printfn "Connection attempt failed, trying again in 5 seconds: %A" e
                do! Async.Sleep 5000
    }

let handleConnection (dbusBus:Connection) (lamps:IDictionary<MacAddress, LampConfig>) profile (o:ObjectInfo) =
    async {
        try
            printfn "ObjectConnected %A" o
            match o.DeviceProps with
            | Some devProps -> 
                match lamps.TryGetValue devProps.Address with
                | true, lampConfig ->
                    // check if we can find the service
                    match lampConfig.Type with
                    | HorevoConfig ->
                        if devProps.UUIDs |> Seq.map Guid.Parse |> Seq.contains horevoProfileUuid |> not then
                            raise <| exn (sprintf "Cannot find horevo uuid '%O' in UUIDs from device %O: %A" horevoProfileUuid o.Path devProps.UUIDs)
                        if not devProps.Paired then
                            printfn "Pairing ... %O" o.Path
                            do! pairDevice dbusBus o.Path
                        //if not devProps.Connected then
                        //    printfn "Connecting ... %O" o.Path
                        //    try
                        //        do! connectDevice dbusBus o.Path
                        //    with
                        //    | e ->
                        //        printfn "connecting failed ... %A " e
                        if not devProps.Trusted then
                            printfn "Device is not trusted! %O" o.Path
                        return! startConnectionLoop dbusBus profile o                    
                | _ -> 
                    printfn "Not found in config... %A" o
            | None -> printfn "No device properties found for %A" o
        with
        | e ->
            printfn "handleConnection failed: %A" e        
    }
    |> Async.Start

let doInitManager (config:TypeSafeConfig) (dbusBus:Connection) =
    async {
        
        let lamps = config.Lamps |> Seq.map (fun l -> l.Address, l) |> dict

        // Connect dbus
        let! c = dbusBus.ConnectAsync() |> Async.AwaitTask
        let connectionName = c.LocalName

        // register dbus profile
        let! profile = registerHorevoProfile dbusBus (function
            | LogMessage msg -> printfn "%A: %s - %A" msg.Level msg.Message msg.Exception
            | ReceivedData (con, mem) -> ()
            | LampConnected con -> ()
            | LampDisconnected con -> ()
            | Heartbeat con -> ()) horevoProfileUuid

        let! mgr, devices = connectInterface dbusBus (function
            | ObjectDisconnected o -> printfn "ObjectDisconnected %A" o
            | ObjectConnected o -> handleConnection dbusBus lamps profile o)

        for dev in devices do
            handleConnection dbusBus lamps profile dev

        return profile :> ILampManager
    }

let createManagerAsync (config:TypeSafeConfig) (dbus:Connection) =
    let mutable realImpl = NotInit
    do  async {
            try
                let! impl = doInitManager config dbus
                realImpl <- Init impl
            with e ->
                realImpl <- InitError e
        }
        |> Async.Start
    
    { new ILampManager with
        member x.Dispose () =
            match realImpl with
            | NotInit -> ()
            | InitError e -> ()
            | Init l -> l.Dispose() 

        member x.Lamps =
            match realImpl with
            | NotInit -> raise <| exn("Application not yet initialized")
            | InitError e -> raise <| exn("Init error", e)
            | Init l -> l.Lamps }


open Microsoft.Extensions
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.DependencyInjection.Extensions
open Microsoft.Extensions.Configuration.Json
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Server
open Microsoft.AspNetCore.Builder.Extensions

open Giraffe
open Giraffe.Core
open Giraffe.ResponseWriters
open FSharp.Control.Tasks.V2.ContextInsensitive
open System

let lamps func (context:Microsoft.AspNetCore.Http.HttpContext) =
    task {
        let mgr = context.GetService<ILampManager>()
        return! json mgr.Lamps func context
    }

let handleLamp f id func (context:Microsoft.AspNetCore.Http.HttpContext) =
    task {
        let mgr = context.GetService<ILampManager>()
        match  tryParseHex id with
        | None ->
            return! RequestErrors.BAD_REQUEST "invalid id" func context
        | Some hex ->
            use _ = hex
            if hex.Memory.Length <> 6 then
                return! RequestErrors.BAD_REQUEST "invalid id" func context
            else
                let macAddr = toHexString true (Memory.op_Implicit hex.Memory)
                match mgr.Lamps |> Seq.tryFind (fun l -> String.Equals(l.Id, macAddr, StringComparison.OrdinalIgnoreCase)) with
                | Some lamp -> return! f lamp func context
                | None -> return! RequestErrors.BAD_REQUEST "id not found" func context
    }
type ResultJson<'a> =
 { Status : string
   Data : 'a}
exception QueryKeyNotFoundException of string
exception InvalidQueryKeyException of string
let handleGetLamp =
    handleLamp (fun lamp func context ->
        task {
            match context.Request.Query.TryGetValue "action" with
            | true, p ->
                let getKey k =
                    match context.Request.Query.TryGetValue k with
                    | true, p -> p.ToString()
                    | _ -> raise <| QueryKeyNotFoundException k
                let getPercent k =
                    match Double.TryParse(getKey k) with
                    | true, d -> d
                    | _ -> raise <| InvalidQueryKeyException k
                let getColor k =
                    match tryParseHex (getKey k) with
                    | Some hex ->
                        use hex = hex
                        if hex.Memory.Length <> 3 then
                            raise <| InvalidQueryKeyException k
                        Color.FromArgb(int hex.Memory.Span.[0], int hex.Memory.Span.[1], int hex.Memory.Span.[2])
                    | None -> raise <| InvalidQueryKeyException k

                try                
                    match p.ToString().ToLowerInvariant() with
                    | "poweroff" ->
                        do! lamp.SetMode false false
                        return! json { Status = "OK"; Data = lamp } func context
                    | "poweron_white" ->
                        do! lamp.SetMode false true
                        return! json { Status = "OK"; Data = lamp } func context
                    | "poweron_color" ->
                        do! lamp.SetMode true true
                        return! json { Status = "OK"; Data = lamp } func context
                    | "set_color" ->
                        let brightness = getPercent "brightness"
                        let color = getColor "color"
                        do! lamp.SetColor brightness color
                        return! json { Status = "OK"; Data = lamp } func context
                    | "set_warm_brightness" ->
                        let brightness = getPercent "brightness"
                        do! lamp.SetWarmBrightness brightness
                        return! json { Status = "OK"; Data = lamp } func context
                    | "set_warm_color" ->
                        let color = getPercent "color"
                        do! lamp.SetWarmColor color
                        return! json { Status = "OK"; Data = lamp } func context
                    | a -> return! RequestErrors.BAD_REQUEST (sprintf "unknown action '%s'" a) func context
                with
                | QueryKeyNotFoundException k ->
                    return! RequestErrors.BAD_REQUEST (sprintf "key '%s' was not found!" k) func context
                | InvalidQueryKeyException k ->
                    return! RequestErrors.BAD_REQUEST (sprintf "key '%s' was not valid!" k) func context
            | _ ->            
                return! json lamp func context
        }
    )

let webApp =
    choose [
        GET >=> routef "/api/lamps/%s" handleGetLamp
        // PUT >=> routef "/lamps/%s" handlePutLamp
        GET >=> route "/api/lamps"   >=> lamps ]

open Saturn
open Saturn.ControllerHelpers.Controller


    

module InternalError =
    open System
    open Giraffe.GiraffeViewEngine
    let layout (ex: Exception) =
        html [_class "has-navbar-fixed-top"] [  
            head [] [
                meta [_charset "utf-8"]
                meta [_name "viewport"; _content "width=device-width, initial-scale=1" ]
                title [] [encodedText "SaturnServer - Error #500"]
            ]
            body [] [
               h1 [] [rawText "ERROR #500"]
               h3 [] [rawText ex.Message]
               h4 [] [rawText ex.Source]
               p [] [rawText ex.StackTrace]
               a [_href "/" ] [rawText "Go back to home page"]
            ]
    ]
module NotFound =
    open Giraffe.GiraffeViewEngine

    let layout =
        html [_class "has-navbar-fixed-top"] [
            head [] [
                meta [_charset "utf-8"]
                meta [_name "viewport"; _content "width=device-width, initial-scale=1" ]
                title [] [encodedText "SaturnServer - Error #404"]
            ]
            body [] [
               h1 [] [rawText "ERROR #404"]
               a [_href "/" ] [rawText "Go back to home page"]
            ]
    ]

let browser = pipeline {
    plug acceptHtml
    plug putSecureBrowserHeaders
    plug fetchSession
    set_header "x-pipeline-type" "Browser"
}

//let defaultView = router {
//    get "/" (htmlView Index.layout)
//    get "/index.html" (redirectTo false "/")
//    get "/default.html" (redirectTo false "/")
//}

let browserRouter = router {
    not_found_handler (htmlView NotFound.layout) //Use the default 404 webpage
    pipe_through browser //Use the default browser pipeline

    //forward "" defaultView //Use the default view
}

//Other scopes may use different pipelines and error handlers
let api = pipeline {
    plug acceptJson
    set_header "x-pipeline-type" "Api"
}
let apiRouter = router {
    //error_handler (text "Api 404")
    pipe_through api

    forward "" webApp
}

let appRouter = router {
    forward "/api" apiRouter
    //forward "" browserRouter
}

let endpointPipe = pipeline {
    plug head
    plug requestId
}

let app (config:TypeSafeConfig) (mgr:ILampManager) = application {
    pipe_through endpointPipe

    error_handler (fun ex _ -> pipeline { render_html (InternalError.layout ex) })
    use_router webApp
    url "http://0.0.0.0:8585/"
    memory_cache
    use_static "static"
    use_gzip
    
    service_config (fun services ->
        services
            .AddSingleton(config)
            .AddSingleton(mgr))
    //use_config (fun _ -> {connectionString = "DataSource=database.sqlite"} ) //TODO: Set development time configuration
}

[<EntryPoint>]
let main argv =

    let configBuilder = 
        Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
    let config = TypeSafeConfig (configBuilder.Build())
    // check config
    ignore (Seq.toList config.Lamps)
    
    printfn "Working directory - %s" (System.IO.Directory.GetCurrentDirectory())
    
    let dbusBus = new Connection(Address.System)
    use mgr = createManagerAsync config dbusBus
    run (app config mgr)
    printfn "exiting..."
    //printfn "waiting for token... "
    //applicationExit.Token.WaitHandle.WaitOne() |> ignore
    
    0 // return an integer exit code
